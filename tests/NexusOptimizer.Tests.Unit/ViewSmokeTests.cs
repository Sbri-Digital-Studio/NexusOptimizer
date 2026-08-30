using System.Threading;
using System.Windows;
using NexusOptimizer.App.Services;
using NexusOptimizer.App.Views;

namespace NexusOptimizer.Tests;

public sealed class ViewSmokeTests
{
    [Fact]
    public void PremiumDashboardViews_LoadTheirXaml()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/NexusOptimizer;component/Themes/Theme.Dark.xaml", UriKind.Relative),
                });
                Locale.Set("it");

                // Il tema chiaro deve restare caricabile: un errore qui si
                // manifesterebbe solo al primo cambio tema dell'utente.
                var light = new ResourceDictionary
                {
                    Source = new Uri("/NexusOptimizer;component/Themes/Theme.Light.xaml", UriKind.Relative),
                };
                Assert.NotNull(light["ModeLevelButton"]);
                Assert.NotNull(app.Resources["ModeLevelButton"]);

                // Tutte le viste, non solo quelle della dashboard: ognuna ha binding
                // di localizzazione e un errore di XAML qui si vedrebbe altrimenti
                // soltanto aprendo quella pagina.
                Assert.NotNull(new DashboardView().Content);
                Assert.NotNull(new CleanView().Content);
                Assert.NotNull(new DiskManagerView().Content);
                Assert.NotNull(new RamManagerView().Content);
                Assert.NotNull(new ToolsView().Content);
                Assert.NotNull(new OptimizerView().Content);
                Assert.NotNull(new GamingView().Content);
                Assert.NotNull(new SystemInfoView().Content);
                Assert.NotNull(new PerformanceView().Content);
                Assert.NotNull(new ProcessesView().Content);
                Assert.NotNull(new StartupView().Content);
                Assert.NotNull(new DiagnosticsView().Content);
                Assert.NotNull(new PrivacyGuardView().Content);
                Assert.NotNull(new HistoryView().Content);
                Assert.NotNull(new SettingsView().Content);
                Assert.NotNull(new SoftwareView().Content);
                Assert.NotNull(new NexusOptimizer.App.MainWindow().Content);

                // Il cambio lingua deve riscrivere i testi statici senza ricostruire
                // le viste: e' la revisione di Locale.Live a farli rivalutare.
                var before = Locale.Live.Version;
                Locale.Set("en");
                Assert.True(Locale.Live.Version > before);
                Locale.Set("it");
                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Il caricamento XAML non si è concluso.");
        Assert.Null(failure);
    }
}
