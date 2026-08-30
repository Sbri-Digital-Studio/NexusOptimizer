using System.Diagnostics;
using Wf = System.Windows.Forms;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Icona di sistema (tray) tramite WinForms NotifyIcon: l'unico punto in cui
/// WinForms e' usato, come da csproj. Menu minimale Ripristina/Esci.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Wf.NotifyIcon _icon;

    public TrayIconService(Action restore, Action exit)
    {
        _icon = new Wf.NotifyIcon
        {
            Text = "Nexus Optimizer",
            Visible = false,
            ContextMenuStrip = BuildMenu(restore, exit),
        };
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            _icon.Icon = exePath is not null && System.IO.File.Exists(exePath)
                ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                : System.Drawing.SystemIcons.Application;
        }
        catch (Exception)
        {
            _icon.Icon = System.Drawing.SystemIcons.Application;
        }
        _icon.DoubleClick += (_, _) => restore();
    }

    private static Wf.ContextMenuStrip BuildMenu(Action restore, Action exit)
    {
        var menu = new Wf.ContextMenuStrip();
        var open = new Wf.ToolStripMenuItem("Apri Nexus Optimizer") { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => restore();
        var quit = new Wf.ToolStripMenuItem("Esci");
        quit.Click += (_, _) => exit();
        menu.Items.Add(open);
        menu.Items.Add(new Wf.ToolStripSeparator());
        menu.Items.Add(quit);
        return menu;
    }

    public void Show() => _icon.Visible = true;

    public void Hide() => _icon.Visible = false;

    /// <summary>
    /// Fumetto di sistema per un avviso. Windows lo mostra solo se l'icona e'
    /// visibile: viene quindi usato unicamente quando l'app e' ridotta nella tray,
    /// dove la campanella non sarebbe raggiungibile.
    /// </summary>
    public void ShowBalloon(string title, string message, bool warning)
    {
        try
        {
            if (!_icon.Visible) return;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = warning ? Wf.ToolTipIcon.Warning : Wf.ToolTipIcon.Info;
            _icon.ShowBalloonTip(8000);
        }
        catch (Exception)
        {
            /* la shell puo' rifiutare il fumetto: l'avviso resta nella campanella */
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
