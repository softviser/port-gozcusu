using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PortGozcusu;

/// <summary>SMB portu açık cihazlardaki paylaşılan klasörleri okur (NetShareEnum).</summary>
internal static class Shares
{
    private const int AccessDenied = 5;
    private const uint SpecialShare = 0x80000000; // C$, ADMIN$, IPC$ gibi yönetim paylaşımları
    private const uint DiskTree = 0, PrintQueue = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHARE_INFO_1
    {
        public string Name;
        public uint Type;
        public string Remark;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string server, int level, out IntPtr buffer, int prefMaxLen,
        out int entriesRead, out int totalEntries, ref int resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    public sealed record Result(List<string> Names, string? Error);

    public static Result List(string host)
    {
        int resume = 0;
        int rc = NetShareEnum($@"\\{host}", 1, out var buffer, -1, out int read, out _, ref resume);
        if (rc != 0)
            return new(new(), rc == AccessDenied ? "erişim yok" : $"okunamadı ({rc})");
        try
        {
            var names = new List<string>();
            int size = Marshal.SizeOf<SHARE_INFO_1>();
            for (int i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<SHARE_INFO_1>(buffer + i * size);
                if ((info.Type & SpecialShare) == 0 && (info.Type & 0xFF) is DiskTree or PrintQueue)
                    names.Add(info.Name);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return new(names, null);
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    /// <summary>Her cihaz için en fazla birkaç saniye bekler; yanıt vermeyen cihaz listede "yanıt yok" olarak kalır.</summary>
    public static async Task<Dictionary<string, Result>> ListAsync(IEnumerable<string> hosts, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, Result>();
        await Parallel.ForEachAsync(hosts,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (host, token) =>
            {
                var work = Task.Run(() => List(host));
                var finished = await Task.WhenAny(work, Task.Delay(5000, token));
                result[host] = finished == work && work.IsCompletedSuccessfully
                    ? work.Result
                    : new(new(), "yanıt yok");
            });
        return new Dictionary<string, Result>(result);
    }
}
