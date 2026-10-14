using System.Text.Json;

namespace PortGozcusu;

public sealed class AppSettings
{
    /// <summary>Boşsa yerel alt ağ(lar) otomatik algılanır.</summary>
    public string Targets { get; set; } = "";
    public string Ports { get; set; } = "21,22,23,25,53,80,110,135,139,143,443,445,465,587,993,995,1433,1521,1883,3306,3389,5000,5432,5900,6379,8000,8080,8443,9000,9100,27017";
    public int IntervalMinutes { get; set; } = 15;
    public int TimeoutMs { get; set; } = 400;
    public int Parallelism { get; set; } = 256;
    public bool NotifyOnOpened { get; set; } = true;
    public bool NotifyOnClosed { get; set; } = true;
    public bool ScanOnStartup { get; set; } = true;
}

public sealed class PortEntry
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string? HostName { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

public sealed class Snapshot
{
    public DateTime? LastScan { get; set; }
    /// <summary>Anahtar: "ip:port"</summary>
    public Dictionary<string, PortEntry> Open { get; set; } = new();
}

public sealed record PortChange(DateTime Time, bool Opened, string Host, int Port, string? HostName)
{
    public string ToLogLine() =>
        string.Join('\t', Time.ToString("s"), Opened ? "ACILDI" : "KAPANDI", Host, Port, HostName ?? "");

    public static PortChange? FromLogLine(string line)
    {
        var p = line.Split('\t');
        if (p.Length < 4 || !DateTime.TryParse(p[0], out var t) || !int.TryParse(p[3], out var port))
            return null;
        return new PortChange(t, p[1] == "ACILDI", p[2], port, p.Length > 4 && p[4].Length > 0 ? p[4] : null);
    }
}

internal static class Storage
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch { /* bozuk dosya: varsayılana dön */ }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static void AppendChanges(IEnumerable<PortChange> changes) =>
        File.AppendAllLines(AppPaths.ChangeLogFile, changes.Select(c => c.ToLogLine()));

    public static List<PortChange> LoadRecentChanges(int max)
    {
        try
        {
            if (!File.Exists(AppPaths.ChangeLogFile)) return new();
            return File.ReadLines(AppPaths.ChangeLogFile).TakeLast(max)
                .Select(PortChange.FromLogLine).OfType<PortChange>().ToList();
        }
        catch { return new(); }
    }
}

internal static class Services
{
    private static readonly Dictionary<int, string> Known = new()
    {
        [20] = "FTP-Data", [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP", [53] = "DNS",
        [67] = "DHCP", [69] = "TFTP", [80] = "HTTP", [88] = "Kerberos", [110] = "POP3", [111] = "RPCbind",
        [123] = "NTP", [135] = "MS-RPC", [137] = "NetBIOS-NS", [139] = "NetBIOS", [143] = "IMAP",
        [161] = "SNMP", [389] = "LDAP", [443] = "HTTPS", [445] = "SMB", [465] = "SMTPS", [514] = "Syslog",
        [515] = "LPD", [548] = "AFP", [554] = "RTSP", [587] = "SMTP-Submission", [631] = "IPP",
        [636] = "LDAPS", [873] = "rsync", [993] = "IMAPS", [995] = "POP3S", [1080] = "SOCKS",
        [1433] = "MS-SQL", [1521] = "Oracle", [1723] = "PPTP", [1883] = "MQTT", [2049] = "NFS",
        [2375] = "Docker", [2376] = "Docker-TLS", [3000] = "HTTP-Dev", [3306] = "MySQL", [3389] = "RDP",
        [5000] = "HTTP-Alt/UPnP", [5060] = "SIP", [5432] = "PostgreSQL", [5672] = "AMQP",
        [5900] = "VNC", [5985] = "WinRM", [5986] = "WinRM-HTTPS", [6379] = "Redis", [8000] = "HTTP-Alt",
        [8080] = "HTTP-Proxy", [8443] = "HTTPS-Alt", [8883] = "MQTT-TLS", [9000] = "HTTP-Alt",
        [9100] = "Yazıcı (JetDirect)", [9200] = "Elasticsearch", [11211] = "Memcached", [27017] = "MongoDB",
    };

    public static string NameOf(int port) => Known.TryGetValue(port, out var n) ? n : "";
}
