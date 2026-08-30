using NexusOptimizer.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace NexusOptimizer.App.Views;

public partial class StartupView : UserControl
{
    public StartupView() => InitializeComponent();

    private async void ToggleClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StartupViewModel vm || vm.Selected is null) return;
        var question = Services.Locale.F(
            vm.Selected.IsEnabled ? "startup.ask.disable" : "startup.ask.enable",
            [vm.Selected.Name]);
        var explanation = Services.Locale.T(vm.Selected.IsEnabled
            ? "startup.ask.disable.note"
            : "startup.ask.enable.note");
        var choice = MessageBox.Show(
            question + "\n\n" + explanation,
            Services.Locale.T("startup.ask.title"), MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes)
            await vm.ToggleSelectedAsync();
    }
}
