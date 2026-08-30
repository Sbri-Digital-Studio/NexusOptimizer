using System.Windows.Controls;
// Disambiguazione WPF vs WinForms (UseWindowsForms=true nel csproj).
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
