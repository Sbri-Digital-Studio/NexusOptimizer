using System.Collections.ObjectModel;
using System.Windows.Input;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Notifications;
using Application = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

/// <summary>
/// Riga della campanella. Il testo viene ricalcolato dal Locale a ogni cambio
/// lingua: gli avvisi memorizzano chiavi e argomenti, mai frasi già tradotte.
/// </summary>
public sealed class NotificationRowVm : ObservableBase
{
    private static readonly WpfBrush CriticalBrush = WpfBrushes.IndianRed;
    private static readonly WpfBrush WarningBrush = WpfBrushes.Goldenrod;
    private static readonly WpfBrush InfoBrush = WpfBrushes.CornflowerBlue;

    private readonly NotificationRecord _record;

    public NotificationRowVm(NotificationRecord record) => _record = record;

    public string Title => Locale.T(_record.TitleKey);

    public string Message => Locale.F(_record.MessageKey, _record.MessageArgs);

    public string? TargetSectionId => _record.TargetSectionId;

    public string? TargetUrl => _record.TargetUrl;

    public bool HasTarget => _record.TargetSectionId is not null || _record.TargetUrl is not null;

    public string OpenLabel => Locale.T("notif.open");

    public WpfBrush Accent => _record.Severity switch
    {
        NotificationSeverity.Critical => CriticalBrush,
        NotificationSeverity.Warning => WarningBrush,
        _ => InfoBrush,
    };

    public string IconKind => _record.Severity switch
    {
        NotificationSeverity.Critical => "bolt",
        NotificationSeverity.Warning => "shield",
        _ => "info",
    };

    /// <summary>Età dell'avviso in forma breve: "adesso", "12 min", "3 h", poi la data.</summary>
    public string TimeText
    {
        get
        {
            var age = DateTime.UtcNow - _record.CreatedUtc;
            if (age < TimeSpan.FromMinutes(1)) return Locale.T("notif.time.now");
            if (age < TimeSpan.FromHours(1))
                return Locale.F("notif.time.minutes", [((int)age.TotalMinutes).ToString(
                    System.Globalization.CultureInfo.CurrentCulture)]);
            if (age < TimeSpan.FromDays(1))
                return Locale.F("notif.time.hours", [((int)age.TotalHours).ToString(
                    System.Globalization.CultureInfo.CurrentCulture)]);
            return _record.CreatedUtc.ToLocalTime().ToString("g",
                System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    public void Refresh()
    {
        Raise(nameof(Title));
        Raise(nameof(Message));
        Raise(nameof(TimeText));
        Raise(nameof(OpenLabel));
    }
}

/// <summary>
/// Campanella della barra titolo: elenco degli avvisi generati da misure reali,
/// contatore dei non letti e apertura della sezione collegata. L'apertura del
/// pannello segna tutto come letto: il pallino non resta acceso senza motivo.
/// </summary>
public sealed class NotificationsViewModel : ObservableBase, IDisposable
{
    private readonly NotificationCenter _center;
    private bool _isOpen;

    public NotificationsViewModel(NotificationCenter center)
    {
        _center = center;
        _center.Changed += OnCenterChanged;
        Locale.Changed += OnLocaleChanged;

        ToggleCommand = new RelayCommand(_ => IsOpen = !IsOpen);
        CloseCommand = new RelayCommand(_ => IsOpen = false);
        ClearCommand = new RelayCommand(_ =>
        {
            _center.Clear();
            IsOpen = false;
        });
        OpenItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is not NotificationRowVm row) return;
            IsOpen = false;
            if (row.TargetUrl is string url) OpenUrl(url);
            else if (row.TargetSectionId is string id) NavigationRequested?.Invoke(id);
        });
        Rebuild();
    }

    /// <summary>Richiesta di apertura di una sezione dalla riga cliccata.</summary>
    public event Action<string>? NavigationRequested;

    public ObservableCollection<NotificationRowVm> Items { get; } = [];

    public ICommand ToggleCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand OpenItemCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!Set(ref _isOpen, value) || !value) return;
            // Aprire il pannello equivale ad aver letto: il badge si spegne.
            _center.MarkAllRead();
            RaiseCounters();
        }
    }

    public int UnreadCount => _center.UnreadCount;

    public bool HasUnread => UnreadCount > 0;

    /// <summary>Oltre 9 il badge resta leggibile solo con il troncamento.</summary>
    public string UnreadText => UnreadCount > 9 ? "9+" : UnreadCount.ToString(
        System.Globalization.CultureInfo.CurrentCulture);

    public bool HasItems => Items.Count > 0;

    public bool IsEmpty => Items.Count == 0;

    public string HeaderText => Locale.T("notif.title");

    public string EmptyText => Locale.T("notif.empty");

    public string ClearLabel => Locale.T("notif.clear");

    public string BellTooltip => Locale.T("notif.tooltip");

    private void OnCenterChanged() => Dispatch(Rebuild);

    private void OnLocaleChanged()
    {
        foreach (var row in Items) row.Refresh();
        Raise(nameof(HeaderText));
        Raise(nameof(EmptyText));
        Raise(nameof(ClearLabel));
        Raise(nameof(BellTooltip));
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var record in _center.Items) Items.Add(new NotificationRowVm(record));
        Raise(nameof(HasItems));
        Raise(nameof(IsEmpty));
        RaiseCounters();
    }

    private void RaiseCounters()
    {
        Raise(nameof(UnreadCount));
        Raise(nameof(HasUnread));
        Raise(nameof(UnreadText));
    }

    /// <summary>
    /// Gli avvisi nascono sul thread di campionamento: la collezione osservabile
    /// va aggiornata sul thread dell'interfaccia.
    /// </summary>
    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        // Senza dispatcher, gia' sul thread giusto, oppure a coda chiusa durante
        // l'uscita: eseguire subito e' l'unico modo per non perdere l'aggiornamento.
        if (dispatcher is null || dispatcher.CheckAccess()
            || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            action();
            return;
        }
        dispatcher.BeginInvoke(action);
    }

    /// <summary>Apertura nel browser predefinito: nessun download automatico.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttps) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception) { /* nessun browser disponibile: l'avviso resta in elenco */ }
    }

    public void Dispose()
    {
        _center.Changed -= OnCenterChanged;
        Locale.Changed -= OnLocaleChanged;
    }
}
