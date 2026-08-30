using System.Windows;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace NexusOptimizer.App.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => InitializeComponent();

    private void ResultButton_Click(object sender, RoutedEventArgs e)
    {
        // Il comando aggiorna prima il ViewModel; al giro successivo portiamo il
        // pannello aggiornato dentro il viewport e rendiamo evidente la navigazione.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            SelectedResultPanel.BringIntoView();
            SelectedResultPanel.Focus();
        });
    }
}
