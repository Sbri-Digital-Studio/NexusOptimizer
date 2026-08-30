using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class CleanView : UserControl
{
    public CleanView() => InitializeComponent();

    private async void RunClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanCleanViewModel vm) return;

        // Il pulsante Esegui avvia un flusso completo: quando manca una
        // anteprima, la genera prima. In questo modo non sembra disabilitato
        // o rotto, ma la pulizia reale conserva comunque la conferma esplicita.
        if (!vm.HasScan)
        {
            await vm.AnalyzeAsync();
            if (!vm.HasScan) return;
        }

        if (!vm.DryRun)
        {
            var choice = System.Windows.MessageBox.Show(
                Services.Locale.T("clean.confirm.text"),
                Services.Locale.T("clean.confirm.title"), System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (choice != System.Windows.MessageBoxResult.Yes) return;
        }

        await vm.RunAsync();
    }

    private async void DeleteClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CleanCleanViewModel vm || !vm.CanDelete) return;

        var choice = System.Windows.MessageBox.Show(
            "Gli elementi analizzati verranno messi in quarantena cifrata locale e potranno essere ripristinati dalla Cronologia. Continuare?",
            "Conferma eliminazione", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (choice == System.Windows.MessageBoxResult.Yes)
            await vm.DeleteAsync();
    }
}
