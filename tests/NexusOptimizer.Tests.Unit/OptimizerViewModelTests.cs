using NexusOptimizer.App.Services;
using NexusOptimizer.App.ViewModels;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Safety;

namespace NexusOptimizer.Tests;

/// <summary>
/// I test non applicano ottimizzazioni: verificano il piano, il livello operativo
/// e i collegamenti. Applicare davvero significherebbe modificare il PC che esegue
/// la suite, cosa che il progetto non fa mai senza un comando esplicito.
/// </summary>
public sealed class OptimizerViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexus-opt-" + Guid.NewGuid().ToString("N"));
    private AppModeService _mode = null!;

    private OptimizerViewModel CreateViewModel(AppModeLevel level = AppModeLevel.Safe)
    {
        Directory.CreateDirectory(_root);
        var store = new ConfigStore(_root);
        var config = AppConfig.Default;
        config.Mode = level.ToId();
        var log = new FileLogService(Path.Combine(_root, "logs"));
        var startup = new StartupService(config, store, log);
        var safety = new SafetyEngine(Path.Combine(_root, "safety"));
        _mode = new AppModeService(config, store, log);
        return new OptimizerViewModel(new OptimizerEngine(config, store, log, startup, safety, _mode));
    }

    [Fact]
    public void Plan_ExposesEveryOptimization()
    {
        var viewModel = CreateViewModel(AppModeLevel.Expert);

        Assert.Equal(6, viewModel.Items.Count);
        Assert.Equal(6, viewModel.SelectedCount);
        Assert.Equal("APPLICA SELEZIONATE (6)", viewModel.PrepareButtonText);

        viewModel.Items[0].IsSelected = false;
        Assert.Equal(5, viewModel.SelectedCount);
    }

    [Fact]
    public void SafeLevel_LocksEverythingThatWritesSystemPreferences()
    {
        var viewModel = CreateViewModel(AppModeLevel.Safe);

        // In SAFE restano disponibili solo le azioni che non toccano il registro.
        Assert.True(Item(viewModel, "startup").IsUnlocked);
        Assert.True(Item(viewModel, "cache").IsUnlocked);
        Assert.True(Item(viewModel, "memory").IsUnlocked);

        Assert.True(Item(viewModel, "windows").IsLocked);
        Assert.True(Item(viewModel, "visual").IsLocked);
        Assert.True(Item(viewModel, "power").IsLocked);

        // Una voce bloccata non può finire nel lotto da applicare.
        Assert.Equal(3, viewModel.SelectedCount);
        Assert.False(Item(viewModel, "windows").IsSelected);
        Assert.False(Item(viewModel, "windows").ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void LockedAction_CannotBeSelectedByTheDashboardToggle()
    {
        var viewModel = CreateViewModel(AppModeLevel.Safe);
        var windows = Item(viewModel, "windows");

        // La Dashboard usa gli stessi item della pagina Optimizer: anche se un
        // binding prova a spuntare una voce bloccata, il modello la rifiuta.
        windows.IsSelected = true;

        Assert.False(windows.IsSelected);
        Assert.Equal(3, viewModel.SelectedCount);
        Assert.Equal("APPLICA SELEZIONATE (3)", viewModel.PrepareButtonText);
    }

    [Fact]
    public void BalancedLevel_UnlocksUserPreferencesButNotSystemWideChanges()
    {
        var viewModel = CreateViewModel(AppModeLevel.Balanced);

        Assert.True(Item(viewModel, "windows").IsUnlocked);
        Assert.True(Item(viewModel, "visual").IsUnlocked);
        Assert.True(Item(viewModel, "power").IsLocked);
        Assert.Equal("RICHIEDE EXPERT", Item(viewModel, "power").LockText);
    }

    [Fact]
    public void ChangingLevelAtRuntime_UnlocksTheAffectedRows()
    {
        var viewModel = CreateViewModel(AppModeLevel.Safe);
        Assert.True(Item(viewModel, "visual").IsLocked);

        _mode.Set(AppModeLevel.Expert);

        Assert.True(Item(viewModel, "visual").IsUnlocked);
        Assert.True(Item(viewModel, "power").IsUnlocked);
        Assert.Contains("EXPERT", viewModel.LevelSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsPreferences_OpenTheWindowsToolsPage()
    {
        var viewModel = CreateViewModel(AppModeLevel.Balanced);
        var windows = Item(viewModel, "windows");

        // La voce sulle preferenze di Windows non deve rimandare alle impostazioni
        // di Nexus: la sezione di approfondimento è quella degli strumenti Windows.
        Assert.Equal("nav.tools", windows.TargetId);
        Assert.NotEqual("nav.settings", windows.TargetId);
    }

    [Fact]
    public void DetailsArrow_RequestsTheRelatedSection()
    {
        var viewModel = CreateViewModel();
        string? requested = null;
        viewModel.NavigateRequested += id => requested = id;

        viewModel.OpenCommand.Execute(Item(viewModel, "cache").TargetId);

        Assert.Equal("nav.cleancat", requested);
    }

    [Fact]
    public void EveryActionDeclaresItsOwnSectionAndReversibility()
    {
        var viewModel = CreateViewModel(AppModeLevel.Expert);

        foreach (var item in viewModel.Items)
        {
            Assert.StartsWith("nav.", item.TargetId, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Detail));
        }

        // Le due operazioni che non toccano impostazioni persistenti non promettono
        // un annullamento che non potrebbero mantenere.
        Assert.False(Item(viewModel, "cache").IsReversible);
        Assert.False(Item(viewModel, "memory").IsReversible);
        Assert.True(Item(viewModel, "windows").IsReversible);
        Assert.True(Item(viewModel, "visual").IsReversible);
        Assert.True(Item(viewModel, "startup").IsReversible);
        Assert.True(Item(viewModel, "power").IsReversible);
    }

    [Fact]
    public void RestoreCenter_SeparatesExactUndoFromRecommendedWindowsDefaults()
    {
        var viewModel = CreateViewModel(AppModeLevel.Expert);

        Assert.False(Item(viewModel, "startup").CanResetToRecommendedDefaults);
        Assert.True(Item(viewModel, "windows").CanResetToRecommendedDefaults);
        Assert.True(Item(viewModel, "visual").CanResetToRecommendedDefaults);
        Assert.True(Item(viewModel, "power").CanResetToRecommendedDefaults);

        // Il piano energetico usa una chiave tecnica diversa dall'ID della card:
        // il Centro ripristino deve comunque ritrovare il suo snapshot esatto.
        Assert.Equal("powercfg", Item(viewModel, "power").TrackingId);
    }

    [Fact]
    public void HighBenefitIsShownAsAGain_NotAsAWarning()
    {
        var viewModel = CreateViewModel();
        var high = viewModel.Items.First(item => item.Benefit == "Alto");

        Assert.Equal(System.Windows.Media.Brushes.MediumSeaGreen, high.BenefitBrush);
        Assert.Equal(System.Windows.Media.Brushes.MediumSeaGreen,
            viewModel.Items.First(item => item.Risk == "Basso").RiskBrush);
    }

    [Fact]
    public void LevelBlocksNewChanges_NeverTheUndoOfAppliedOnes()
    {
        var viewModel = CreateViewModel(AppModeLevel.Safe);
        var windows = Item(viewModel, "windows");

        // Una voce bloccata dal livello non si può applicare...
        Assert.True(windows.IsLocked);
        Assert.False(windows.ApplyCommand.CanExecute(null));

        // ...ma finché non risulta applicata non offre nemmeno l'annullamento,
        // e quando lo è deve poterlo offrire anche in SAFE.
        Assert.False(windows.CanRevertNow);
        Assert.True(windows.IsReversible);
    }

    private static OptimizerActionVm Item(OptimizerViewModel viewModel, string id)
        => viewModel.Items.Single(item => item.Id == id);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* la pulizia del temporaneo non è parte del test */ }
    }
}
