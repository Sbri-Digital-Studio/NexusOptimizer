using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Cleaning;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Safety;
using NexusOptimizer.Core.Health;
using NexusOptimizer.Core.Notifications;

namespace NexusOptimizer.App;

public partial class App : System.Windows.Application
{
    private const string ProductName = "Nexus Optimizer";
    private const int CurrentOnboardingVersion = 2;
    private bool _uiErrorAlreadyReported;
    private SingleInstanceGuard? _instance;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // L'onboarding è l'unica finestra al primo avvio: non deve chiudere l'app
        // prima che la dashboard principale venga mostrata.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Istanza singola per sessione: la seconda copia riporta in primo piano la
        // prima invece di aprire una finestra gemella sulla stessa configurazione.
        _instance = SingleInstanceGuard.Acquire();
        if (_instance is null)
        {
            SingleInstanceGuard.ActivateExistingInstance();
            Shutdown(0);
            return;
        }

        // Handler PRIMA di ogni costruzione, cosi' anche un errore DI viene tracciato.
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("Task con eccezione non osservata", args.Exception);
            args.SetObserved();
        };

        try
        {
            var store = new ConfigStore(); // %LOCALAPPDATA%\NexusOptimizer\config.json (atomico)
            var config = store.Load();

            var services = new ServiceCollection();
            services.AddSingleton(store);
            services.AddSingleton(config);
            services.AddSingleton(_ => new FileLogService(
                Path.Combine(ConfigStore.AppDataDirectory, "logs"), LogLevel.Info));
            // Cadenza del monitor dalla configurazione (era un valore fisso: l'opzione
            // esisteva nel file ma non veniva letta da nessuno).
            services.AddSingleton(_ => new SystemMonitor(
                Math.Clamp(config.MonitorIntervalMs, SystemMonitor.MinIntervalMs, SystemMonitor.MaxIntervalMs)));
            services.AddSingleton<IMemoryOptimizationService, MemoryOptimizationService>();
            services.AddSingleton<AppModeService>();
            services.AddSingleton<GamingModeService>();
            services.AddSingleton<ICrashLogReader, WindowsCrashLogReader>();
            services.AddSingleton<LocalDiagnosticsService>();
            services.AddSingleton<HealthAssessmentCache>();
            services.AddSingleton<NotificationCenter>();
            services.AddSingleton<SystemAlertEvaluator>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<UpdateService>();
            services.AddSingleton<NotificationsViewModel>();
            services.AddSingleton<SafetyEngine>();
            services.AddSingleton<AutoSafeCleanService>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<CleanCleanViewModel>();
            services.AddSingleton<SystemInfoViewModel>();
            services.AddSingleton<SystemInfoService>();
            services.AddSingleton<ProcessService>();
            services.AddSingleton<ProcessesViewModel>();
            services.AddSingleton<StartupService>();
            services.AddSingleton<StartupViewModel>();
            services.AddSingleton<DiagnosticsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<PerformanceViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddSingleton<OptimizerEngine>();
            services.AddSingleton<OptimizerViewModel>();
            services.AddSingleton<RamManagerViewModel>();
            services.AddSingleton<DiskManagerViewModel>();
            services.AddSingleton<PrivacyGuardViewModel>();
            services.AddSingleton<ToolsViewModel>();
            services.AddSingleton<InstalledAppsService>();
            services.AddSingleton<DriverService>();
            services.AddSingleton<WingetService>();
            services.AddSingleton<SoftwareViewModel>();
            services.AddSingleton<GamingViewModel>();
            services.AddSingleton<MainViewModel>();
            Services = services.BuildServiceProvider();

            EnableBindingDiagnosticsIfRequested();

            // Localizzazione dal config; accento personalizzato; tema.
            Locale.Set(config.Language);
            ApplyTheme(config.Theme);
            ApplyAccent(config.AccentColor);
            Log("Avvio", null);
            Services.GetRequiredService<FileLogService>()
                .Info($"{ProductName} avviato (tema={config.Theme}, lingua={config.Language})");

            // Onboarding solo alla prima apertura, prima della finestra principale.
            if (!config.OnboardingDone || config.OnboardingVersion < CurrentOnboardingVersion)
            {
                var ob = new Services.OnboardingWindow(config, store);
                ob.ShowDialog();
            }

            var shell = Services.GetRequiredService<MainViewModel>();
            var main = new MainWindow { DataContext = shell };
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            OpenRequestedSection(shell, e.Args);
            // Gli avvisi partono dopo la finestra: il primo controllo non deve
            // competere con il caricamento della dashboard.
            Services.GetRequiredService<NotificationService>().Start();
            _instance.ListenForActivation(() => Dispatcher.Invoke(RestoreMainWindow));
            // Lo score viene calcolato subito in background: la Dashboard non richiede
            // più l'apertura preventiva della pagina Diagnostica.
            _ = Services.GetRequiredService<DiagnosticsViewModel>().RefreshAsync();
            _ = RunAutoSafeCleanIfDueAsync();
            _ = CheckForUpdatesIfDueAsync();
            _ = Task.Run(PurgeOldLogs);
        }
        catch (Exception ex)
        {
            Log("Errore fatale all'avvio", ex);
            // Un errore qui puo' precedere Locale.Set: il dizionario italiano e' il
            // fallback naturale, ed e' comunque meglio di un messaggio vuoto.
            System.Windows.MessageBox.Show(
                Locale.F("app.err.start", [ProductName]),
                ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Manutenzione dei log dell'applicazione: insieme al tetto per file evita che
    /// la cartella cresca senza limite. Tocca solo i file di log di Nexus.
    /// </summary>
    private static void PurgeOldLogs()
    {
        try
        {
            const int keepDays = 14;
            var log = Services.GetService<FileLogService>();
            var removed = log?.Purge(keepDays) ?? 0;
            if (removed > 0) log?.Info($"Manutenzione log: {removed} file più vecchi di {keepDays} giorni rimossi.");
        }
        catch (Exception ex)
        {
            Log("Manutenzione log non completata", ex);
        }
    }

    /// <summary>
    /// Apertura diretta di una sezione da riga di comando: "--page:nav.gaming".
    /// Serve per collegamenti dedicati (per esempio una scorciatoia alla Modalità
    /// Gaming) e per le verifiche automatiche dell'interfaccia.
    /// </summary>
    private static void OpenRequestedSection(MainViewModel shell, string[] args)
    {
        const string prefix = "--page:";
        var request = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (request is null) return;
        var id = request[prefix.Length..].Trim();
        if (id.Length > 0) shell.OpenSection(id);
    }

    /// <summary>
    /// Diagnostica dei binding XAML su richiesta (variabile NEXUS_TRACE_BINDINGS=1):
    /// WPF scrive gli errori di binding solo nel debugger, quindi in una build di
    /// verifica li dirottiamo su un file dedicato. Disattivata di default: nessun
    /// costo a runtime per l'utente finale.
    /// </summary>
    private static void EnableBindingDiagnosticsIfRequested()
    {
        if (Environment.GetEnvironmentVariable("NEXUS_TRACE_BINDINGS") != "1") return;
        try
        {
            var path = Path.Combine(LogFallbackDirectory(), $"bindings-{Environment.ProcessId}.log");
            Directory.CreateDirectory(LogFallbackDirectory());
            var listener = new TextWriterTraceListener(path) { TraceOutputOptions = TraceOptions.None };
            // Riga di intestazione: conferma che la diagnostica è realmente attiva
            // anche quando non viene rilevato alcun errore di binding.
            listener.WriteLine($"# Diagnostica binding attiva — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            listener.Flush();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            Trace.AutoFlush = true;
        }
        catch (Exception ex)
        {
            Log("Diagnostica binding non attivata", ex);
        }
    }

    /// <summary>dark | light | auto (auto legge la preferenza Windows documentata).</summary>
    public static void ApplyTheme(string theme)
    {
        var resolved = theme switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => IsSystemDark() ? "Dark" : "Light",
        };
        // L'assembly WPF e' pubblicato come "NexusOptimizer" (AssemblyName), non "NexusOptimizer.App".
        // Usare il nome effettivo evita FileNotFoundException al primo caricamento del tema.
        var uri = new Uri($"/NexusOptimizer;component/Themes/Theme.{resolved}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        var merged = Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return true; // fallback prudente: tema scuro
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Un errore di rendering/binding puo' essere sollevato piu' volte nello
        // stesso ciclo WPF. Lo registriamo sempre, ma avvisiamo la persona una
        // sola volta: altrimenti il messaggio stesso rende l'app inutilizzabile.
        Log("Eccezione UI non gestita", e.Exception);
        e.Handled = true;
        if (_uiErrorAlreadyReported) return;

        _uiErrorAlreadyReported = true;
        System.Windows.MessageBox.Show(
            Locale.T("app.err.ui"),
            ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Applies an accent hex to the current theme brushes (DynamicResource reacts live).</summary>
    public static void ApplyAccent(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            var soft = System.Windows.Media.Color.FromArgb(0x33, c.R, c.G, c.B);
            var res = Current?.Resources;
            if (res is not null)
            {
                res["AccentBrush"] = new System.Windows.Media.SolidColorBrush(c);
                res["AccentSoftBrush"] = new System.Windows.Media.SolidColorBrush(soft);
            }
        }
        catch { /* hex non valido: si resta sul default */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is not null)
        {
            Services.GetService<SystemMonitor>()?.Dispose();
            Services.GetService<DashboardViewModel>()?.Dispose();
            Services.GetService<CleanCleanViewModel>()?.Dispose();
            Services.GetService<ProcessesViewModel>()?.Dispose();
            Services.GetService<DiagnosticsViewModel>()?.Dispose();
            Services.GetService<RamManagerViewModel>()?.Dispose();
            Services.GetService<GamingViewModel>()?.Dispose();
            Services.GetService<SoftwareViewModel>()?.Dispose();
            Services.GetService<NotificationService>()?.Dispose();
            Services.GetService<UpdateService>()?.Dispose();
            Services.GetService<NotificationsViewModel>()?.Dispose();
            RestoreGamingModeIfActive();
            Services.GetService<FileLogService>()?.Info("Chiusura completata");
            Services.GetService<FileLogService>()?.Dispose();
        }
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Riporta in primo piano la finestra principale su richiesta di un secondo
    /// avvio, anche quando l'applicazione e' ridotta nella tray.
    /// </summary>
    private void RestoreMainWindow()
    {
        try
        {
            if (MainWindow is not Window window) return;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
        catch (Exception ex)
        {
            Log("Ripristino finestra da seconda istanza non riuscito", ex);
        }
    }

    /// <summary>
    /// Controllo aggiornamenti all'avvio: parte solo se attivato dall'utente e con
    /// un canale HTTPS configurato, al massimo una volta ogni 24 ore. In ogni altro
    /// caso l'applicazione non contatta nulla.
    /// </summary>
    private static async Task CheckForUpdatesIfDueAsync()
    {
        try
        {
            await Services.GetRequiredService<UpdateService>().CheckIfDueAsync();
        }
        catch (Exception ex)
        {
            Log("Controllo aggiornamenti all'avvio non riuscito", ex);
        }
    }

    private static void Log(string message, Exception? ex)
    {
        try
        {
            var svc = Services.GetService(typeof(FileLogService)) as FileLogService;
            svc ??= new FileLogService(LogFallbackDirectory());
            if (ex is null) svc.Warning(message);
            else svc.Error(message, ex);
        }
        catch
        {
            /* mai far fallire l'app a causa del logging */
        }
    }

    /// <summary>
    /// Uscita di sicurezza: se la Modalità Gaming è ancora attiva, piano energetico,
    /// priorità e preferenze utente vengono riportati allo stato precedente prima
    /// che il processo termini. Nessuna modifica sopravvive alla chiusura.
    /// </summary>
    private static void RestoreGamingModeIfActive()
    {
        try
        {
            var gaming = Services.GetService<GamingModeService>();
            if (gaming is null || !gaming.IsActive) return;
            gaming.DeactivateAsync().Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Log("Ripristino Modalità Gaming all'uscita non completato", ex);
        }
    }

    private static string LogFallbackDirectory()
        => Path.Combine(ConfigStore.AppDataDirectory, "logs");

    private static async Task RunAutoSafeCleanIfDueAsync()
    {
        try
        {
            var service = Services.GetRequiredService<AutoSafeCleanService>();
            var result = await service.RunIfDueAsync();
            if (result is not null)
                Services.GetRequiredService<FileLogService>().Info(
                    $"Auto Safe Clean completato: {result.ItemsRemoved} elementi, {result.BytesFreed} byte.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Un'opzione automatica non deve mai compromettere l'avvio.
            Services.GetRequiredService<FileLogService>().Error("Auto Safe Clean non completato", ex);
        }
    }
}
