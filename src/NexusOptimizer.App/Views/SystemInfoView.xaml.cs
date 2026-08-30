using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class SystemInfoView : UserControl
{
    public SystemInfoView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SystemInfoViewModel vm) await vm.LoadIfNeededAsync();
        };
    }
}
