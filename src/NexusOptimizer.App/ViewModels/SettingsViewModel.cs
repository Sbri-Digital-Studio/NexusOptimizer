using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Updates;

namespace NexusOptimizer.App.ViewModels;

/// <summary>Voce dell'elenco esclusioni utente (PathGuard le rispetta già).</summary>
public sealed class ExclusionRow
{
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public override string ToString() => Path;
}

/// <summary>
/// Impostazioni: persistenza immediata su config.json atomico.
/// Avvio con Windows scritto REALMENTE in HKCU\...\Run. Zero valori simulati.
/// </summary>
public sealed class SettingsViewModel : ObservableBase
{
    public sealed record AccentOption(string Name, string Hex);

    public IReadOnlyList<AccentOption> Accents { get; } =
    [
        new("Blu",   "#4F8CFF"),
        new("Verde", "#34C759"),
        new("Ambra", "#E6A23C"),
        new("Viola", "#9A6BFF"),
        new("Rosa",  "#FF5A8F"),
        new("Teal",  "#22B8CF"),
    ];

    private readonly ConfigStore _store;
    private readonly AppConfig _cfg;
    private readonly FileLogService _log;
    private readonly AutoSafeCleanService _autoSafeClean;
    private readonly UpdateService _updates;
    private readonly SystemMonitor _monitor;

    public event Action? SharedFlagChanged;

    public SettingsViewModel(ConfigStore store, AppConfig cfg, FileLogService log,
                             AutoSafeCleanService autoSafeClean, UpdateService updates,
                             SystemMonitor monitor)
    {
        _store = store;
        _cfg = cfg;
        _log = log;
        _autoSafeClean = autoSafeClean;
        _updates = updates;
        _monitor = monitor;
        _themeIndex = cfg.Theme switch { "dark" => 1, "light" => 2, _ => 0 };
        _languageIndex = cfg.Language == "en" ? 1 : 0;
        _accent = Accents.FirstOrDefault(option =>
            option.Hex.Equals(cfg.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? Accents[0];
        foreach (var e in cfg.Exclusions.Distinct(StringComparer.OrdinalIgnoreCase))
            Exclusions.Add(new ExclusionRow { Path = e, IsDirectory = System.IO.Directory.Exists(e) });

        Locale.Set(cfg.Language);
        Locale.Changed += () =>
        {
            Raise(nameof(ThemeOptions));
            Raise(nameof(Accent));
            Raise(nameof(LanguageIndex));
            Raise(nameof(StatusLine));
        };
        PreviewAutoCleanCommand = new RelayCommand(_ => _ = PreviewAutoCleanAsync(), _ => !AutoPreviewBusy);
        CheckUpdatesCommand = new RelayCommand(_ => _ = CheckUpdatesAsync(), _ => CanCheckUpdates);
        _updateStatusLine = DescribeUpdateChannel();
    }

    /// <summary>Opzioni tema etichettate nella lingua corrente.</summary>
    public IReadOnlyList<string> ThemeOptions =>
        [.. ThemeOptionKeys.Select(k => Services.Locale.T(k))];

    private static readonly string[] ThemeOptionKeys =
        ["set.theme.auto", "set.theme.dark", "set.theme.light"];

    /// <summary>Voci del selettore lingua (sempre esplicithe).</summary>
    public IReadOnlyList<string> LanguageOptions { get; } = ["Italiano", "English"];


    // ---------------------------------------------------------------- tema
    private int _themeIndex;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex == value) return;
            _themeIndex = value;
            Raise();
            _cfg.Theme = value switch { 1 => "dark", 2 => "light", _ => "auto" };
            Persist();
            App.ApplyTheme(_cfg.Theme);
        }
    }

    // ---------------------------------------------------------------- accento
    private AccentOption? _accent;
    public AccentOption Accent
    {
        get => _accent ??= Accents[0];
        set
        {
            if (string.Equals(value.Hex, _accent?.Hex, StringComparison.OrdinalIgnoreCase)) return;
            _accent = value;
            Raise();
            ApplyAccent(value.Hex);
        }
    }

    private void ApplyAccent(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            var soft = System.Windows.Media.Color.FromArgb(0x33, c.R, c.G, c.B);
            var res = System.Windows.Application.Current?.Resources;
            if (res is not null)
            {
                res["AccentBrush"] = new System.Windows.Media.SolidColorBrush(c);
                res["AccentSoftBrush"] = new System.Windows.Media.SolidColorBrush(soft);
            }
            _cfg.AccentColor = hex;
            Persist();
        }
        catch (Exception ex)
        {
            _log.Error("Colore accento non valido", ex);
        }
    }

    // ---------------------------------------------------------------- lingua
    private int _languageIndex;
    public int LanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (_languageIndex == value) return;
            _languageIndex = value;
            Raise();
            _cfg.Language = value == 1 ? "en" : "it";
            Persist();
            Locale.Set(_cfg.Language);
        }
    }

    // ---------------------------------------------------------------- flag semplici
    public bool Animations
    {
        get => _cfg.Animations;
        set { if (_cfg.Animations == value) return; _cfg.Animations = value; Raise(); Persist(); }
    }

    public bool MinimizeToTray
    {
        get => _cfg.MinimizeToTray;
        set { if (_cfg.MinimizeToTray == value) return; _cfg.MinimizeToTray = value; Raise(); Persist(); SharedFlagChanged?.Invoke(); }
    }

    public bool LiveMonitoring
    {
        get => _cfg.LiveMonitoring;
        set { if (_cfg.LiveMonitoring == value) return; _cfg.LiveMonitoring = value; Raise(); Persist(); SharedFlagChanged?.Invoke(); }
    }

    public bool QuietMode
    {
        get => _cfg.QuietMode;
        set { if (_cfg.QuietMode == value) return; _cfg.QuietMode = value; Raise(); Persist(); }
    }

    // ---------------------------------------------------------------- cadenza del monitor
    // Un campione più frequente rende i grafici più fluidi e costa un po' di CPU
    // in più. Il valore era già in config.json ma non veniva letto da nessuno.

    private static readonly int[] MonitorIntervalValues = [500, 1000, 2000];

    public IReadOnlyList<string> MonitorIntervals =>
        [.. MonitorIntervalValues.Select(ms => (ms / 1000d).ToString("0.#", Locale.Culture) + " s")];

    public int MonitorIntervalIndex
    {
        get
        {
            var index = Array.IndexOf(MonitorIntervalValues, _cfg.MonitorIntervalMs);
            return index < 0 ? 1 : index;
        }
        set
        {
            if (value < 0 || value >= MonitorIntervalValues.Length) return;
            var chosen = MonitorIntervalValues[value];
            if (_cfg.MonitorIntervalMs == chosen) return;
            _cfg.MonitorIntervalMs = chosen;
            _monitor.IntervalMs = chosen;
            Raise();
            Persist();
        }
    }

    // ---------------------------------------------------------------- avvisi
    // Ogni interruttore governa una regola reale del centro avvisi: spento, la
    // regola non viene nemmeno valutata (vedi SystemAlertEvaluator).

    public bool NotifyLowDisk
    {
        get => _cfg.NotifyLowDisk;
        set { if (_cfg.NotifyLowDisk == value) return; _cfg.NotifyLowDisk = value; Raise(); Persist(); }
    }

    public bool NotifyRecoverableSpace
    {
        get => _cfg.NotifyRecoverableSpace;
        set { if (_cfg.NotifyRecoverableSpace == value) return; _cfg.NotifyRecoverableSpace = value; Raise(); Persist(); }
    }

    public bool TemperatureAlerts
    {
        get => _cfg.TemperatureAlerts;
        set { if (_cfg.TemperatureAlerts == value) return; _cfg.TemperatureAlerts = value; Raise(); Persist(); }
    }

    public bool StartupMonitoring
    {
        get => _cfg.StartupMonitoring;
        set { if (_cfg.StartupMonitoring == value) return; _cfg.StartupMonitoring = value; Raise(); Persist(); }
    }

    /// <summary>Soglie proposte: valori interi, nessun campo libero da validare.</summary>
    public IReadOnlyList<string> LowDiskThresholds { get; } = ["5%", "10%", "15%", "20%"];

    private static readonly double[] LowDiskThresholdValues = [5, 10, 15, 20];

    public int LowDiskThresholdIndex
    {
        get
        {
            var index = Array.FindIndex(LowDiskThresholdValues,
                value => Math.Abs(value - _cfg.NotifyLowDiskPercent) < 0.01);
            return index < 0 ? 1 : index;
        }
        set
        {
            if (value < 0 || value >= LowDiskThresholdValues.Length) return;
            if (Math.Abs(LowDiskThresholdValues[value] - _cfg.NotifyLowDiskPercent) < 0.01) return;
            _cfg.NotifyLowDiskPercent = LowDiskThresholdValues[value];
            Raise();
            Persist();
        }
    }

    // ---------------------------------------------------------------- Auto Safe Clean
    public bool AutoSafeClean
    {
        get => _cfg.AutoCleanEnabled;
        set
        {
            if (_cfg.AutoCleanEnabled == value) return;
            _cfg.AutoCleanEnabled = value;
            Persist();
            Raise();
        }
    }

    private bool _autoPreviewBusy;
    public bool AutoPreviewBusy
    {
        get => _autoPreviewBusy;
        private set
        {
            if (!Set(ref _autoPreviewBusy, value)) return;
            PreviewAutoCleanCommand.RaiseCanExecute();
        }
    }

    public RelayCommand PreviewAutoCleanCommand { get; }

    private async Task PreviewAutoCleanAsync()
    {
        if (AutoPreviewBusy) return;
        AutoPreviewBusy = true;
        try
        {
            var preview = await _autoSafeClean.PreviewAsync();
            StatusLine = Locale.T("set.autoclean.preview")
                .Replace("{0}", preview.Categories.Sum(category => category.Items.Count)
                    .ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", Formatter.Bytes(preview.TotalBytes), StringComparison.Ordinal);
        }
        catch (Exception)
        {
            StatusLine = Locale.T("set.autoclean.error");
        }
        finally
        {
            AutoPreviewBusy = false;
        }
    }

    // ---------------------------------------------------------------- aggiornamenti
    // Spento e senza canale configurato l'applicazione non contatta nulla: e'
    // l'unico punto del programma in cui esiste una chiamata di rete.

    public bool CheckForUpdates
    {
        get => _cfg.CheckForUpdates;
        set
        {
            if (_cfg.CheckForUpdates == value) return;
            _cfg.CheckForUpdates = value;
            Raise();
            Persist();
            UpdateStatusLine = DescribeUpdateChannel();
            CheckUpdatesCommand.RaiseCanExecute();
        }
    }

    public string UpdateFeedUrl
    {
        get => _cfg.UpdateFeedUrl;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.Equals(_cfg.UpdateFeedUrl, normalized, StringComparison.Ordinal)) return;
            _cfg.UpdateFeedUrl = normalized;
            Raise();
            Persist();
            UpdateStatusLine = DescribeUpdateChannel();
            CheckUpdatesCommand.RaiseCanExecute();
        }
    }

    /// <summary>
    /// Avvisi sugli aggiornamenti di programmi e driver. Sono chiamate di rete,
    /// quindi spenti di default come tutto il resto: il risultato e' un avviso
    /// nella campanella, mai un'installazione.
    /// </summary>
    public bool SoftwareUpdateCheck
    {
        get => _cfg.SoftwareUpdateCheck;
        set { if (_cfg.SoftwareUpdateCheck == value) return; _cfg.SoftwareUpdateCheck = value; Raise(); Persist(); }
    }

    public bool DriverUpdateCheck
    {
        get => _cfg.DriverUpdateCheck;
        set { if (_cfg.DriverUpdateCheck == value) return; _cfg.DriverUpdateCheck = value; Raise(); Persist(); }
    }

    private bool _updateBusy;

    public bool CanCheckUpdates => !_updateBusy
        && _cfg.CheckForUpdates
        && UpdateChannel.IsSupportedFeed(_cfg.UpdateFeedUrl);

    public RelayCommand CheckUpdatesCommand { get; }

    private string _updateStatusLine = "";
    public string UpdateStatusLine { get => _updateStatusLine; private set => Set(ref _updateStatusLine, value); }

    public string LastUpdateCheckText => Locale.F("set.updates.last",
        [_cfg.LastUpdateCheckUtc is DateTime last
            ? last.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
            : Locale.T("set.updates.never")]);

    /// <summary>Stato del canale prima di qualsiasi controllo: mai una promessa vaga.</summary>
    private string DescribeUpdateChannel()
    {
        if (!_cfg.CheckForUpdates) return Locale.T("set.updates.disabled");
        return UpdateChannel.IsSupportedFeed(_cfg.UpdateFeedUrl)
            ? LastUpdateCheckText
            : Locale.T("set.updates.none");
    }

    private async Task CheckUpdatesAsync()
    {
        if (!CanCheckUpdates) return;
        _updateBusy = true;
        Raise(nameof(CanCheckUpdates));
        CheckUpdatesCommand.RaiseCanExecute();
        UpdateStatusLine = Locale.T("set.updates.checking");
        try
        {
            var result = await _updates.CheckAsync();
            UpdateStatusLine = result.Status switch
            {
                UpdateCheckStatus.Disabled => Locale.T("set.updates.disabled"),
                UpdateCheckStatus.NotConfigured => Locale.T("set.updates.none"),
                UpdateCheckStatus.UpToDate => Locale.F("set.updates.uptodate", [UpdateService.CurrentVersionText]),
                UpdateCheckStatus.UpdateAvailable => Locale.F("set.updates.available",
                    [result.LatestVersion ?? "", UpdateService.CurrentVersionText]),
                _ => Locale.T("set.updates.error"),
            };
        }
        finally
        {
            _updateBusy = false;
            Raise(nameof(CanCheckUpdates));
            Raise(nameof(LastUpdateCheckText));
            CheckUpdatesCommand.RaiseCanExecute();
        }
    }

    // ---------------------------------------------------------------- avvio con Windows (HKCU Run reale)
    private const string RunValueName = "NexusOptimizer";
    private const string LegacyRunValueName = "NexusOptimizer";

    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool StartWithWindows
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return k?.GetValue(RunValueName) is string;
            }
            catch (Exception) { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (value)
                {
                    var exe = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(exe))
                    {
                        StatusLine = Locale.T("set.startup.error");
                        Raise(nameof(StatusLine));
                        return;
                    }
                    k.SetValue(RunValueName, $"\"{exe}\"");
                    StatusLine = Locale.T("set.startup.added");
                }
                else
                {
                    k.DeleteValue(RunValueName, throwOnMissingValue: false);
                    k.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
                    StatusLine = Locale.T("set.startup.removed");
                }
                _log.Info($"Startup-with-Windows => {value}");
            }
            catch (Exception ex)
            {
                _log.Error("Scrittura chiave Run fallita", ex);
                StatusLine = Locale.T("set.startup.error");
            }
            Raise();
            Raise(nameof(StatusLine));
        }
    }

    private string _statusLine = "";
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }

    // ---------------------------------------------------------------- esclusioni
    public ObservableCollection<ExclusionRow> Exclusions { get; } = [];

    private ExclusionRow? _selectedExclusion;
    public ExclusionRow? SelectedExclusion
    {
        get => _selectedExclusion;
        set { if (_selectedExclusion != value) { _selectedExclusion = value; Raise(); RemoveCommand.RaiseCanExecute(); } }
    }

    public bool CanRemove => SelectedExclusion is not null;

    private RelayCommand? _addFolderCmd;
    public ICommand AddFolderCommand => _addFolderCommand();
    private ICommand _addFolderCommand()
        => _addFolderCmd ??= new RelayCommand(_ =>
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = false };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                Add(dlg.SelectedPath, System.IO.Directory.Exists(dlg.SelectedPath));
        });

    private RelayCommand? _addFileCmd;
    public ICommand AddFileCommand => _addFileCmd ??= new RelayCommand(_ =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { CheckFileExists = true, Multiselect = false };
        if (dlg.ShowDialog() == true)
            Add(dlg.FileName, isDir: false);
    });

    private RelayCommand? _removeCmd;
    public RelayCommand RemoveCommand => _removeCmd ??= new RelayCommand(
        _ => RemoveSelected(),
        _ => CanRemove);

    private void RemoveSelected()
    {
        var row = SelectedExclusion;
        if (row is null) return;
        _cfg.Exclusions.RemoveAll(x => x.Equals(row.Path, StringComparison.OrdinalIgnoreCase));
        Exclusions.Remove(row);
        SelectedExclusion = null;
        Persist();
    }

    private void Add(string p, bool isDir)
    {
        if (string.IsNullOrWhiteSpace(p)) return;
        if (_cfg.Exclusions.Any(x => x.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLine = Locale.T("set.excl.duplicate");
            Raise(nameof(StatusLine));
            return;
        }
        _cfg.Exclusions.Add(p);
        Exclusions.Add(new ExclusionRow { Path = p, IsDirectory = isDir });
        Persist();
        StatusLine = Locale.T("set.excl.added");
        Raise(nameof(StatusLine));
    }

    // ------------------------------------------------------ conferma visibile
    // Le impostazioni si salvano da sole a ogni modifica: senza un riscontro
    // l'utente non ha modo di sapere se la sua scelta è stata registrata.

    private System.Windows.Threading.DispatcherTimer? _savedTimer;
    private bool _justSaved;

    /// <summary>Vero per qualche secondo dopo un salvataggio riuscito.</summary>
    public bool JustSaved
    {
        get => _justSaved;
        private set => Set(ref _justSaved, value);
    }

    private string _savedLabel = "";

    /// <summary>Testo della conferma, con l'ora del salvataggio.</summary>
    public string SavedLabel { get => _savedLabel; private set => Set(ref _savedLabel, value); }

    private void FlashSaved()
    {
        SavedLabel = Locale.F("set.saved",
            [DateTime.Now.ToString("HH:mm:ss", Locale.Culture)]);
        JustSaved = true;

        _savedTimer ??= CreateSavedTimer();
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateSavedTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            JustSaved = false;
        };
        return timer;
    }

    private void Persist()
    {
        try
        {
            _store.Save(_cfg);
            FlashSaved();
        }
        catch (Exception ex)
        {
            _log.Error("Salvataggio config fallito (settings)", ex);
            SavedLabel = Locale.T("set.saved.failed");
            JustSaved = true;
        }
    }
}

