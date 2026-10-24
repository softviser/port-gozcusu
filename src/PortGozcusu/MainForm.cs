using System.Diagnostics;
using System.Net;
using System.Text;

namespace PortGozcusu;

internal sealed class MainForm : Form
{
    private const int MaxChangeRows = 1000;

    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TextBox _txtTargets, _txtPorts, _txtFilter;
    private readonly NumericUpDown _numInterval, _numTimeout, _numFrom, _numTo;
    private readonly ComboBox _cmbGroup;
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
    private readonly List<PortChange> _changes = new(); // en yenisi başta
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

        _numFrom = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 1, Width = 75 };
        _numTo = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 1024, Width = 75 };
        var btnAddRange = new Button { Text = "Aralığı ekle", AutoSize = true };
        btnAddRange.Click += (_, _) => AddPortRange();
        var cmbPresets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        cmbPresets.Items.AddRange(PortPresets.Keys.ToArray<object>());
        cmbPresets.SelectionChangeCommitted += (_, _) => _txtPorts.Text = PortPresets[(string)cmbPresets.SelectedItem!];
        var range = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        range.Controls.AddRange(new Control[]
        {
            _numFrom, MakeLabel("–"), _numTo, btnAddRange,
            MakeLabel("   Hazır liste:"), cmbPresets,
        });
        table.Controls.Add(MakeLabel("Port aralığı:"), 0, 2);
        table.Controls.Add(range, 1, 2);
        table.SetColumnSpan(range, 4);

        var timing = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        timing.Controls.AddRange(new Control[]
        {
            MakeLabel("Tarama aralığı (dk):"), _numInterval,
            MakeLabel("   Zaman aşımı (ms):"), _numTimeout,
        });
        table.Controls.Add(timing, 1, 3);
        table.SetColumnSpan(timing, 4);

        var checks = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        checks.Controls.AddRange(new Control[] { _chkNotifyOpened, _chkNotifyClosed, _chkScanOnStartup, _chkAutostart });
        table.Controls.Add(checks, 1, 4);
        table.SetColumnSpan(checks, 4);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Örnek hedef: 192.168.1.0/24, 10.0.0.5-40, sunucu.local   •   Örnek port: 22,80,443,8000-8100",
        };
        table.Controls.Add(hint, 1, 5);
        table.SetColumnSpan(hint, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        buttons.Controls.AddRange(new Control[] { _btnScan, _btnStop, btnSave });
        table.Controls.Add(buttons, 1, 6);
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
        _lvOpen = MakeListView(("Adres", 120), ("Cihaz adı", 180), ("Port", 60), ("Servis", 130), ("Paylaşımlar", 200), ("İlk görülme", 125), ("Son görülme", 125));
        _lvChanges = MakeListView(("Zaman", 140), ("Durum", 90), ("Adres", 130), ("Cihaz adı", 220), ("Port", 70), ("Servis", 150));
        _lvOpen.ContextMenuStrip = MakeItemMenu(_lvOpen);
        _lvChanges.ContextMenuStrip = MakeItemMenu(_lvChanges);

        _txtFilter = new TextBox { Width = 280, PlaceholderText = "Adres, cihaz adı, port veya servis ara" };
        _txtFilter.TextChanged += (_, _) => RenderAll();
        _cmbGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _cmbGroup.Items.AddRange(new object[] { "Gruplama yok", "Adres", "Port", "Servis" });
        _cmbGroup.SelectionChangeCommitted += (_, _) =>
        {
            _settings.GroupBy = _cmbGroup.SelectedIndex > 0 ? (string)_cmbGroup.SelectedItem! : "";
            Storage.Save(AppPaths.SettingsFile, _settings);
            RenderAll();
        };
        var btnClearFilter = new Button { Text = "Temizle", AutoSize = true };
        btnClearFilter.Click += (_, _) => _txtFilter.Clear();
        var filterBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
        filterBar.Controls.AddRange(new Control[]
        {
            MakeLabel("Ara:"), _txtFilter, btnClearFilter, MakeLabel("   Grupla:"), _cmbGroup,
        });

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
        Controls.Add(filterBar);
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
        _changes.AddRange(Storage.LoadRecentChanges(MaxChangeRows).AsEnumerable().Reverse());
        RenderAll();

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
        _cmbGroup.SelectedIndex = Math.Max(0, _cmbGroup.Items.IndexOf(_settings.GroupBy));
    }

    private static readonly Dictionary<string, string> PortPresets = new()
    {
        ["Yaygın portlar"] = AppSettings.DefaultPorts,
        ["Web (80, 443, 8000-8100, 8443)"] = "80,443,8000-8100,8443",
        ["Uzak erişim (SSH, Telnet, RDP, VNC, WinRM)"] = "22,23,3389,5900,5985,5986",
        ["Veritabanı"] = "1433,1521,3306,5432,6379,9200,11211,27017",
        ["Dosya paylaşımı (FTP, SMB, NFS)"] = "20,21,139,445,2049",
        ["İyi bilinen portlar (1-1024)"] = "1-1024",
    };

    private void AddPortRange()
    {
        int from = (int)_numFrom.Value, to = (int)_numTo.Value;
        if (to < from) (from, to) = (to, from);
        var token = from == to ? from.ToString() : $"{from}-{to}";
        var current = _txtPorts.Text.Trim().TrimEnd(',');
        _txtPorts.Text = current.Length == 0 ? token : $"{current},{token}";
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
            SetStatus("Paylaşılan klasörler okunuyor...");
            var shares = await Shares.ListAsync(
                found.Where(f => f.Port is 139 or 445).Select(f => f.Host.ToString()).Distinct(), ct);

            var scopeHosts = hosts.Select(h => h.ToString()).ToHashSet();
            var scopePorts = ports.ToHashSet();
            bool baseline = _snapshot.LastScan == null;
            var changes = ApplyResults(found, names, shares, scopeHosts, scopePorts, DateTime.Now);

            if (changes.Count > 0)
            {
                Storage.AppendChanges(changes);
                _changes.InsertRange(0, changes);
                if (_changes.Count > MaxChangeRows) _changes.RemoveRange(MaxChangeRows, _changes.Count - MaxChangeRows);
                Notify(changes);
            }
            RenderAll();

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
        Dictionary<string, Shares.Result> shares, HashSet<string> scopeHosts, HashSet<int> scopePorts, DateTime now)
    {
        bool baseline = _snapshot.LastScan == null;
        var newOpen = new Dictionary<string, PortEntry>();
        var changes = new List<PortChange>();

        foreach (var (ip, port) in found)
        {
            var host = ip.ToString();
            var key = $"{host}:{port}";
            names.TryGetValue(host, out var hostName);
            if (_snapshot.Open.TryGetValue(key, out var entry))
            {
                entry.LastSeen = now;
                entry.HostName = hostName ?? entry.HostName;
            }
            else
            {
                entry = new PortEntry { Host = host, Port = port, HostName = hostName, FirstSeen = now, LastSeen = now };
                if (!baseline) changes.Add(new PortChange(now, true, host, port, hostName));
            }
            if (port is 139 or 445 && shares.TryGetValue(host, out var sh))
            {
                entry.Shares = sh.Names;
                entry.ShareError = sh.Error;
            }
            newOpen[key] = entry;
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

    private sealed record Target(string Host, int Port, string? HostName, IReadOnlyList<string>? Shares = null);

    private void RenderAll()
    {
        Fill(_lvOpen, _snapshot.Open.Values
            .OrderBy(e => IPAddress.TryParse(e.Host, out var ip) ? Scanner.IpSortKey(ip) : 0)
            .ThenBy(e => e.Port)
            .Select(e => new ListViewItem(new[]
            {
                e.Host, e.HostName ?? "", e.Port.ToString(), Services.NameOf(e.Port), SharesText(e),
                e.FirstSeen.ToString("g"), e.LastSeen.ToString("g"),
            }) { Tag = new Target(e.Host, e.Port, e.HostName, e.Shares) }));

        Fill(_lvChanges, _changes.Select(c => new ListViewItem(new[]
        {
            c.Time.ToString("g"), c.Opened ? "▲ Açıldı" : "▼ Kapandı", c.Host, c.HostName ?? "",
            c.Port.ToString(), Services.NameOf(c.Port),
        })
        {
            ForeColor = c.Opened ? Color.FromArgb(176, 64, 0) : Color.FromArgb(0, 110, 60),
            Tag = new Target(c.Host, c.Port, c.HostName),
        }));

        bool filtered = _txtFilter.Text.Trim().Length > 0;
        _tabs.TabPages[0].Text = filtered
            ? $"Açık Portlar ({_lvOpen.Items.Count}/{_snapshot.Open.Count})"
            : $"Açık Portlar ({_snapshot.Open.Count})";
        _tabChanges.Text = filtered
            ? $"Değişiklikler ({_lvChanges.Items.Count}/{_changes.Count})"
            : $"Değişiklikler ({_changes.Count})";
    }

    /// <summary>Listeyi arama metnine göre süzer ve seçili alana göre gruplar.</summary>
    private void Fill(ListView lv, IEnumerable<ListViewItem> items)
    {
        var filter = _txtFilter.Text.Trim();
        var mode = _settings.GroupBy;
        var rows = items.Where(i => filter.Length == 0 || i.SubItems.Cast<ListViewItem.ListViewSubItem>()
            .Any(s => s.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase)));

        lv.BeginUpdate();
        lv.Items.Clear();
        lv.Groups.Clear();
        lv.ShowGroups = mode.Length > 0;
        if (mode.Length == 0)
        {
            lv.Items.AddRange(rows.ToArray());
        }
        else
        {
            foreach (var g in rows.GroupBy(i => GroupKey((Target)i.Tag!, mode)).OrderBy(g => g.Key.Sort, StringComparer.Ordinal))
            {
                var group = new ListViewGroup($"{g.Key.Header}   ({g.Count()})")
                {
                    CollapsedState = ListViewGroupCollapsedState.Expanded,
                };
                lv.Groups.Add(group);
                foreach (var item in g)
                {
                    item.Group = group;
                    lv.Items.Add(item);
                }
            }
        }
        if (lv.ListViewItemSorter != null) lv.Sort();
        lv.EndUpdate();
    }

    private static string SharesText(PortEntry e) =>
        e.Shares is { Count: > 0 } s ? string.Join(", ", s) : e.ShareError is { } err ? $"({err})" : "";

    private static (string Header, string Sort) GroupKey(Target t, string mode)
    {
        var svc = Services.NameOf(t.Port);
        switch (mode)
        {
            case "Port":
                return (svc.Length > 0 ? $"Port {t.Port} — {svc}" : $"Port {t.Port}", t.Port.ToString("D5"));
            case "Servis":
                return svc.Length > 0 ? (svc, "0" + svc) : ("Bilinmeyen servis", "1");
            default:
                var sort = IPAddress.TryParse(t.Host, out var ip) ? Scanner.IpSortKey(ip).ToString("D10") : t.Host;
                return (t.HostName is { Length: > 0 } hn ? $"{t.Host} — {hn}" : t.Host, sort);
        }
    }

    private ContextMenuStrip MakeItemMenu(ListView lv)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            if (lv.SelectedItems.Count == 0 || lv.SelectedItems[0].Tag is not Target t)
            {
                e.Cancel = true;
                return;
            }

            menu.Items.Add(new ToolStripMenuItem($"{t.Host}:{t.Port}  {Services.NameOf(t.Port)}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            foreach (var a in PortActions.ForPort(t.Host, t.Port))
                menu.Items.Add(a.Title, null, (_, _) => Launch(a));
            foreach (var share in t.Shares ?? Array.Empty<string>())
            {
                var path = $@"\\{t.Host}\{share}";
                menu.Items.Add($"{path} klasörünü aç", null, (_, _) => Launch(new PortAction("Paylaşım", "explorer.exe", path)));
            }
            menu.Items.Add("Portu şimdi kontrol et", null, async (_, _) => await RecheckAsync(t));
            menu.Items.Add(new ToolStripSeparator());
            foreach (var a in PortActions.ForHost(t.Host))
                menu.Items.Add(a.Title, null, (_, _) => Launch(a));
            menu.Items.Add("Bu adresin tüm portlarını göster", null, (_, _) => _txtFilter.Text = t.Host);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Adresi kopyala", null, (_, _) => Clipboard.SetText(t.Host));
            menu.Items.Add("Adres:port kopyala", null, (_, _) => Clipboard.SetText($"{t.Host}:{t.Port}"));
            if (t.HostName is { Length: > 0 } hn)
                menu.Items.Add("Cihaz adını kopyala", null, (_, _) => Clipboard.SetText(hn));
            // Menü boşken açılırsa WinForms Opening'i iptal edilmiş olarak gönderir.
            e.Cancel = false;
        };
        return menu;
    }

    private void Launch(PortAction a)
    {
        if (a.ShowOutput)
        {
            new CommandOutputForm(a).Show(this);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(a.FileName, a.Arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{a.Title} başlatılamadı:\n{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RecheckAsync(Target t)
    {
        if (!IPAddress.TryParse(t.Host, out var ip)) return;
        SetStatus($"{t.Host}:{t.Port} kontrol ediliyor...");
        bool open = await Scanner.IsOpenAsync(ip, t.Port, Math.Max(_settings.TimeoutMs, 1000), CancellationToken.None);
        SetStatus($"{t.Host}:{t.Port} şu anda {(open ? "AÇIK" : "kapalı ya da yanıt vermiyor")} ({DateTime.Now:T})");
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

        var sb = new StringBuilder("Adres;Cihaz adı;Port;Servis;Paylaşımlar;İlk görülme;Son görülme\r\n");
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
            lv.Columns.Add(title, width).Tag = title;

        var sorter = new ListSort();
        lv.ColumnClick += (_, e) =>
        {
            sorter.Descending = lv.ListViewItemSorter != null && sorter.Column == e.Column && !sorter.Descending;
            sorter.Column = e.Column;
            lv.ListViewItemSorter = sorter;
            lv.Sort();
            foreach (ColumnHeader c in lv.Columns)
                c.Text = (string)c.Tag! + (c.Index == e.Column ? (sorter.Descending ? " ▼" : " ▲") : "");
        };
        return lv;
    }
}
