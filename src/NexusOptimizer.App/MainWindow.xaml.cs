using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Notifications;
// Alias per disambiguare i tipi WPF dai global using WinForms.
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using ListBox = System.Windows.Controls.ListBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace NexusOptimizer.App;

/// <summary>
/// Shell principale: navigazione laterale, header, Command Palette (CTRL+K)
/// e gestione tray/monitoring adattivo.
/// </summary>
public partial class MainWindow : Window
{
    private TrayIconService? _tray;
    private readonly NotificationCenter? _center;
    private bool _inTray;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
        Closed += (_, _) => _tray?.Dispose();
        Loaded += (_, _) => GetDash()?.StartIfNeeded();
        PreviewKeyDown += OnPreviewKeyDown;
        // In una prova di caricamento della vista il contenitore DI non esiste: la
        // campanella semplicemente non ha una sorgente e la finestra resta valida.
        _center = App.Services?.GetService(typeof(NotificationCenter)) as NotificationCenter;
        if (_center is not null) _center.Published += OnNotificationPublished;
        Closed += (_, _) => { if (_center is not null) _center.Published -= OnNotificationPublished; };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;
    private DashboardViewModel? GetDash() => (DataContext as MainViewModel)?.Dashboard;

    // ------------------------------------------------------------- palette
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm?.OpenPalette();
            e.Handled = true;
            FocusPalette();
        }
        else if (e.Key == Key.Escape && Vm?.IsPaletteOpen == true)
        {
            Vm.ClosePalette();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Vm?.IsPaletteOpen == true)
        {
            Vm.CommitFirstResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt && Vm?.CanGoBack == true)
        {
            Vm.GoBack();
            e.Handled = true;
        }
    }

    private void FocusPalette()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => PaletteBox.Focus()));
    }

    private void OpenPaletteClicked(object sender, RoutedEventArgs e)
    {
        Vm?.OpenPalette();
        FocusPalette();
    }

    private void OnPaletteItem(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is PaletteItem item)
            item.Execute();
    }

    // ------------------------------------------------------------- title bar Fluent

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        try { DragMove(); }
        catch (InvalidOperationException) { /* il mouse può essere rilasciato durante il trascinamento */ }
    }

    private void MinimizeWindowClicked(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void MaximizeWindowClicked(object sender, RoutedEventArgs e)
        => ToggleWindowState();

    private void CloseWindowClicked(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void OpenSettingsClicked(object sender, RoutedEventArgs e)
        => NavigateShell("nav.settings");

    private void OpenGamingClicked(object sender, RoutedEventArgs e)
        => NavigateShell("nav.gaming");

    /// <summary>
    /// Alterna chiaro/scuro e salva la scelta: il tema "auto" viene risolto sul
    /// valore attualmente in uso, così il primo clic fa sempre l'effetto atteso.
    /// </summary>
    private void ToggleThemeClicked(object sender, RoutedEventArgs e)
    {
        var config = App.Services.GetService(typeof(AppConfig)) as AppConfig;
        var store = App.Services.GetService(typeof(ConfigStore)) as ConfigStore;
        if (config is null) return;

        var currentlyDark = config.Theme switch
        {
            "dark" => true,
            "light" => false,
            _ => IsCurrentPaletteDark(),
        };
        config.Theme = currentlyDark ? "light" : "dark";
        App.ApplyTheme(config.Theme);
        App.ApplyAccent(config.AccentColor);
        try { store?.Save(config); }
        catch (Exception) { /* la preferenza resta valida per questa sessione */ }
    }

    /// <summary>Legge la luminosità del fondo corrente per capire il tema attivo.</summary>
    private static bool IsCurrentPaletteDark()
    {
        return Application.Current?.Resources["WindowBackgroundBrush"] is SolidColorBrush brush
               && (brush.Color.R + brush.Color.G + brush.Color.B) / 3 < 128;
    }

    private void NavigateShell(string id)
    {
        if (Vm is null) return;
        var target = Vm.Items.FirstOrDefault(item => item.Id == id);
        if (target is not null) Vm.Selected = target;
    }

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    // ------------------------------------------------------------- monitor

    /// <summary>Pausa o ripristina il polling rispettando la scelta utente (leggerezza).</summary>
    private void UpdateMonitor()
    {
        var sp = App.Services;
        var monitor = sp.GetService(typeof(SystemMonitor)) as SystemMonitor;
        if (monitor is null) return;
        var live = (sp.GetService(typeof(AppConfig)) as AppConfig)?.LiveMonitoring ?? true;

        if (!live || _inTray) { if (monitor.IsRunning) monitor.Pause(); }
        else if (!monitor.IsRunning) monitor.Resume();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        var cfg = App.Services.GetService(typeof(AppConfig)) as AppConfig;
        if (WindowState == WindowState.Minimized)
        {
            if (!_inTray && cfg?.MinimizeToTray == true)
                EnterTray();
            else
                UpdateMonitor();
        }
        else
        {
            if (_inTray) ExitTray();
            UpdateMonitor();
        }
    }

    /// <summary>
    /// Fumetto della tray per un nuovo avviso. Con la finestra aperta il badge
    /// della campanella e' gia' visibile: interrompere con un fumetto sarebbe
    /// rumore. La modalita' silenziosa lo sopprime in ogni caso.
    /// </summary>
    private void OnNotificationPublished(NotificationRecord record)
    {
        if (!_inTray) return;
        if ((App.Services?.GetService(typeof(AppConfig)) as AppConfig)?.QuietMode == true) return;
        Dispatcher.BeginInvoke(new Action(() => _tray?.ShowBalloon(
            Locale.T(record.TitleKey),
            Locale.F(record.MessageKey, record.MessageArgs),
            record.Severity != NotificationSeverity.Info)));
    }

    private void EnterTray()
    {
        _inTray = true;
        Hide();
        ShowInTaskbar = false;
        try { (_tray ??= new TrayIconService(ExitTray, () => Application.Current.Shutdown())).Show(); }
        catch (Exception) { /* niente tray: l'app resta comunque utilizzabile */ }
        UpdateMonitor();
    }

    private void ExitTray()
    {
        _inTray = false;
        ShowInTaskbar = true;
        _tray?.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateMonitor();
    }
}
