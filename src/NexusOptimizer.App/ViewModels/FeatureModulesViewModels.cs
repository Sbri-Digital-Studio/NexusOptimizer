using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using Application = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>
/// Gestione unificata di RAM e memoria grafica: telemetria reale e operazioni
/// esplicite affidate allo stesso servizio usato da Optimizer e Modalita' Gaming.
/// </summary>
public sealed class RamManagerViewModel : ObservableBase, IPageLifecycle, IDisposable
{
    private readonly SystemMonitor _monitor;
    private readonly IMemoryOptimizationService _memory;
    private readonly RelayCommand _cleanRamCommand;
    private readonly RelayCommand _cleanVramCommand;
    private readonly RelayCommand _optimizeAllCommand;
    private bool _active;
    private bool _isOptimizing;
    private string _usedText = Formatter.Dash;
    private string _availableText = Formatter.Dash;
    private string _totalText = Formatter.Dash;
    private string _ramActionStatus = Locale.T("ram.ram.ready");
    private string _vramActionStatus = Locale.T("ram.vram.clean.ready");
    private string _lastRamReleasedText = Formatter.Dash;
    private string _lastVramActionText = Formatter.Dash;
    private double? _usedPercent;
    private double? _vramPercent;
    private string _vramUsedText = Formatter.Dash;
    private string _vramTotalText = Formatter.Dash;
    private bool _hasVram;

    public RamManagerViewModel(SystemMonitor monitor, IMemoryOptimizationService memory)
    {
        _monitor = monitor;
        _memory = memory;
        _monitor.Snapshot += OnSnapshot;
        Locale.Changed += OnLocaleChanged;
        _cleanRamCommand = new RelayCommand(_ => _ = CleanRamAsync(), _ => !IsOptimizing);
        _cleanVramCommand = new RelayCommand(_ => _ = CleanVramAsync(), _ => !IsOptimizing);
        _optimizeAllCommand = new RelayCommand(_ => _ = OptimizeAllAsync(), _ => !IsOptimizing);
    }

    public SeriesPointBuffer Series { get; } = new(SystemMonitor.RingCapacity);

    /// <summary>
    /// Memoria della scheda video: e' memoria a tutti gli effetti e vive qui
    /// accanto alla RAM. Il totale arriva da NVML (schede NVIDIA); dove Windows
    /// non lo espone si mostra il solo valore in uso, senza percentuale inventata.
    /// </summary>
    public SeriesPointBuffer VramSeries { get; } = new(SystemMonitor.RingCapacity);

    public double? VramPercent { get => _vramPercent; private set => Set(ref _vramPercent, value); }
    public string VramUsedText { get => _vramUsedText; private set => Set(ref _vramUsedText, value); }
    public string VramTotalText { get => _vramTotalText; private set => Set(ref _vramTotalText, value); }

    /// <summary>La scheda espone la memoria video? Altrimenti la sezione non compare.</summary>
    public bool HasVram { get => _hasVram; private set => Set(ref _hasVram, value); }
    public string UsedText { get => _usedText; private set => Set(ref _usedText, value); }
    public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    public double? UsedPercent { get => _usedPercent; private set => Set(ref _usedPercent, value); }
    public ICommand CleanRamCommand => _cleanRamCommand;
    public ICommand CleanVramCommand => _cleanVramCommand;
    public ICommand OptimizeAllCommand => _optimizeAllCommand;

    /// <summary>Alias compatibile con i collegamenti precedenti: ora esegue entrambe le azioni.</summary>
    public ICommand OptimizeCommand => _optimizeAllCommand;

    public bool IsOptimizing
    {
        get => _isOptimizing;
        private set
        {
            if (!Set(ref _isOptimizing, value)) return;
            _cleanRamCommand.RaiseCanExecute();
            _cleanVramCommand.RaiseCanExecute();
            _optimizeAllCommand.RaiseCanExecute();
            Raise(nameof(CleanRamButtonText));
            Raise(nameof(CleanVramButtonText));
            Raise(nameof(OptimizeAllButtonText));
            Raise(nameof(OptimizeButtonText));
        }
    }

    public string CleanRamButtonText => Locale.T(IsOptimizing ? "ram.cleaning" : "ram.ram.clean");
    public string CleanVramButtonText => Locale.T(IsOptimizing ? "ram.cleaning" : "ram.vram.clean");
    public string OptimizeAllButtonText => Locale.T(IsOptimizing ? "ram.cleaning" : "ram.all.clean");
    public string OptimizeButtonText => OptimizeAllButtonText;
    public string RamActionStatus { get => _ramActionStatus; private set => Set(ref _ramActionStatus, value); }
    public string VramActionStatus { get => _vramActionStatus; private set => Set(ref _vramActionStatus, value); }
    public string LastRamReleasedText
    {
        get => _lastRamReleasedText;
        private set
        {
            if (!Set(ref _lastRamReleasedText, value)) return;
            Raise(nameof(LastReleasedText));
        }
    }
    public string LastVramActionText { get => _lastVramActionText; private set => Set(ref _lastVramActionText, value); }

    /// <summary>Alias compatibile con la vecchia card.</summary>
    public string LastReleasedText => LastRamReleasedText;

    /// <summary>Alias compatibile con la vecchia card.</summary>
    public string ActionStatus => RamActionStatus;

    public void Activate() => _active = true;
    public void Deactivate() => _active = false;

    private void OnSnapshot(SystemSnapshot snapshot)
    {
        if (!_active) return;
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Apply(snapshot));
    }

    private void Apply(SystemSnapshot snapshot)
    {
        UsedPercent = snapshot.RamUsedPercent;
        UsedText = Formatter.Percent(snapshot.RamUsedPercent);
        TotalText = snapshot.RamTotalBytes is double total ? Formatter.Bytes(total) : Formatter.Unavailable;
        AvailableText = snapshot.RamAvailableBytes is double available ? Formatter.Bytes(available) : Formatter.Unavailable;
        Series.Push(snapshot.RamUsedPercent ?? 0);

        // --- memoria video ---
        // Una lettura opzionale puo' saltare un campione: la card non deve sparire
        // e ricomparire quando la scheda e' gia' stata rilevata.
        HasVram = HasVram || snapshot.GpuMemoryUsedBytes is double;
        if (snapshot.GpuMemoryUsedBytes is double vramUsed)
        {
            VramUsedText = Formatter.Bytes(vramUsed);
            if (snapshot.GpuMemoryTotalBytes is double vramTotal && vramTotal > 0)
            {
                VramTotalText = Formatter.Bytes(vramTotal);
                VramPercent = Math.Clamp(100.0 * vramUsed / vramTotal, 0, 100);
            }
            else
            {
                // Senza il totale della scheda una percentuale sarebbe inventata.
                VramTotalText = Formatter.Unavailable;
                VramPercent = null;
            }
            VramSeries.Push(VramPercent ?? 0);
        }
    }

    private async Task CleanRamAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        try
        {
            await CleanRamCoreAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private async Task CleanVramAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        try
        {
            await CleanVramCoreAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private async Task OptimizeAllAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        try
        {
            await CleanRamCoreAsync();
            await CleanVramCoreAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private async Task CleanRamCoreAsync()
    {
        RamActionStatus = Locale.T("ram.ram.cleaning");
        Series.Clear();
        try
        {
            var result = await Task.Run(_memory.OptimizeRam);
            LastRamReleasedText = Formatter.Bytes(result.RecoveredBytes);
            RamActionStatus = result.Changed
                ? Locale.F("ram.ram.done", [Formatter.Bytes(result.RecoveredBytes), Formatter.Count(result.TrimmedProcessCount)])
                : Locale.T("ram.ram.nothing");
        }
        catch (Exception)
        {
            RamActionStatus = Locale.T("ram.ram.failed");
        }
    }

    private async Task CleanVramCoreAsync()
    {
        VramActionStatus = Locale.T("ram.vram.cleaning");
        VramSeries.Clear();
        try
        {
            _ = await Task.Run(_memory.OptimizeVram);
            LastVramActionText = Locale.T("ram.vram.last.done");
            VramActionStatus = Locale.T("ram.vram.clean.done");
        }
        catch (Exception)
        {
            VramActionStatus = Locale.T("ram.vram.clean.failed");
        }
    }

    private void OnLocaleChanged()
    {
        Raise(nameof(CleanRamButtonText));
        Raise(nameof(CleanVramButtonText));
        Raise(nameof(OptimizeAllButtonText));
        Raise(nameof(OptimizeButtonText));
    }

    public void Dispose()
    {
        _monitor.Snapshot -= OnSnapshot;
        Locale.Changed -= OnLocaleChanged;
    }
}

public sealed class DiskVolumeVm(string name, string label, string format, long total, long free)
{
    public string Name { get; } = name;
    public string Label { get; } = string.IsNullOrWhiteSpace(label) ? "Disco locale" : label;
    public string Format { get; } = format;
    public string TotalText { get; } = Formatter.Bytes(total);
    public string FreeText { get; } = Formatter.Bytes(free);
    public string UsedText { get; } = Formatter.Bytes(Math.Max(0, total - free));
    public double UsedPercent { get; } = total <= 0 ? 0 : 100d * (total - free) / total;
    public string UsedPercentText => $"{UsedPercent:0}%";
    public WpfBrush StateBrush => total <= 0 ? WpfBrushes.Gray
        : free / (double)total >= 0.20 ? WpfBrushes.MediumSeaGreen
        : free / (double)total >= 0.10 ? WpfBrushes.Goldenrod
        : WpfBrushes.IndianRed;
}

/// <summary>Inventario dischi locale tramite DriveInfo, eseguito fuori dal thread UI.</summary>
public sealed class DiskManagerViewModel : ObservableBase, IPageLifecycle
{
    private readonly FileLogService _log;
    private bool _busy;
    private string _status = "Pronto";

    public DiskManagerViewModel(FileLogService log)
    {
        _log = log;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
        OpenStorageSettingsCommand = new RelayCommand(_ => OpenStorageSettings());
        CleanCommand = new RelayCommand(_ =>
        {
            Status = "Apertura dell'anteprima Smart Clean…";
            CleanRequested?.Invoke();
        });
    }

    public event Action? CleanRequested;
    public ObservableCollection<DiskVolumeVm> Volumes { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand OpenStorageSettingsCommand { get; }
    public ICommand CleanCommand { get; }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) (RefreshCommand as RelayCommand)?.RaiseCanExecute(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public Visibility EmptyStateVisibility => Volumes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public void Activate()
    {
        _log.Info("Disk Manager aperto");
        _ = RefreshAsync();
    }

    public void Deactivate() { }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Lettura dei volumi locali…";
        try
        {
            var volumes = await Task.Run(ReadVolumes);
            Volumes.Clear();
            foreach (var volume in volumes) Volumes.Add(volume);
            Raise(nameof(EmptyStateVisibility));
            Status = Locale.F("disk.status.volumes",
                [Locale.P(Volumes.Count, "disk.volume.one", "disk.volume.many")]);
            _log.Info($"Disk Manager aggiornato: {Volumes.Count} unità");
        }
        catch (Exception ex)
        {
            Status = "Lettura non disponibile. Usa Aggiorna o apri le impostazioni Archiviazione.";
            _log.Error("Disk Manager: lettura volumi fallita", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DiskVolumeVm[] ReadVolumes()
    {
        var result = new List<DiskVolumeVm>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return []; }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;
                result.Add(new DiskVolumeVm(drive.Name, drive.VolumeLabel, drive.DriveFormat,
                    drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch
            {
                // I volumi rimovibili possono disconnettersi tra IsReady e la lettura.
            }
        }
        return result.ToArray();
    }

    private static void OpenStorageSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:storage") { UseShellExecute = true }); }
        catch (Exception) { }
    }
}

/// <summary>Stato privacy dell'app, senza modificare impostazioni di sicurezza di Windows.</summary>
public sealed class PrivacyGuardViewModel : ObservableBase
{
    private readonly AppConfig _config;
    public event Action<string>? NavigateRequested;
    public ICommand OpenCommand { get; }

    public PrivacyGuardViewModel(AppConfig config)
    {
        _config = config;
        OpenCommand = new RelayCommand(target =>
        {
            if (target is string id && id.Length > 0) NavigateRequested?.Invoke(id);
        });
    }

    public string TelemetryStatus => _config.TelemetryEnabled ? "ATTIVA SU SCELTA" : "DISATTIVATA";
    public WpfBrush TelemetryBrush => _config.TelemetryEnabled ? WpfBrushes.Goldenrod : WpfBrushes.MediumSeaGreen;
    public string LocalDataPath => ConfigStore.AppDataDirectory;
}

public sealed class ToolItemVm
{
    public ToolItemVm(string titleKey, string detailKey, string executable, string? arguments = null,
                      string iconKind = "apps", WpfBrush? iconBrush = null, string category = "WINDOWS")
    {
        _titleKey = titleKey;
        _detailKey = detailKey;
        IconKind = iconKind;
        IconBrush = iconBrush ?? WpfBrushes.DeepSkyBlue;
        Category = category;
        OpenCommand = new RelayCommand(_ => Open(executable, arguments));
    }

    private readonly string _titleKey;
    private readonly string _detailKey;

    // Titolo e descrizione arrivano dal dizionario a ogni lettura: cambiando
    // lingua la scheda si riscrive senza essere ricostruita.
    public string Title => Locale.T(_titleKey);
    public string Detail => Locale.T(_detailKey);
    public string IconKind { get; }
    public WpfBrush IconBrush { get; }
    public string Category { get; }
    public ICommand OpenCommand { get; }

    private static void Open(string executable, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(executable, arguments ?? string.Empty) { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}

/// <summary>Accesso esplicito a strumenti Windows esistenti; nessuna elevazione automatica.</summary>
public sealed class ToolsViewModel
{
    public ObservableCollection<ToolItemVm> Items { get; } =
    [
        new("tool.taskmgr", "tool.taskmgr.sub", "taskmgr.exe", iconKind: "taskManager", iconBrush: WpfBrushes.DeepSkyBlue, category: "PROCESSI"),
        new("tool.resmon", "tool.resmon.sub", "resmon.exe", iconKind: "resourceMonitor", iconBrush: WpfBrushes.MediumSeaGreen, category: "PRESTAZIONI"),
        new("tool.reliability", "tool.reliability.sub", "perfmon.exe", "/rel", "reliability", WpfBrushes.Goldenrod, "DIAGNOSTICA"),
        new("tool.msinfo", "tool.msinfo.sub", "msinfo32.exe", iconKind: "systemInfo", iconBrush: WpfBrushes.MediumPurple, category: "HARDWARE"),
        new("tool.apps", "tool.apps.sub", "ms-settings:appsfeatures", iconKind: "installedApps", iconBrush: WpfBrushes.IndianRed, category: "APPLICAZIONI"),
        new("tool.storage", "tool.storage.sub", "ms-settings:storage", iconKind: "storageSettings", iconBrush: WpfBrushes.DeepSkyBlue, category: "DISCHI"),
        new("tool.restore", "tool.restore.sub", "SystemPropertiesProtection.exe", iconKind: "systemRestore", iconBrush: WpfBrushes.MediumSeaGreen, category: "RIPRISTINO"),
        new("tool.cleanmgr", "tool.cleanmgr.sub", "cleanmgr.exe", iconKind: "diskCleanup", iconBrush: WpfBrushes.Goldenrod, category: "MANUTENZIONE"),
    ];
}
