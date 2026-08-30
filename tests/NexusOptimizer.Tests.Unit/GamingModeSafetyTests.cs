using NexusOptimizer.App.Services;
using NexusOptimizer.Core.Configuration;

namespace NexusOptimizer.Tests;

/// <summary>
/// Gate di sicurezza della Modalità Gaming: il catalogo delle app chiudibili e il
/// perimetro protetto non devono mai sovrapporsi, e i livelli non devono mai
/// pre-selezionare qualcosa di più permissivo di quanto dichiarato.
/// </summary>
public sealed class GamingModeSafetyTests
{
    private static readonly string[] MustBeProtected =
    [
        // kernel e sessione
        "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "explorer", "dwm",
        // sicurezza
        "msmpeng", "securityhealthservice", "avp", "ekrn", "bdagent",
        // driver grafici
        "nvcontainer", "atieclxx",
        // piattaforme di gioco e anti-cheat: chiuderli interromperebbe la partita
        "steam", "steamwebhelper", "easyanticheat", "battleye", "vgc",
        // il programma stesso
        "nexusoptimizer",
    ];

    [Fact]
    [Trait("Category", "GamingSafety")]
    public void ProtectedProcesses_CoverSystemSecurityAndAntiCheat()
    {
        foreach (var name in MustBeProtected)
            Assert.True(ProtectedProcesses.IsProtected(name), $"'{name}' deve restare nel perimetro protetto");
    }

    [Fact]
    [Trait("Category", "GamingSafety")]
    public void ProtectedProcesses_AreCaseInsensitive()
    {
        Assert.True(ProtectedProcesses.IsProtected("LSASS"));
        Assert.True(ProtectedProcesses.IsProtected("Steam"));
        Assert.False(ProtectedProcesses.IsProtected("spotify"));
    }

    [Fact]
    [Trait("Category", "GamingSafety")]
    public void Catalog_NeverProposesAProtectedProcess()
    {
        foreach (var name in MustBeProtected)
            Assert.Null(BackgroundAppCatalog.Find(name));
    }

    [Fact]
    [Trait("Category", "GamingSafety")]
    public void Catalog_KnowsCommonBackgroundApps()
    {
        Assert.NotNull(BackgroundAppCatalog.Find("onedrive"));
        Assert.NotNull(BackgroundAppCatalog.Find("chrome"));
        Assert.NotNull(BackgroundAppCatalog.Find("SPOTIFY")); // ricerca case-insensitive
    }

    [Fact]
    [Trait("Category", "GamingSafety")]
    public void SafeLevel_PreselectsOnlyRestartableApps()
    {
        // In SAFE possono essere pre-selezionate solo voci che si riaprono da sole
        // e non possono contenere lavoro non salvato: mai browser, chat o launcher.
        var browser = BackgroundAppCatalog.Find("chrome");
        var chat = BackgroundAppCatalog.Find("discord");
        var launcher = BackgroundAppCatalog.Find("epicgameslauncher");
        var sync = BackgroundAppCatalog.Find("onedrive");

        Assert.NotNull(browser);
        Assert.NotNull(chat);
        Assert.NotNull(launcher);
        Assert.NotNull(sync);

        Assert.True(browser!.DefaultFromLevel > AppModeLevel.Safe);
        Assert.True(chat!.DefaultFromLevel > AppModeLevel.Safe);
        Assert.True(launcher!.DefaultFromLevel > AppModeLevel.Safe);
        Assert.Equal(AppModeLevel.Safe, sync!.DefaultFromLevel);
    }

    [Fact]
    public void AppModeLevels_ParseAndSerializeRoundTrip()
    {
        Assert.Equal(AppModeLevel.Safe, AppModeLevels.Parse(null));
        Assert.Equal(AppModeLevel.Safe, AppModeLevels.Parse("sconosciuto"));
        Assert.Equal(AppModeLevel.Balanced, AppModeLevels.Parse("BALANCED"));
        Assert.Equal(AppModeLevel.Expert, AppModeLevels.Parse(" expert "));

        foreach (var level in new[] { AppModeLevel.Safe, AppModeLevel.Balanced, AppModeLevel.Expert })
            Assert.Equal(level, AppModeLevels.Parse(level.ToId()));
    }

    [Fact]
    public void DefaultConfiguration_StartsInSafeMode()
        => Assert.Equal(AppModeLevel.Safe, AppModeLevels.Parse(AppConfig.Default.Mode));
}
