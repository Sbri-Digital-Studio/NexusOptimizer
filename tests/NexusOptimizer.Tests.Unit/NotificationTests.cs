using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Notifications;

namespace NexusOptimizer.Tests;

/// <summary>
/// Gli avvisi sono la parte piu' delicata dell'onestà dell'interfaccia: devono
/// nascere solo da una misura oltre soglia, rispettare l'interruttore che li
/// governa e non ripetersi. Qui il tempo e' passato dal test, non dall'orologio.
/// </summary>
public sealed class SystemAlertEvaluatorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AppConfig Config() => new()
    {
        NotifyLowDisk = true,
        NotifyLowDiskPercent = 10,
        NotifyRecoverableSpace = true,
        TemperatureAlerts = true,
        StartupMonitoring = true,
    };

    private static PeriodicReadings Disk(double freePercent, long freeBytes = 4L * 1024 * 1024 * 1024)
        => new() { Drives = [new DriveSpaceReading("C:", freePercent, freeBytes)] };

    [Fact]
    public void LowDisk_BelowThreshold_RaisesWarningOnce()
    {
        var evaluator = new SystemAlertEvaluator(Config());

        var first = evaluator.EvaluatePeriodic(Disk(8), Start);
        var second = evaluator.EvaluatePeriodic(Disk(8), Start.AddMinutes(20));

        Assert.Single(first);
        Assert.Equal(NotificationSeverity.Warning, first[0].Severity);
        Assert.Equal("nav.diskmanager", first[0].TargetSectionId);
        Assert.Empty(second);
    }

    [Fact]
    public void LowDisk_AfterSpaceRecovered_ArmsAgain()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        evaluator.EvaluatePeriodic(Disk(8), Start);

        // Rientro sopra soglia + margine: la regola si riarma...
        Assert.Empty(evaluator.EvaluatePeriodic(Disk(40), Start.AddMinutes(10)));
        // ...e un nuovo calo avvisa di nuovo, anche dentro il cooldown.
        Assert.Single(evaluator.EvaluatePeriodic(Disk(8), Start.AddMinutes(20)));
    }

    [Fact]
    public void LowDisk_UnderFivePercent_IsCritical()
    {
        var evaluator = new SystemAlertEvaluator(Config());

        var alerts = evaluator.EvaluatePeriodic(Disk(3), Start);

        Assert.Single(alerts);
        Assert.Equal(NotificationSeverity.Critical, alerts[0].Severity);
        Assert.Equal("notif.disk.crit.msg", alerts[0].MessageKey);
    }

    [Fact]
    public void LowDisk_Disabled_RaisesNothing()
    {
        var config = Config();
        config.NotifyLowDisk = false;
        var evaluator = new SystemAlertEvaluator(config);

        Assert.Empty(evaluator.EvaluatePeriodic(Disk(1), Start));
    }

    [Fact]
    public void Temperature_NeedsSustainedSamples_BeforeWarning()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        var now = Start;

        for (var sample = 1; sample < SystemAlertEvaluator.SustainedSamples; sample++)
        {
            Assert.Empty(evaluator.EvaluateTemperatures(90, null, now));
            now = now.AddSeconds(1);
        }

        var alerts = evaluator.EvaluateTemperatures(90, null, now);
        Assert.Single(alerts);
        Assert.Equal("notif.temp.cpu.title", alerts[0].TitleKey);
        Assert.Equal(NotificationSeverity.Warning, alerts[0].Severity);
    }

    [Fact]
    public void Temperature_SingleSpike_DoesNotWarn()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        var now = Start;

        for (var sample = 0; sample < SystemAlertEvaluator.SustainedSamples * 2; sample++)
        {
            // Un campione caldo ogni due: la serie consecutiva non si forma mai.
            var value = sample % 2 == 0 ? 92d : 60d;
            Assert.Empty(evaluator.EvaluateTemperatures(value, null, now));
            now = now.AddSeconds(1);
        }
    }

    [Fact]
    public void Temperature_WithoutSensor_RaisesNothing()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        var now = Start;

        for (var sample = 0; sample <= SystemAlertEvaluator.SustainedSamples; sample++)
        {
            Assert.Empty(evaluator.EvaluateTemperatures(null, null, now));
            now = now.AddSeconds(1);
        }
    }

    [Fact]
    public void Temperature_Disabled_RaisesNothing()
    {
        var config = Config();
        config.TemperatureAlerts = false;
        var evaluator = new SystemAlertEvaluator(config);
        var now = Start;

        for (var sample = 0; sample <= SystemAlertEvaluator.SustainedSamples; sample++)
        {
            Assert.Empty(evaluator.EvaluateTemperatures(99, 99, now));
            now = now.AddSeconds(1);
        }
    }

    [Fact]
    public void Temperature_CriticalLevel_IsReportedAsCritical()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        var now = Start;
        IReadOnlyList<NotificationRecord> alerts = [];

        for (var sample = 0; sample <= SystemAlertEvaluator.SustainedSamples; sample++)
        {
            alerts = evaluator.EvaluateTemperatures(null, 99, now);
            if (alerts.Count > 0) break;
            now = now.AddSeconds(1);
        }

        Assert.Single(alerts);
        Assert.Equal(NotificationSeverity.Critical, alerts[0].Severity);
        Assert.Equal("notif.temp.gpu.title", alerts[0].TitleKey);
    }

    [Fact]
    public void RecoverableSpace_OnlyAboveThreshold_AndOnlyWhenEnabled()
    {
        var evaluator = new SystemAlertEvaluator(Config());

        var small = evaluator.EvaluatePeriodic(
            new PeriodicReadings { RecoverableBytes = 512L * 1024 * 1024 }, Start);
        var large = evaluator.EvaluatePeriodic(
            new PeriodicReadings { RecoverableBytes = 6L * 1024 * 1024 * 1024 }, Start.AddMinutes(1));

        Assert.Empty(small);
        Assert.Single(large);
        Assert.Equal(NotificationSeverity.Info, large[0].Severity);
        Assert.Equal("nav.cleancat", large[0].TargetSectionId);

        var disabled = Config();
        disabled.NotifyRecoverableSpace = false;
        Assert.Empty(new SystemAlertEvaluator(disabled).EvaluatePeriodic(
            new PeriodicReadings { RecoverableBytes = 6L * 1024 * 1024 * 1024 }, Start));
    }

    [Fact]
    public void NewStartupEntry_IsAnnouncedOnlyOnce()
    {
        var evaluator = new SystemAlertEvaluator(Config());
        var readings = new PeriodicReadings { NewStartupEntries = ["Esempio"] };

        var first = evaluator.EvaluatePeriodic(readings, Start);
        var second = evaluator.EvaluatePeriodic(readings, Start.AddDays(2));

        Assert.Single(first);
        Assert.Equal("nav.startup", first[0].TargetSectionId);
        Assert.Equal("Esempio", Assert.Single(first[0].MessageArgs));
        Assert.Empty(second);
    }

    [Fact]
    public void StartupMonitoring_Disabled_RaisesNothing()
    {
        var config = Config();
        config.StartupMonitoring = false;
        var evaluator = new SystemAlertEvaluator(config);

        Assert.Empty(evaluator.EvaluatePeriodic(
            new PeriodicReadings { NewStartupEntries = ["Esempio"] }, Start));
    }
}

public sealed class NotificationCenterTests
{
    private static NotificationRecord Record(string key, DateTime createdUtc) => new()
    {
        Key = key,
        TitleKey = "notif.disk.title",
        MessageKey = "notif.disk.low.msg",
        CreatedUtc = createdUtc,
    };

    [Fact]
    public void Publish_SameKeyWithinWindow_IsRejected()
    {
        var center = new NotificationCenter();
        var now = DateTime.UtcNow;

        Assert.True(center.Publish(Record("disk.low:C:", now)));
        Assert.False(center.Publish(Record("disk.low:C:", now.AddMinutes(1))));
        Assert.Equal(1, center.Count);
    }

    [Fact]
    public void Publish_BeyondWindow_IsAccepted()
    {
        var center = new NotificationCenter();
        var now = DateTime.UtcNow;

        center.Publish(Record("disk.low:C:", now));
        Assert.True(center.Publish(Record("disk.low:C:",
            now + NotificationCenter.DeduplicationWindow + TimeSpan.FromMinutes(1))));
        Assert.Equal(2, center.Count);
    }

    [Fact]
    public void History_IsCapped_AndNewestFirst()
    {
        var center = new NotificationCenter();
        var now = DateTime.UtcNow;

        for (var index = 0; index < NotificationCenter.MaxItems + 12; index++)
            center.Publish(Record("rule-" + index, now.AddSeconds(index)));

        Assert.Equal(NotificationCenter.MaxItems, center.Count);
        Assert.Equal("rule-" + (NotificationCenter.MaxItems + 11), center.Items[0].Key);
    }

    [Fact]
    public void MarkAllRead_ClearsUnreadCounter()
    {
        var center = new NotificationCenter();
        center.Publish(Record("a", DateTime.UtcNow));
        center.Publish(Record("b", DateTime.UtcNow));

        Assert.Equal(2, center.UnreadCount);
        center.MarkAllRead();
        Assert.Equal(0, center.UnreadCount);
        Assert.Equal(2, center.Count);
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        var center = new NotificationCenter();
        center.Publish(Record("a", DateTime.UtcNow));

        center.Clear();

        Assert.Equal(0, center.Count);
        Assert.Empty(center.Items);
    }
}
