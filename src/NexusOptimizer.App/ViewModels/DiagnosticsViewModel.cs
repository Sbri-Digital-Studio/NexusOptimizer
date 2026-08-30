using System.Collections.ObjectModel;
using System.Globalization;
using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Health;
using NexusOptimizer.Core.Logging;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.ViewModels;

public sealed class HealthFactorVm(HealthFactor factor) : ObservableBase
{
    public HealthFactor Factor { get; } = factor;
    public string Title => Locale.T($"diagnostics.factor.{Factor.Id}.title");
    public string ScoreText => Factor.IsAvailable
        ? $"{Factor.EarnedPoints}/{Factor.MaximumPoints}"
        : Locale.T("diagnostics.unavailable");
    public string StateText => Locale.T($"diagnostics.state.{Factor.Severity.ToString().ToLowerInvariant()}");
    public WpfBrush StateBrush => Factor.Severity switch
    {
        HealthSeverity.Good => WpfBrushes.MediumSeaGreen,
        HealthSeverity.Critical => WpfBrushes.IndianRed,
        HealthSeverity.Attention => WpfBrushes.Goldenrod,
        _ => WpfBrushes.Gray,
    };

    public string Detail => Factor.Id switch
    {
        "storage" when Factor.Evidence is double ratio => Locale.T("diagnostics.factor.storage.detail")
            .Replace("{0}", ratio.ToString("P0", CultureInfo.CurrentCulture), StringComparison.Ordinal),
        "reliability" when Factor.Evidence is double count => Locale.T("diagnostics.factor.reliability.detail")
            .Replace("{0}", ((int)count).ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal),
        "uptime" when Factor.Evidence is double days => Locale.T("diagnostics.factor.uptime.detail")
            .Replace("{0}", Formatter.Uptime(TimeSpan.FromDays(days)), StringComparison.Ordinal),
        _ => Locale.T("diagnostics.factor.unavailable"),
    };

    public void RefreshLocalized()
    {
        Raise(nameof(Title)); Raise(nameof(ScoreText)); Raise(nameof(StateText)); Raise(nameof(Detail));
    }
}

public sealed class HealthRecommendationVm(HealthRecommendation recommendation) : ObservableBase
{
    public HealthRecommendation Recommendation { get; } = recommendation;
    public string Title => Locale.T($"diagnostics.recommendation.{Recommendation.Id}.title");
    public string Detail => Locale.T($"diagnostics.recommendation.{Recommendation.Id}.detail");
    public WpfBrush StateBrush => Recommendation.Severity switch
    {
        HealthSeverity.Good => WpfBrushes.MediumSeaGreen,
        HealthSeverity.Critical => WpfBrushes.IndianRed,
        HealthSeverity.Attention => WpfBrushes.Goldenrod,
        _ => WpfBrushes.DodgerBlue,
    };

    public void RefreshLocalized() { Raise(nameof(Title)); Raise(nameof(Detail)); }
}

public sealed class CrashIncidentVm(CrashIncident incident)
{
    public CrashIncident Incident { get; } = incident;
    public string DateText => Incident.OccurredAt.ToString("g", CultureInfo.CurrentCulture);
    public string Source => Incident.Source;
    public string EventId => Incident.EventId.ToString(CultureInfo.InvariantCulture);
    public WpfBrush StateBrush => WpfBrushes.IndianRed;
}

/// <summary>Pagina read-only: tutte le letture sono locali e nessuna raccomandazione viene applicata automaticamente.</summary>
public sealed class DiagnosticsViewModel : ObservableBase, IPageLifecycle, IDisposable
{
    private readonly LocalDiagnosticsService _service;
    private readonly HealthAssessmentCache _cache;
    private readonly FileLogService _log;
    private CancellationTokenSource? _scanCts;
    private bool _busy;
    private HealthAssessment? _assessment;
    private string _status = string.Empty;
    private object? _selectedResult;
    private string _selectedResultTitle = string.Empty;
    private string _selectedResultDetail = string.Empty;
    private string _selectedResultMeta = string.Empty;
    private WpfBrush _selectedResultBrush = WpfBrushes.DodgerBlue;

    public DiagnosticsViewModel(LocalDiagnosticsService service, HealthAssessmentCache cache, FileLogService log)
    {
        _service = service;
        _cache = cache;
        _log = log;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
        SelectFactorCommand = new RelayCommand(SelectFactor);
        SelectRecommendationCommand = new RelayCommand(SelectRecommendation);
        SelectCrashCommand = new RelayCommand(SelectCrash);
        Status = Locale.T("diagnostics.ready");
        ResetSelection();
        Locale.Changed += OnLocaleChanged;
    }

    public ObservableCollection<HealthFactorVm> Factors { get; } = [];
    public ObservableCollection<HealthRecommendationVm> Recommendations { get; } = [];
    public ObservableCollection<CrashIncidentVm> CrashEvents { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectFactorCommand { get; }
    public RelayCommand SelectRecommendationCommand { get; }
    public RelayCommand SelectCrashCommand { get; }

    public string PageTitle => Locale.T("diagnostics.title");
    public string PageSubtitle => Locale.T("diagnostics.sub");
    public string RefreshLabel => Locale.T("diagnostics.refresh");
    public string CancelLabel => Locale.T("diagnostics.cancel");
    public string FactorsTitle => Locale.T("diagnostics.factors.title");
    public string RecommendationsTitle => Locale.T("diagnostics.recommendations.title");
    public string CrashesTitle => Locale.T("diagnostics.crashes.title");
    public string CrashDateLabel => Locale.T("diagnostics.crashes.date");
    public string CrashSourceLabel => Locale.T("diagnostics.crashes.source");
    public string CrashEventLabel => Locale.T("diagnostics.crashes.event");
    public string FormulaText => Locale.T("diagnostics.formula");
    public string PrivacyNote => Locale.T("diagnostics.privacy");
    public string ResultClickHint => Locale.T("diagnostics.result.click");
    public string SelectedResultLabel => Locale.T("diagnostics.selection.label");
    public string ScoreText => _assessment?.Score?.ToString(CultureInfo.CurrentCulture) ?? Locale.T("diagnostics.unavailable");
    public string ScoreSuffix => _assessment?.Score is null ? string.Empty : "/100";
    public WpfBrush ScoreBrush => ScoreBrushFor(_assessment?.Score);
    public string ScoreDetail => _assessment is null
        ? Locale.T("diagnostics.score.empty")
        : _assessment.IsPartial ? Locale.T("diagnostics.score.partial") : Locale.T("diagnostics.score.complete");
    public string CrashStatus => _assessment is null
        ? string.Empty
        : _assessment.RecentCrashes.Count == 0
            ? Locale.T("diagnostics.crashes.none")
            : Locale.T("diagnostics.crashes.found")
                .Replace("{0}", _assessment.RecentCrashes.Count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string SelectedResultTitle { get => _selectedResultTitle; private set => Set(ref _selectedResultTitle, value); }
    public string SelectedResultDetail { get => _selectedResultDetail; private set => Set(ref _selectedResultDetail, value); }
    public string SelectedResultMeta { get => _selectedResultMeta; private set => Set(ref _selectedResultMeta, value); }
    public WpfBrush SelectedResultBrush { get => _selectedResultBrush; private set => Set(ref _selectedResultBrush, value); }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            RefreshCommand.RaiseCanExecute();
            CancelCommand.RaiseCanExecute();
        }
    }

    public void Activate()
    {
        if (_assessment is null && _cache.Current is not null) Apply(_cache.Current);
        if (_assessment is null && !IsBusy) _ = RefreshAsync();
    }

    public void Deactivate() => Cancel();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsBusy = true;
        Status = Locale.T("diagnostics.scanning");
        try
        {
            var token = _scanCts.Token;
            var assessment = await Task.Run(() => _service.Assess(token), token);
            Apply(assessment);
            _cache.Publish(assessment);
            Status = Locale.T("diagnostics.done");
        }
        catch (OperationCanceledException)
        {
            Status = Locale.T("diagnostics.cancelled");
        }
        catch (Exception ex)
        {
            _log.Error("Diagnostica non completata", ex);
            Status = Locale.T("diagnostics.error");
        }
        finally
        {
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void Apply(HealthAssessment assessment)
    {
        _assessment = assessment;
        Factors.Clear();
        foreach (var factor in assessment.Factors) Factors.Add(new HealthFactorVm(factor));
        Recommendations.Clear();
        foreach (var recommendation in assessment.Recommendations)
            Recommendations.Add(new HealthRecommendationVm(recommendation));
        CrashEvents.Clear();
        foreach (var incident in assessment.RecentCrashes.Take(12)) CrashEvents.Add(new CrashIncidentVm(incident));
        ResetSelection();
        Raise(nameof(ScoreText)); Raise(nameof(ScoreSuffix)); Raise(nameof(ScoreBrush));
        Raise(nameof(ScoreDetail)); Raise(nameof(CrashStatus));
    }

    private void SelectFactor(object? parameter)
    {
        if (parameter is not HealthFactorVm factor) return;
        _selectedResult = factor;
        SelectedResultTitle = factor.Title;
        SelectedResultDetail = Locale.T($"diagnostics.factor.{factor.Factor.Id}.expanded");
        SelectedResultMeta = $"{factor.ScoreText} · {factor.StateText}";
        SelectedResultBrush = factor.StateBrush;
    }

    private void SelectRecommendation(object? parameter)
    {
        if (parameter is not HealthRecommendationVm recommendation) return;
        _selectedResult = recommendation;
        SelectedResultTitle = recommendation.Title;
        SelectedResultDetail = recommendation.Detail;
        SelectedResultMeta = Locale.T("diagnostics.selection.recommendation");
        SelectedResultBrush = recommendation.StateBrush;
    }

    private void SelectCrash(object? parameter)
    {
        if (parameter is not CrashIncidentVm crash) return;
        _selectedResult = crash;
        SelectedResultTitle = crash.Source;
        SelectedResultDetail = Locale.T("diagnostics.selection.crash")
            .Replace("{0}", crash.DateText, StringComparison.Ordinal)
            .Replace("{1}", crash.EventId, StringComparison.Ordinal);
        SelectedResultMeta = Locale.T("diagnostics.selection.crash.meta");
        SelectedResultBrush = crash.StateBrush;
    }

    private void ResetSelection()
    {
        _selectedResult = null;
        SelectedResultTitle = Locale.T("diagnostics.selection.empty.title");
        SelectedResultDetail = Locale.T("diagnostics.selection.empty.detail");
        SelectedResultMeta = string.Empty;
        SelectedResultBrush = WpfBrushes.DodgerBlue;
    }

    private static WpfBrush ScoreBrushFor(int? score) => score switch
    {
        >= 80 => WpfBrushes.MediumSeaGreen,
        >= 60 => WpfBrushes.Goldenrod,
        >= 0 => WpfBrushes.IndianRed,
        _ => WpfBrushes.Gray,
    };

    private void Cancel()
    {
        if (IsBusy) _scanCts?.Cancel();
    }

    private void OnLocaleChanged()
    {
        Raise(nameof(PageTitle)); Raise(nameof(PageSubtitle)); Raise(nameof(RefreshLabel)); Raise(nameof(CancelLabel));
        Raise(nameof(FactorsTitle)); Raise(nameof(RecommendationsTitle)); Raise(nameof(CrashesTitle));
        Raise(nameof(CrashDateLabel)); Raise(nameof(CrashSourceLabel)); Raise(nameof(CrashEventLabel));
        Raise(nameof(FormulaText)); Raise(nameof(PrivacyNote)); Raise(nameof(ResultClickHint));
        Raise(nameof(SelectedResultLabel)); Raise(nameof(ScoreText)); Raise(nameof(ScoreDetail));
        Raise(nameof(CrashStatus)); Raise(nameof(Status));
        foreach (var factor in Factors) factor.RefreshLocalized();
        foreach (var recommendation in Recommendations) recommendation.RefreshLocalized();
        switch (_selectedResult)
        {
            case HealthFactorVm factor: SelectFactor(factor); break;
            case HealthRecommendationVm recommendation: SelectRecommendation(recommendation); break;
            case CrashIncidentVm crash: SelectCrash(crash); break;
            default: ResetSelection(); break;
        }
    }

    public void Dispose()
    {
        Locale.Changed -= OnLocaleChanged;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}
