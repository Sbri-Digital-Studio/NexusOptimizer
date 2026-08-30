using NexusOptimizer.Core.Health;

namespace NexusOptimizer.Tests;

public sealed class HealthAssessmentTests
{
    [Fact]
    public void Assess_HealthyEvidence_ReturnsFullScoreAndNoActionRecommendation()
    {
        var input = new HealthInput(
            SystemDriveTotalBytes: 1000,
            SystemDriveFreeBytes: 350,
            Uptime: TimeSpan.FromDays(2),
            CrashAnalysis: new CrashAnalysis(true, []));

        var assessment = HealthAssessmentEngine.Assess(input);

        Assert.Equal(100, assessment.Score);
        Assert.False(assessment.IsPartial);
        Assert.All(assessment.Factors, factor => Assert.Equal(HealthSeverity.Good, factor.Severity));
        Assert.Contains(assessment.Recommendations, recommendation => recommendation.Id == "all-good");
    }

    [Fact]
    public void Assess_UnavailableSource_NormalizesOnlyAvailableWeights()
    {
        var input = new HealthInput(
            SystemDriveTotalBytes: 1000,
            SystemDriveFreeBytes: 100,
            Uptime: TimeSpan.FromDays(8),
            CrashAnalysis: CrashAnalysis.Unavailable("access denied"));

        var assessment = HealthAssessmentEngine.Assess(input);

        // 28/40 spazio + 16/20 uptime: 44 punti su 60 disponibili, non su 100.
        Assert.Equal(73, assessment.Score);
        Assert.True(assessment.IsPartial);
        Assert.Equal(0, assessment.Factors.Single(factor => factor.Id == "reliability").MaximumPoints);
    }

    [Fact]
    public void Assess_NoAvailableEvidence_DoesNotClaimThePcIsHealthy()
    {
        var assessment = HealthAssessmentEngine.Assess(new HealthInput(
            SystemDriveTotalBytes: null,
            SystemDriveFreeBytes: null,
            Uptime: null,
            CrashAnalysis: CrashAnalysis.Unavailable()));

        Assert.Null(assessment.Score);
        Assert.True(assessment.IsPartial);
        Assert.Contains(assessment.Recommendations, recommendation => recommendation.Id == "no-data");
        Assert.DoesNotContain(assessment.Recommendations, recommendation => recommendation.Id == "all-good");
    }

    [Fact]
    public void Assess_CriticalStorageAndRepeatedCrashes_ProducesConservativeRecommendations()
    {
        var crashes = new CrashAnalysis(true,
        [
            new CrashIncident(DateTime.Now, "Application Error", 1000),
            new CrashIncident(DateTime.Now, "Application Error", 1000),
            new CrashIncident(DateTime.Now, "Application Error", 1000),
            new CrashIncident(DateTime.Now, "Application Hang", 1002),
            new CrashIncident(DateTime.Now, ".NET Runtime", 1026),
        ]);

        var assessment = HealthAssessmentEngine.Assess(new HealthInput(
            SystemDriveTotalBytes: 1000,
            SystemDriveFreeBytes: 40,
            Uptime: TimeSpan.FromDays(35),
            CrashAnalysis: crashes));

        Assert.Equal(HealthSeverity.Critical, assessment.Factors.Single(factor => factor.Id == "storage").Severity);
        Assert.Equal(HealthSeverity.Critical, assessment.Factors.Single(factor => factor.Id == "reliability").Severity);
        Assert.Contains(assessment.Recommendations, recommendation => recommendation.Id == "free-space");
        Assert.Contains(assessment.Recommendations, recommendation => recommendation.Id == "review-crashes");
        Assert.Contains(assessment.Recommendations, recommendation => recommendation.Id == "restart");
    }

    [Fact]
    public void CrashAnalyzer_UsesOnlyKnownCrashSourcesAndWindow()
    {
        var now = DateTime.Now;
        var analysis = CrashAnalyzer.Analyze(
        [
            new CrashIncident(now.AddHours(-1), "Application Error", 1000),
            new CrashIncident(now.AddHours(-2), "Application Error", 42),
            new CrashIncident(now.AddHours(-3), "Custom Service", 1000),
            new CrashIncident(now.AddDays(-10), ".NET Runtime", 1026),
            new CrashIncident(now.AddHours(-4), "Application Hang", 1002),
        ],
        now.AddDays(-7));

        Assert.True(analysis.IsAvailable);
        Assert.Equal(2, analysis.Incidents.Count);
        Assert.All(analysis.Incidents, incident => Assert.True(CrashAnalyzer.IsCrashEvent(incident.Source, incident.EventId)));
    }
}
