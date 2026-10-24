using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PortGozcusu;

/// <summary>ping, tracert gibi komutları uygulama içinde çalıştırıp çıktısını canlı gösterir.</summary>
internal sealed class CommandOutputForm : Form
{
    private readonly PortAction _action;
    private readonly TextBox _output;
    private readonly Button _btnStop, _btnRerun;
    private Process? _process;

    [DllImport("kernel32.dll")]
    private static extern int GetOEMCP();

    public CommandOutputForm(PortAction action)
    {
        _action = action;
        Text = $"{action.Title} — {action.FileName} {action.Arguments}";
        Icon = AppInfo.AppIcon;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 480);
        MinimumSize = new Size(480, 300);

        _output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10F),
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
        };

        _btnStop = new Button { Text = "Durdur", AutoSize = true };
        _btnRerun = new Button { Text = "Yeniden çalıştır", AutoSize = true, Enabled = false };
        var btnCopy = new Button { Text = "Kopyala", AutoSize = true };
        var btnClose = new Button { Text = "Kapat", AutoSize = true, DialogResult = DialogResult.Cancel };
        _btnStop.Click += (_, _) => Stop();
        _btnRerun.Click += (_, _) => Start();
        btnCopy.Click += (_, _) => { if (_output.TextLength > 0) Clipboard.SetText(_output.Text); };
        btnClose.Click += (_, _) => Close();
        CancelButton = btnClose;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        buttons.Controls.AddRange(new Control[] { btnClose, btnCopy, _btnRerun, _btnStop });

        Controls.Add(_output);
        Controls.Add(buttons);
        Shown += (_, _) => Start();
    }

    private void Start()
    {
        _process?.Dispose();
        _output.Clear();
        Append($"> {_action.FileName} {_action.Arguments}");
        // Konsol programları sistemin OEM kod sayfasıyla yazar (Türkçe Windows'ta 857).
        var oem = Encoding.GetEncoding(GetOEMCP());
        var psi = new ProcessStartInfo(_action.FileName, _action.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
        process.Exited += (_, _) =>
        {
            process.WaitForExit(); // kalan çıktı satırlarının okunmasını bekler
            Append($"\r\n[Bitti — çıkış kodu {process.ExitCode}]");
            Ui(() => { _btnStop.Enabled = false; _btnRerun.Enabled = true; });
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _btnStop.Enabled = true;
            _btnRerun.Enabled = false;
        }
        catch (Exception ex)
        {
            Append("Başlatılamadı: " + ex.Message);
            process.Dispose();
        }
    }

    private void Stop()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { /* zaten kapanmış */ }
    }

    private void Append(string line) => Ui(() => _output.AppendText(line + "\r\n"));

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* pencere kapanıyor */ }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Stop();
        _process?.Dispose();
        base.OnFormClosed(e);
    }
}
