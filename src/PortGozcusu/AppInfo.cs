using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace PortGozcusu;

internal static class AppInfo
{
    public const string Name = "Port Gözcüsü";
    public const string Company = "Softviser Yazılım";
    public const string Website = "https://softviser.com.tr";
    public const string Repository = "https://github.com/softviser/port-gozcusu";
    public const string MutexName = "Softviser.PortGozcusu.SingleInstance";
    public const string ShowEventName = "Softviser.PortGozcusu.Show";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0";

    public static Icon AppIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("PortGozcusu.Assets.app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* tarayıcı/gezgin açılamazsa yoksay */ }
    }
}

/// <summary>Veri klasörü: exe yanında portable.txt varsa taşınabilir mod (exe\data), yoksa %APPDATA%.</summary>
internal static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataDir { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Softviser", "PortGozcusu");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string SnapshotFile => Path.Combine(DataDir, "snapshot.json");
    public static string ChangeLogFile => Path.Combine(DataDir, "changes.log");

    static AppPaths() => Directory.CreateDirectory(DataDir);
}

/// <summary>Windows ile başlatma (HKCU\...\Run). Kurulum programı da aynı değeri kullanır.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PortGozcusu";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(ValueName, false);
    }
}
