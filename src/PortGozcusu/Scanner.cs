using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PortGozcusu;

internal static class Scanner
{
    public static async Task<List<(IPAddress Host, int Port)>> ScanAsync(
        IReadOnlyList<IPAddress> hosts, IReadOnlyList<int> ports, int timeoutMs, int parallelism,
        IProgress<int>? progress, CancellationToken ct)
    {
        var jobs = hosts.SelectMany(h => ports.Select(p => (Host: h, Port: p)));
        var open = new ConcurrentBag<(IPAddress Host, int Port)>();
        int done = 0;

        await Parallel.ForEachAsync(jobs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct },
            async (job, token) =>
            {
                if (await IsOpenAsync(job.Host, job.Port, timeoutMs, token))
                    open.Add(job);
                int d = Interlocked.Increment(ref done);
                if (d % 64 == 0) progress?.Report(d);
            });

        progress?.Report(done);
        return open.OrderBy(o => IpSortKey(o.Host)).ThenBy(o => o.Port).ToList();
    }

    private static async Task<bool> IsOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        using var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        // Bağlantıyı RST ile kapat: binlerce TIME_WAIT soketi biriktirme.
        socket.LingerState = new LingerOption(true, 0);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await socket.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
    }

    /// <summary>Açık portu olan cihazların adlarını (ters DNS / NetBIOS) çözümler.</summary>
    public static async Task<Dictionary<string, string>> ResolveNamesAsync(IEnumerable<IPAddress> hosts, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, string>();
        await Parallel.ForEachAsync(hosts,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (ip, token) =>
            {
                try
                {
                    var lookup = Dns.GetHostEntryAsync(ip);
                    var finished = await Task.WhenAny(lookup, Task.Delay(1500, token));
                    if (finished == lookup && lookup.IsCompletedSuccessfully)
                    {
                        var name = lookup.Result.HostName;
                        if (!string.IsNullOrWhiteSpace(name) && name != ip.ToString())
                            result[ip.ToString()] = name;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* çözümlenemedi */ }
            });
        return new Dictionary<string, string>(result);
    }

    public static ulong IpSortKey(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return ((ulong)b[0] << 24) | ((ulong)b[1] << 16) | ((ulong)b[2] << 8) | b[3];
    }
}

internal static class TargetParser
{
    public const int MaxHosts = 4096;
    public const long MaxProbes = 1_000_000;
    private static readonly char[] Separators = { ',', ';', ' ', '\n', '\r', '\t' };

    public static List<IPAddress> ParseHosts(string text)
    {
        var seen = new HashSet<uint>();
        var list = new List<IPAddress>();

        void Add(uint value)
        {
            if (seen.Add(value)) list.Add(FromUInt(value));
            if (list.Count > MaxHosts)
                throw new FormatException($"En fazla {MaxHosts} adres taranabilir. Hedef aralığını daraltın.");
        }

        foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Contains('/'))
            {
                var parts = token.Split('/');
                if (parts.Length != 2 || !TryParseV4(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
                    throw new FormatException($"Geçersiz CIDR: {token}");
                if (prefix < 20)
                    throw new FormatException($"{token} çok geniş. En geniş /20 kullanılabilir.");
                uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
                uint network = ip & mask, broadcast = network | ~mask;
                uint first = prefix <= 30 ? network + 1 : network;
                uint last = prefix <= 30 ? broadcast - 1 : broadcast;
                for (uint v = first; v <= last && v >= first; v++) Add(v);
            }
            else if (token.Contains('-'))
            {
                var parts = token.Split('-', 2);
                if (!TryParseV4(parts[0], out var start))
                    throw new FormatException($"Geçersiz aralık: {token}");
                uint end;
                if (byte.TryParse(parts[1], out var lastOctet))
                    end = (start & 0xFFFFFF00) | lastOctet;
                else if (!TryParseV4(parts[1], out end))
                    throw new FormatException($"Geçersiz aralık: {token}");
                if (end < start)
                    throw new FormatException($"Aralık sonu başlangıçtan küçük: {token}");
                if (end - start >= MaxHosts)
                    throw new FormatException($"{token} çok geniş. En fazla {MaxHosts} adres.");
                for (uint v = start; v <= end && v >= start; v++) Add(v);
            }
            else if (TryParseV4(token, out var single))
            {
                Add(single);
            }
            else
            {
                IPAddress[] addresses;
                try { addresses = Dns.GetHostAddresses(token); }
                catch { throw new FormatException($"Ad çözümlenemedi: {token}"); }
                var v4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? throw new FormatException($"IPv4 adresi bulunamadı: {token}");
                Add(ToUInt(v4));
            }
        }

        if (list.Count == 0) throw new FormatException("Taranacak adres yok.");
        return list;
    }

    public static List<int> ParsePorts(string text)
    {
        var set = new SortedSet<int>();
        foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            var parts = token.Split('-', 2);
            if (!int.TryParse(parts[0], out var a) || a is < 1 or > 65535)
                throw new FormatException($"Geçersiz port: {token}");
            int b = a;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out b) || b < a || b > 65535))
                throw new FormatException($"Geçersiz port aralığı: {token}");
            for (int p = a; p <= b; p++) set.Add(p);
        }
        if (set.Count == 0) throw new FormatException("En az bir port girin.");
        return set.ToList();
    }

    private static bool TryParseV4(string s, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(s.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || s.Count(c => c == '.') != 3)
            return false;
        value = ToUInt(ip);
        return true;
    }

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt(uint v) =>
        new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
}

internal static class NetworkInfo
{
    /// <summary>Etkin ağ bağdaştırıcılarının IPv4 alt ağlarını döndürür (çok genişse /24'e daraltılır).</summary>
    public static List<string> GetLocalSubnets()
    {
        var withGateway = new List<string>();
        var others = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = ni.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                    continue;
                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue; // APIPA

                int prefix = Math.Clamp(ua.PrefixLength, 24, 32);
                uint ip = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                uint mask = uint.MaxValue << (32 - prefix);
                uint net = ip & mask;
                var cidr = $"{net >> 24}.{(net >> 16) & 255}.{(net >> 8) & 255}.{net & 255}/{prefix}";
                var target = hasGateway ? withGateway : others;
                if (!target.Contains(cidr)) target.Add(cidr);
            }
        }

        return withGateway.Count > 0 ? withGateway : others;
    }
}
