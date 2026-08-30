using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
using NexusOptimizer.Core.Notifications;

namespace NexusOptimizer.Tests;

/// <summary>
/// Comportamento della campanella: contatore dei non letti, lettura all'apertura
/// e apertura della sezione collegata. Il testo mostrato deve venire dal
/// dizionario, non dalla chiave grezza.
///
/// Gli avvisi pubblicati DOPO la creazione del ViewModel arrivano alla lista
/// attraverso il dispatcher dell'interfaccia: in una suite di test quel
/// dispatcher può esistere (lo crea ViewSmokeTests) e la consegna diventa
/// asincrona. Per restare deterministici si popola il centro avvisi prima di
/// costruire il ViewModel, e ciò che cambia dopo si verifica sul centro.
/// </summary>
public sealed class NotificationsViewModelTests
{
    private static NotificationRecord DiskAlert() => new()
    {
        Key = "disk.low:C:",
        TitleKey = "notif.disk.title",
        MessageKey = "notif.disk.low.msg",
        MessageArgs = ["C:", "8", "12 GB"],
        Severity = NotificationSeverity.Warning,
        TargetSectionId = "nav.diskmanager",
    };

    private static (NotificationCenter Center, NotificationsViewModel ViewModel) WithAlert()
    {
        var center = new NotificationCenter();
        center.Publish(DiskAlert());
        return (center, new NotificationsViewModel(center));
    }

    [Fact]
    public void PendingAlert_LightsTheBadge_AndOpeningMarksItRead()
    {
        var (center, viewModel) = WithAlert();
        using var _ = viewModel;

        Assert.True(viewModel.HasUnread);
        Assert.Equal(1, viewModel.UnreadCount);
        Assert.Single(viewModel.Items);

        viewModel.IsOpen = true;

        Assert.False(viewModel.HasUnread);
        Assert.Equal(0, center.UnreadCount);
        Assert.Equal(1, center.Count); // letto, non cancellato
    }

    [Fact]
    public void Row_ShowsLocalizedText_NotTheRawKey()
    {
        Locale.Set("it");
        var (_, viewModel) = WithAlert();
        using var _disposable = viewModel;

        var row = viewModel.Items[0];

        Assert.NotEqual("notif.disk.title", row.Title);
        Assert.Contains("C:", row.Message, StringComparison.Ordinal);
        Assert.True(row.HasTarget);
    }

    [Fact]
    public void OpeningARow_ClosesThePanel_AndRequestsItsSection()
    {
        var (_, viewModel) = WithAlert();
        using var _disposable = viewModel;
        string? requested = null;
        viewModel.NavigationRequested += id => requested = id;
        viewModel.IsOpen = true;

        viewModel.OpenItemCommand.Execute(viewModel.Items[0]);

        Assert.Equal("nav.diskmanager", requested);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void Clear_EmptiesTheHistory()
    {
        var (center, viewModel) = WithAlert();
        using var _disposable = viewModel;

        viewModel.ClearCommand.Execute(null);

        Assert.Equal(0, center.Count);
        Assert.False(viewModel.IsOpen);
        Assert.Equal(0, center.UnreadCount);
    }

    [Fact]
    public void UnreadBadge_StopsAtNinePlus()
    {
        var center = new NotificationCenter();
        for (var index = 0; index < 12; index++)
        {
            center.Publish(new NotificationRecord
            {
                Key = "rule-" + index,
                TitleKey = "notif.disk.title",
                MessageKey = "notif.disk.low.msg",
            });
        }

        using var viewModel = new NotificationsViewModel(center);

        Assert.Equal("9+", viewModel.UnreadText);
    }
}
