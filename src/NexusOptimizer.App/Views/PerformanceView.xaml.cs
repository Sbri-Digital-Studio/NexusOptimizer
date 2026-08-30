using System.Windows.Controls;
// Disambiguazione WPF vs WinForms.
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class PerformanceView : UserControl
{
    public PerformanceView()
    {
        InitializeComponent();
    }
}
