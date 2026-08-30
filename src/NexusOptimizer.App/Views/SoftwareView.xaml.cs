using NexusOptimizer.App.ViewModels;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class SoftwareView : UserControl
{
    public SoftwareView() => InitializeComponent();

    /// <summary>
    /// La disinstallazione parte solo dopo una conferma esplicita: da qui in poi
    /// comanda il programma di disinstallazione dell'autore del software, con la
    /// sua interfaccia e le sue domande.
    /// </summary>
    private async void UninstallClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SoftwareViewModel vm || vm.SelectedApp is null) return;

        var choice = MessageBox.Show(
            Services.Locale.F("soft.uninstall.confirm", [vm.SelectedApp.Name]),
            Services.Locale.T("soft.uninstall.confirm.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;

        await vm.UninstallSelectedAsync();
    }
}
