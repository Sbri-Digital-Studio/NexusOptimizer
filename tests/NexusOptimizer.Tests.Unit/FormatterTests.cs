using System.Globalization;
using NexusOptimizer.App.Services;

namespace NexusOptimizer.Tests;

/// <summary>
/// Le nuove unità della dashboard non devono mai inventare un valore: un dato
/// assente resta "n.d." e i numeri seguono la formattazione italiana.
/// </summary>
public sealed class FormatterTests
{
    [Fact]
    public void MissingValues_AreDeclaredUnavailable()
    {
        Assert.Equal(Formatter.Unavailable, Formatter.Mbps(null));
        Assert.Equal(Formatter.Unavailable, Formatter.Celsius(null));
        Assert.Equal(Formatter.Unavailable, Formatter.Clock(null));
        Assert.Equal(Formatter.Unavailable, Formatter.Clock(0));
        Assert.Equal(Formatter.Unavailable, Formatter.Gigabytes(null));
        Assert.Equal(Formatter.Unavailable, Formatter.Count((int?)null));
    }

    [Fact]
    public void Mbps_ConvertsFromKilobytesPerSecond()
    {
        // 1 KB/s = 1024 byte = 8192 bit → 0,0082 Mbps; 10 000 KB/s ≈ 81,9 Mbps
        Assert.Equal("81,9 Mbps", Formatter.Mbps(10_000));
        Assert.Equal("0,0 Mbps", Formatter.Mbps(0));
        Assert.Equal("0,5 KB/s", Formatter.Mbps(0.5));
        Assert.Equal("0,05 KB/s", Formatter.RatePerSec(0.05));
    }

    [Fact]
    public void Clock_SwitchesToGigahertzAboveOneThousand()
    {
        Assert.Equal("4,32 GHz", Formatter.Clock(4320));
        Assert.Equal("800 MHz", Formatter.Clock(800));
    }

    [Fact]
    public void Celsius_RoundsToWholeDegrees()
        => Assert.Equal("56 °C", Formatter.Celsius(55.6));

    [Fact]
    public void Uptime_IsSplitIntoDaysAndClock()
    {
        var span = new TimeSpan(2, 4, 15, 32);
        Assert.Equal("2 Giorni", Formatter.UptimeDays(span));
        Assert.Equal("04:15:32", Formatter.UptimeClock(span));
        Assert.Equal("1 Giorno", Formatter.UptimeDays(TimeSpan.FromDays(1)));
        Assert.Equal("Oggi", Formatter.UptimeDays(TimeSpan.FromHours(3)));
    }

    [Fact]
    public void Gigabytes_UsesItalianDecimalSeparator()
        => Assert.Equal("8,0 GB", Formatter.Gigabytes(8L * 1024 * 1024 * 1024));

    [Fact]
    public void Count_FormatsThousandsConsistently()
        => Assert.Equal(Formatter.Count(1234), Formatter.Count((int?)1234));

    [Fact]
    public void Percent_NeverShowsNegativeOrUnknownValues()
    {
        Assert.Equal(Formatter.Unavailable, Formatter.Percent(null));
        Assert.Equal(Formatter.Unavailable, Formatter.Percent(-1));
        Assert.Equal("39%", Formatter.Percent(38.6, decimals: 0));
        Assert.Contains("%", Formatter.Percent(12.34, decimals: 1), StringComparison.Ordinal);
    }

    [Fact]
    public void ItalianCultureIsUsedRegardlessOfSystemLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            NexusOptimizer.App.Services.Locale.Set("it");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("4,32 GHz", Formatter.Clock(4320));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EnglishInterfaceFormatsNumbersInEnglish()
    {
        // La cultura segue la lingua scelta, non il sistema: con l'interfaccia in
        // inglese un separatore decimale italiano accanto a un testo inglese
        // sarebbe una svista visibile.
        try
        {
            NexusOptimizer.App.Services.Locale.Set("en");
            Assert.Equal("4.32 GHz", Formatter.Clock(4320));
            Assert.Equal("2.5 GB", Formatter.Bytes(2.5 * 1024 * 1024 * 1024));

            NexusOptimizer.App.Services.Locale.Set("it");
            Assert.Equal("4,32 GHz", Formatter.Clock(4320));
            Assert.Equal("2,5 GB", Formatter.Bytes(2.5 * 1024 * 1024 * 1024));
        }
        finally
        {
            NexusOptimizer.App.Services.Locale.Set("it");
        }
    }
}
