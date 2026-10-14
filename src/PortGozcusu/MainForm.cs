using System.Net;
using System.Text;

namespace PortGozcusu;

internal sealed class MainForm : Form
{
    private const int MaxChangeRows = 1000;

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TextBox _txtTargets, _txtPorts;
    private readonly NumericUpDown _numInterval, _numTimeout;
    private readonly CheckBox _chkAutostart, _chkNotifyOpened, _chkNotifyClosed, _chkScanOnStartup;
    private readonly Button _btnScan, _btnStop;
    private readonly ListView _lvOpen, _lvChanges;
    private readonly TabControl _tabs;
    private readonly TabPage _tabChanges;
    private readonly ToolStripStatusLabel _lblStatus, _lblNext;
    private readonly ToolStripProgressBar _progress;
    private readonly ToolStripMenuItem _trayScan;

    private AppSettings _settings;
    private Snapshot _snapshot;
    private CancellationTokenSource? _scanCts;
    private DateTime _nextScan = DateTime.MaxValue;
    private bool _allowShow, _exiting, _hideHintShown;

    public MainForm(bool startMinimized)
    {
        _allowShow = !startMinimized;
        _settings = Storage.Load<AppSettings>(AppPaths.SettingsFile);
        _snapshot = Storage.Load<Snapshot>(AppPaths.SnapshotFile);

        Text = $"{AppInfo.Name} {AppInfo.Version} — {AppInfo.Company}";
        Icon = AppInfo.AppIcon;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1040, 700);
        MinimumSize = new Size(820, 520);

        // Menü
        var menu = new MenuStrip();
        var mFile = new ToolStripMenuItem("&Dosya");
        mFile.DropDownItems.Add("Açık portları dışa aktar (CSV)...", null, (_, _) => ExportCsv());
        mFile.DropDownItems.Add("Veri klasörünü aç", null, (_, _) => AppInfo.Open(AppPaths.DataDir));
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add("Ç&ıkış", null, (_, _) => ExitApp());
        var mHelp = new ToolStripMenuItem("&Yardım");
        mHelp.DropDownItems.Add("softviser.com.tr", null, (_, _) => AppInfo.Open(AppInfo.Website));
        mHelp.DropDownItems.Add("GitHub deposu (kaynak kod)", null, (_, _) => AppInfo.Open(AppInfo.Repository));
        mHelp.DropDownItems.Add(new ToolStripSeparator());
        mHelp.DropDownItems.Add("&Hakkında", null, (_, _) => ShowAbout());
        menu.Items.AddRange(new ToolStripItem[] { mFile, mHelp });
        MainMenuStrip = menu;

        // Ayarlar
        _txtTargets = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Boş bırakılırsa yerel alt ağ otomatik algılanır" };
        _txtPorts = new TextBox { Dock = DockStyle.Fill };
        _numInterval = new NumericUpDown { Minimum = 1, Maximum = 1440, Width = 80 };
        _numTimeout = new NumericUpDown { Minimum = 50, Maximum = 10000, Increment = 50, Width = 80 };
        _chkAutostart = new CheckBox { Text = "Windows ile başlat", AutoSize = true };
        _chkScanOnStartup = new CheckBox { Text = "Açılışta tara", AutoSize = true };
        _chkNotifyOpened = new CheckBox { Text = "Yeni açılan portları bildir", AutoSize = true };
        _chkNotifyClosed = new CheckBox { Text = "Kapanan portları bildir", AutoSize = true };

        _btnScan = new Button { Text = "Şimdi Tara", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        _btnStop = new Button { Text = "Durdur", AutoSize = true, Enabled = false, Padding = new Padding(8, 2, 8, 2) };
        var btnSave = new Button { Text = "Ayarları Kaydet", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        var btnAuto = new Button { Text = "Alt ağı algıla", AutoSize = true };
        _btnScan.Click += async (_, _) => { if (ApplyUiToSettings()) await RunScanAsync(manual: true); };
        _btnStop.Click += (_, _) => _scanCts?.Cancel();
        btnSave.Click += (_, _) => { if (ApplyUiToSettings()) SetStatus("Ayarlar kaydedildi."); };
        btnAuto.Click += (_, _) => _txtTargets.Text = string.Join(", ", NetworkInfo.GetLocalSubnets());

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true, Padding = new Padding(4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        table.Controls.Add(MakeLabel("Hedefler:"), 0, 0);
        table.Controls.Add(_txtTargets, 1, 0);
        table.SetColumnSpan(_txtTargets, 3);
        table.Controls.Add(btnAuto, 4, 0);

        table.Controls.Add(MakeLabel("Portlar:"), 0, 1);
        table.Controls.Add(_txtPorts, 1, 1);
        table.SetColumnSpan(_txtPorts, 4);

        var timing = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        timing.Controls.AddRange(new Control[]
        {
            MakeLabel("Tarama aralığı (dk):"), _numInterval,
            MakeLabel("   Zaman aşımı (ms):"), _numTimeout,
        });
        table.Controls.Add(timing, 1, 2);
        table.SetColumnSpan(timing, 4);

        var checks = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        checks.Controls.AddRange(new Control[] { _chkNotifyOpened, _chkNotifyClosed, _chkScanOnStartup, _chkAutostart });
        table.Controls.Add(checks, 1, 3);
        table.SetColumnSpan(checks, 4);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Örnek hedef: 192.168.1.0/24, 10.0.0.5-40, sunucu.local   •   Örnek port: 22,80,443,8000-8100",
        };
        table.Controls.Add(hint, 1, 4);
        table.SetColumnSpan(hint, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        buttons.Controls.AddRange(new Control[] { _btnScan, _btnStop, btnSave });
        table.Controls.Add(buttons, 1, 5);
        table.SetColumnSpan(buttons, 4);

        var group = new GroupBox
        {
            Text = "Tarama Ayarları",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };
        group.Controls.Add(table);

        // Listeler
        _lvOpen = MakeListView(("Adres", 130), ("Cihaz adı", 220), ("Port", 70), ("Servis", 150), ("İlk görülme", 140), ("Son görülme", 140));
        _lvChanges = MakeListView(("Zaman", 140), ("Durum", 90), ("Adres", 130), ("Cihaz adı", 220), ("Port", 70), ("Servis", 150));

        _tabs = new TabControl { Dock = DockStyle.Fill };
        var tabOpen = new TabPage("Açık Portlar");
        tabOpen.Controls.Add(_lvOpen);
        _tabChanges = new TabPage("Değişiklikler");
        _tabChanges.Controls.Add(_lvChanges);
        _tabs.TabPages.Add(tabOpen);
        _tabs.TabPages.Add(_tabChanges);

        // Durum çubuğu
        _lblStatus = new ToolStripStatusLabel("Hazır") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new ToolStripProgressBar { Visible = false, Width = 200 };
        _lblNext = new ToolStripStatusLabel();
        var website = new ToolStripStatusLabel("softviser.com.tr") { IsLink = true };
        website.Click += (_, _) => AppInfo.Open(AppInfo.Website);
        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _lblStatus, _progress, _lblNext, website });

        Controls.Add(_tabs);
        Controls.Add(group);
        Controls.Add(status);
        Controls.Add(menu);

        // Sistem tepsisi
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Göster", null, (_, _) => ShowMain());
        _trayScan = new ToolStripMenuItem("Şimdi tara", null, async (_, _) => await RunScanAsync(manual: false));
        trayMenu.Items.Add(_trayScan);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        _tray = new NotifyIcon
        {
            Icon = AppInfo.AppIcon,
            Text = AppInfo.Name,
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.BalloonTipClicked += (_, _) => { ShowMain(); _tabs.SelectedTab = _tabChanges; };

        _timer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _timer.Tick += async (_, _) =>
        {
            UpdateNextLabel();
            if (_scanCts == null && DateTime.Now >= _nextScan)
                await RunScanAsync(manual: false);
        };

        LoadSettingsToUi();
        RenderOpenPorts();
        foreach (var c in Storage.LoadRecentChanges(MaxChangeRows))
            AddChangeRow(c);

        HandleCreated += OnFirstHandleCreated;
    }

    // Başlangıç / görünürlük

    private void OnFirstHandleCreated(object? sender, EventArgs e)
    {
        HandleCreated -= OnFirstHandleCreated;
        if (_snapshot.LastScan is { } last)
            SetStatus($"Son tarama: {last:g} — {_snapshot.Open.Count} açık port");

        _nextScan = _settings.ScanOnStartup ? DateTime.Now.AddSeconds(2) : DateTime.Now.AddMinutes(_settings.IntervalMinutes);
        UpdateNextLabel();
        _timer.Start();
        // Açılışta tarama: arayüz yüklendikten kısa süre sonra başlasın.
        if (_settings.ScanOnStartup)
            BeginInvoke(async () => { await Task.Delay(1500); await RunScanAsync(manual: false); });
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    public void ShowMain()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_hideHintShown)
            {
                _hideHintShown = true;
                _tray.ShowBalloonTip(3000, AppInfo.Name, "Arka planda izlemeye devam ediyor. Çıkmak için tepsi simgesine sağ tıklayın.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        _scanCts?.Cancel();
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    // Ayarlar

    private void LoadSettingsToUi()
    {
        _txtTargets.Text = _settings.Targets;
        _txtPorts.Text = _settings.Ports;
        _numInterval.Value = Math.Clamp(_settings.IntervalMinutes, (int)_numInterval.Minimum, (int)_numInterval.Maximum);
        _numTimeout.Value = Math.Clamp(_settings.TimeoutMs, (int)_numTimeout.Minimum, (int)_numTimeout.Maximum);
        _chkNotifyOpened.Checked = _settings.NotifyOnOpened;
        _chkNotifyClosed.Checked = _settings.NotifyOnClosed;
        _chkScanOnStartup.Checked = _settings.ScanOnStartup;
        _chkAutostart.Checked = Autostart.IsEnabled();
    }

    private bool ApplyUiToSettings()
    {
        try
        {
            TargetParser.ParsePorts(_txtPorts.Text);
            if (!string.IsNullOrWhiteSpace(_txtTargets.Text) && !_txtTargets.Text.Any(char.IsLetter))
                TargetParser.ParseHosts(_txtTargets.Text); // ad içeren hedefler tarama sırasında çözülür
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        bool intervalChanged = _settings.IntervalMinutes != (int)_numInterval.Value;
        _settings.Targets = _txtTargets.Text.Trim();
        _settings.Ports = _txtPorts.Text.Trim();
        _settings.IntervalMinutes = (int)_numInterval.Value;
        _settings.TimeoutMs = (int)_numTimeout.Value;
        _settings.NotifyOnOpened = _chkNotifyOpened.Checked;
        _settings.NotifyOnClosed = _chkNotifyClosed.Checked;
        _settings.ScanOnStartup = _chkScanOnStartup.Checked;
        Storage.Save(AppPaths.SettingsFile, _settings);

        try { Autostart.Set(_chkAutostart.Checked); }
        catch (Exception ex) { SetStatus("Başlangıç ayarı yazılamadı: " + ex.Message); }

        if (intervalChanged && _scanCts == null)
        {
            var last = _snapshot.LastScan ?? DateTime.Now;
            _nextScan = last.AddMinutes(_settings.IntervalMinutes);
            UpdateNextLabel();
        }
        return true;
    }

    // Tarama

    private async Task RunScanAsync(bool manual)
    {
        if (_scanCts != null) return;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        SetScanning(true);

        try
        {
            string targetsText = string.IsNullOrWhiteSpace(_settings.Targets)
                ? string.Join(", ", NetworkInfo.GetLocalSubnets())
                : _settings.Targets;
            if (string.IsNullOrWhiteSpace(targetsText))
                throw new FormatException("Etkin bir ağ bağlantısı bulunamadı. Hedefleri elle girin.");

            var hosts = await Task.Run(() => TargetParser.ParseHosts(targetsText), ct);
            var ports = TargetParser.ParsePorts(_settings.Ports);
            long total = (long)hosts.Count * ports.Count;
            if (total > TargetParser.MaxProbes)
                throw new FormatException($"Adres × port sayısı çok büyük ({total:N0}). Hedef veya port listesini daraltın.");

            _progress.Maximum = (int)total;
            _progress.Value = 0;
            SetStatus($"Taranıyor: {hosts.Count} adres × {ports.Count} port ({targetsText})");
            var progress = new Progress<int>(d => _progress.Value = Math.Min(d, _progress.Maximum));

            var found = await Scanner.ScanAsync(hosts, ports, _settings.TimeoutMs, _settings.Parallelism, progress, ct);
            SetStatus("Cihaz adları çözümleniyor...");
            var names = await Scanner.ResolveNamesAsync(found.Select(f => f.Host).Distinct(), ct);

            var scopeHosts = hosts.Select(h => h.ToString()).ToHashSet();
            var scopePorts = ports.ToHashSet();
            bool baseline = _snapshot.LastScan == null;
            var changes = ApplyResults(found, names, scopeHosts, scopePorts, DateTime.Now);

            RenderOpenPorts();
            if (changes.Count > 0)
            {
                Storage.AppendChanges(changes);
                foreach (var c in changes) AddChangeRow(c);
                Notify(changes);
            }

            var summary = $"Son tarama: {DateTime.Now:g} — {_snapshot.Open.Count} açık port, {changes.Count} değişiklik";
            SetStatus(summary);
            if (baseline)
                _tray.ShowBalloonTip(5000, AppInfo.Name, $"İlk tarama tamamlandı: {_snapshot.Open.Count} açık port bulundu. Bundan sonraki değişiklikler bildirilecek.", ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Tarama durduruldu.");
        }
        catch (FormatException ex)
        {
            SetStatus("Hata: " + ex.Message);
            if (manual) MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            SetStatus("Beklenmeyen hata: " + ex.Message);
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            _nextScan = DateTime.Now.AddMinutes(_settings.IntervalMinutes);
            SetScanning(false);
            UpdateNextLabel();
        }
    }

    private List<PortChange> ApplyResults(List<(IPAddress Host, int Port)> found, Dictionary<string, string> names,
        HashSet<string> scopeHosts, HashSet<int> scopePorts, DateTime now)
    {
        bool baseline = _snapshot.LastScan == null;
        var newOpen = new Dictionary<string, PortEntry>();
        var changes = new List<PortChange>();

        foreach (var (ip, port) in found)
        {
            var host = ip.ToString();
            var key = $"{host}:{port}";
            names.TryGetValue(host, out var hostName);
            if (_snapshot.Open.TryGetValue(key, out var prev))
            {
                prev.LastSeen = now;
                prev.HostName = hostName ?? prev.HostName;
                newOpen[key] = prev;
            }
            else
            {
                newOpen[key] = new PortEntry { Host = host, Port = port, HostName = hostName, FirstSeen = now, LastSeen = now };
                if (!baseline) changes.Add(new PortChange(now, true, host, port, hostName));
            }
        }

        foreach (var (key, prev) in _snapshot.Open)
        {
            if (newOpen.ContainsKey(key)) continue;
            // Hedef/port listesi değiştiyse kapsam dışı kalan kayıtlar sessizce düşer.
            if (!scopeHosts.Contains(prev.Host) || !scopePorts.Contains(prev.Port)) continue;
            if (!baseline) changes.Add(new PortChange(now, false, prev.Host, prev.Port, prev.HostName));
        }

        _snapshot.Open = newOpen;
        _snapshot.LastScan = now;
        Storage.Save(AppPaths.SnapshotFile, _snapshot);
        return changes;
    }

    private void Notify(List<PortChange> changes)
    {
        var relevant = changes.Where(c => c.Opened ? _settings.NotifyOnOpened : _settings.NotifyOnClosed).ToList();
        if (relevant.Count == 0) return;

        int opened = relevant.Count(c => c.Opened), closed = relevant.Count - opened;
        var title = $"{AppInfo.Name}: {relevant.Count} değişiklik";
        var sb = new StringBuilder();
        foreach (var c in relevant.Take(5))
        {
            var svc = Services.NameOf(c.Port);
            sb.AppendLine($"{(c.Opened ? "▲ AÇILDI" : "▼ KAPANDI")}  {c.Host}:{c.Port}{(svc.Length > 0 ? $" ({svc})" : "")}");
        }
        if (relevant.Count > 5) sb.AppendLine($"... ve {relevant.Count - 5} değişiklik daha");
        var text = sb.ToString().TrimEnd();
        if (text.Length > 250) text = text[..247] + "...";

        _tray.ShowBalloonTip(10000, title, text, closed > 0 && opened == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    // Görünüm

    private void RenderOpenPorts()
    {
        _lvOpen.BeginUpdate();
        _lvOpen.Items.Clear();
        foreach (var e in _snapshot.Open.Values
                     .OrderBy(e => IPAddress.TryParse(e.Host, out var ip) ? Scanner.IpSortKey(ip) : 0)
                     .ThenBy(e => e.Port))
        {
            _lvOpen.Items.Add(new ListViewItem(new[]
            {
                e.Host, e.HostName ?? "", e.Port.ToString(), Services.NameOf(e.Port),
                e.FirstSeen.ToString("g"), e.LastSeen.ToString("g"),
            }));
        }
        _lvOpen.EndUpdate();
        _tabs.TabPages[0].Text = $"Açık Portlar ({_snapshot.Open.Count})";
    }

    private void AddChangeRow(PortChange c)
    {
        var item = new ListViewItem(new[]
        {
            c.Time.ToString("g"), c.Opened ? "▲ Açıldı" : "▼ Kapandı", c.Host, c.HostName ?? "",
            c.Port.ToString(), Services.NameOf(c.Port),
        })
        {
            ForeColor = c.Opened ? Color.FromArgb(176, 64, 0) : Color.FromArgb(0, 110, 60),
        };
        _lvChanges.Items.Insert(0, item);
        while (_lvChanges.Items.Count > MaxChangeRows)
            _lvChanges.Items.RemoveAt(_lvChanges.Items.Count - 1);
        _tabChanges.Text = $"Değişiklikler ({_lvChanges.Items.Count})";
    }

    private void SetScanning(bool scanning)
    {
        _btnScan.Enabled = !scanning;
        _trayScan.Enabled = !scanning;
        _btnStop.Enabled = scanning;
        _progress.Visible = scanning;
        _tray.Text = scanning ? $"{AppInfo.Name} — taranıyor..." : $"{AppInfo.Name} — {_snapshot.Open.Count} açık port";
    }

    private void SetStatus(string text) => _lblStatus.Text = text;

    private void UpdateNextLabel() =>
        _lblNext.Text = _scanCts != null || _nextScan == DateTime.MaxValue ? "" : $"Sonraki tarama: {_nextScan:HH:mm}";

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"acik-portlar-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var sb = new StringBuilder("Adres;Cihaz adı;Port;Servis;İlk görülme;Son görülme\r\n");
        foreach (ListViewItem item in _lvOpen.Items)
            sb.AppendLine(string.Join(';', item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text.Replace(';', ','))));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        SetStatus("Dışa aktarıldı: " + dlg.FileName);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"{AppInfo.Name} {AppInfo.Version}\n\n" +
            "Yerel ağdaki açık TCP portlarını düzenli aralıklarla tarar ve değişiklik olduğunda bildirim verir.\n\n" +
            $"Geliştirici: {AppInfo.Company}\n{AppInfo.Website}\n\n" +
            $"Açık kaynak (MIT Lisansı)\n{AppInfo.Repository}\n\n" +
            $"Veri klasörü{(AppPaths.IsPortable ? " (taşınabilir mod)" : "")}:\n{AppPaths.DataDir}\n\n" +
            "Yalnızca yönetme yetkiniz olan ağları tarayın.",
            "Hakkında", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

    private static ListView MakeListView(params (string Title, int Width)[] columns)
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
        };
        foreach (var (title, width) in columns)
            lv.Columns.Add(title, width);
        return lv;
    }
}
