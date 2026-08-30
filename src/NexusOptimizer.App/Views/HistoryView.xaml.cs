using System.Globalization;
using NexusOptimizer.App.ViewModels;
using FrameworkElement = System.Windows.FrameworkElement;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private async void RestoreAllSystemCoreClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel viewModel || !viewModel.CanRestoreSystemCore) return;
        var text = Services.Locale.F("restore.confirm.own", [viewModel.SystemCoreCountText]);
        if (!Confirm(text)) return;
        await viewModel.RestoreAllSystemCoreAsync();
    }

    private async void RestoreSelectedWindowsClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel viewModel || !viewModel.CanRestoreRecommended) return;
        var text = Services.Locale.F("restore.confirm.recommended",
            [viewModel.RecommendedSelectedCount.ToString(CultureInfo.CurrentCulture)]);
        if (!Confirm(text)) return;
        await viewModel.RestoreSelectedRecommendedAsync();
    }

    private async void RestoreChangeClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel viewModel
            || sender is not FrameworkElement { DataContext: ActiveChangeVm item }) return;

        var key = item.IsDetected
            ? "restore.confirm.recommended.one"
            : item.IsChangedAfterApply
                ? "restore.confirm.changed.one"
                : "restore.confirm.own.one";
        if (!Confirm(Services.Locale.F(key, [item.Title]))) return;
        await viewModel.RestoreChangeAsync(item);
    }

    private async void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel viewModel || !viewModel.CanUndo) return;
        var choice = MessageBox.Show(
            Services.Locale.T("restore.confirm.history"),
            Services.Locale.T("restore.confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes)
            await viewModel.UndoSelectedAsync();
    }

    private static bool Confirm(string text)
        => MessageBox.Show(
            text,
            Services.Locale.T("restore.confirm.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
