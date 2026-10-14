namespace PortGozcusu;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, AppInfo.MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Zaten çalışan örneğe "pencereni göster" sinyali gönder.
            if (EventWaitHandle.TryOpenExisting(AppInfo.ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        using var form = new MainForm(startMinimized);

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowEventName);
        var listener = new Thread(() =>
        {
            while (showEvent.WaitOne())
            {
                try { form.BeginInvoke(form.ShowMain); }
                catch { break; }
            }
        }) { IsBackground = true, Name = "ShowEventListener" };
        listener.Start();

        Application.Run(form);
    }
}
