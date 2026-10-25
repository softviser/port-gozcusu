namespace PortGozcusu;

/// <summary>
/// Listede sağ tıklanan adres/port için çalıştırılabilecek komut.
/// ShowOutput true ise komut ayrı konsol yerine uygulama içindeki çıktı penceresinde çalışır.
/// </summary>
internal sealed record PortAction(string Title, string FileName, string Arguments = "", bool ShowOutput = false);

internal static class PortActions
{
    private static readonly HashSet<int> HttpPorts = new() { 80, 81, 3000, 5000, 8000, 8008, 8080, 8081, 8088, 8090, 8888, 9000, 9090 };
    private static readonly HashSet<int> HttpsPorts = new() { 443, 4443, 8443, 9443, 10443 };

    /// <summary>Porta özel işlemler (RDP, SSH, tarayıcı...).</summary>
    public static List<PortAction> ForPort(string host, int port)
    {
        var list = new List<PortAction>();
        switch (port)
        {
            case 3389:
                list.Add(new("Uzak Masaüstü ile bağlan (RDP)", "mstsc.exe", $"/v:{host}:{port}"));
                break;
            case 22:
                list.Add(new("SSH ile bağlan", "cmd.exe", $"/k ssh -p {port} {host}"));
                break;
            case 23:
                list.Add(new("Telnet ile bağlan", "cmd.exe", $"/k telnet {host} {port}"));
                break;
            case 21:
                list.Add(new("FTP adresini aç", $"ftp://{host}:{port}/"));
                break;
            case 139 or 445:
                return list; // Gezgin ve paylaşım işlemleri adres menüsünde (MainForm)
            case 5900 or 5901:
                list.Add(new("VNC ile bağlan", $"vnc://{host}:{port}"));
                break;
            case 5985 or 5986:
                list.Add(new("PowerShell uzak oturumu aç", "powershell.exe",
                    $"-NoExit -Command Enter-PSSession -ComputerName {host} -Port {port}{(port == 5986 ? " -UseSSL" : "")}"));
                break;
        }

        if (HttpsPorts.Contains(port))
            list.Add(new("Tarayıcıda aç (HTTPS)", port == 443 ? $"https://{host}/" : $"https://{host}:{port}/"));
        else if (HttpPorts.Contains(port))
            list.Add(new("Tarayıcıda aç", port == 80 ? $"http://{host}/" : $"http://{host}:{port}/"));
        else if (list.Count == 0)
            list.Add(new("Tarayıcıda dene (HTTP)", $"http://{host}:{port}/"));

        return list;
    }

    /// <summary>Her adres için geçerli ağ araçları.</summary>
    public static List<PortAction> ForHost(string host) => new()
    {
        new("Ping gönder", "ping.exe", $"-n 4 {host}", ShowOutput: true),
        new("Sürekli ping (ping -t)", "ping.exe", $"-t {host}", ShowOutput: true),
        new("Rota izle (tracert)", "tracert.exe", $"-d {host}", ShowOutput: true),
    };
}
