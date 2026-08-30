namespace NexusOptimizer.Core.Health;

/// <summary>
/// Formula V1 del PC Health Score. È intenzionalmente piccola, documentabile e priva di
/// azioni automatiche: la valutazione non modifica mai il sistema.
/// </summary>
public static class HealthAssessmentEngine
{
    public const int StorageWeight = 40;
    public const int ReliabilityWeight = 40;
    public const int UptimeWeight = 20;

    public static HealthAssessment Assess(HealthInput input, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var factors = new[]
        {
            ScoreStorage(input.SystemDriveTotalBytes, input.SystemDriveFreeBytes),
            ScoreReliability(input.CrashAnalysis),
            ScoreUptime(input.Uptime),
        };
        var available = factors.Where(factor => factor.IsAvailable).ToArray();
        int? score = available.Length == 0
            ? null
            : (int)Math.Round(available.Sum(factor => factor.EarnedPoints) * 100d
                               / available.Sum(factor => factor.MaximumPoints),
                               MidpointRounding.AwayFromZero);

        var recommendations = BuildRecommendations(factors);
        return new HealthAssessment(
            score,
            available.Length != factors.Length,
            generatedAt ?? DateTimeOffset.Now,
            factors,
            recommendations,
            input.CrashAnalysis.IsAvailable ? input.CrashAnalysis.Incidents : []);
    }

    public static HealthFactor ScoreStorage(long? totalBytes, long? freeBytes)
    {
        if (totalBytes is null or <= 0 || freeBytes is null or < 0 || freeBytes > totalBytes)
            return Unavailable("storage");

        var freeRatio = freeBytes.Value / (double)totalBytes.Value;
        return freeRatio switch
        {
            >= .20 => new HealthFactor("storage", StorageWeight, StorageWeight, HealthSeverity.Good, freeRatio),
            >= .10 => new HealthFactor("storage", 28, StorageWeight, HealthSeverity.Attention, freeRatio),
            >= .05 => new HealthFactor("storage", 16, StorageWeight, HealthSeverity.Attention, freeRatio),
            _ => new HealthFactor("storage", 4, StorageWeight, HealthSeverity.Critical, freeRatio),
        };
    }

    public static HealthFactor ScoreReliability(CrashAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.IsAvailable) return Unavailable("reliability");

        var count = analysis.Incidents.Count;
        return count switch
        {
            0 => new HealthFactor("reliability", ReliabilityWeight, ReliabilityWeight, HealthSeverity.Good, count),
            1 => new HealthFactor("reliability", 28, ReliabilityWeight, HealthSeverity.Attention, count),
            2 => new HealthFactor("reliability", 20, ReliabilityWeight, HealthSeverity.Attention, count),
            <= 4 => new HealthFactor("reliability", 12, ReliabilityWeight, HealthSeverity.Attention, count),
            _ => new HealthFactor("reliability", 4, ReliabilityWeight, HealthSeverity.Critical, count),
        };
    }

    public static HealthFactor ScoreUptime(TimeSpan? uptime)
    {
        if (uptime is null || uptime.Value < TimeSpan.Zero) return Unavailable("uptime");

        var days = uptime.Value.TotalDays;
        if (days < 7) return new HealthFactor("uptime", UptimeWeight, UptimeWeight, HealthSeverity.Good, days);
        if (days < 14) return new HealthFactor("uptime", 16, UptimeWeight, HealthSeverity.Good, days);
        if (days < 30) return new HealthFactor("uptime", 10, UptimeWeight, HealthSeverity.Attention, days);
        return new HealthFactor("uptime", 4, UptimeWeight, HealthSeverity.Attention, days);
    }

    private static HealthFactor Unavailable(string id)
        => new(id, 0, 0, HealthSeverity.Unavailable);

    private static IReadOnlyList<HealthRecommendation> BuildRecommendations(IEnumerable<HealthFactor> factors)
    {
        var byId = factors.ToDictionary(factor => factor.Id, StringComparer.Ordinal);
        if (byId.Values.All(factor => !factor.IsAvailable))
            return [new HealthRecommendation("no-data", HealthSeverity.Info)];

        var recommendations = new List<HealthRecommendation>();
        if (byId["storage"].IsAvailable && byId["storage"].Evidence < .10)
            recommendations.Add(new("free-space", byId["storage"].Severity));
        if (byId["reliability"].IsAvailable && byId["reliability"].Evidence > 0)
            recommendations.Add(new("review-crashes", byId["reliability"].Severity));
        if (byId["uptime"].IsAvailable && byId["uptime"].Evidence >= 14)
            recommendations.Add(new("restart", HealthSeverity.Info));
        if (recommendations.Count == 0)
            recommendations.Add(new("all-good", HealthSeverity.Good));
        return recommendations;
    }
}
