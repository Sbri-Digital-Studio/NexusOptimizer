using NexusOptimizer.App.Services;

namespace NexusOptimizer.Tests;

/// <summary>
/// Le due letture "interpretative" della sezione Programmi e driver: la stringa
/// di disinstallazione dichiarata nel Registro e la tabella di winget. Sbagliare
/// qui significa avviare il comando sbagliato o mostrare aggiornamenti inventati.
/// </summary>
public sealed class SoftwareInventoryTests
{
    [Fact]
    public void QuotedUninstallString_KeepsPathAndArgumentsSeparate()
    {
        var (file, arguments) = InstalledAppsService.SplitCommand(
            "\"C:\\Program Files\\Esempio\\unins000.exe\" /SILENT /NORESTART");

        Assert.Equal(@"C:\Program Files\Esempio\unins000.exe", file);
        Assert.Equal("/SILENT /NORESTART", arguments);
    }

    [Fact]
    public void UnquotedExecutable_IsSplitOnTheExtension()
    {
        var (file, arguments) = InstalledAppsService.SplitCommand(@"C:\Tools\setup.exe --uninstall");

        Assert.Equal(@"C:\Tools\setup.exe", file);
        Assert.Equal("--uninstall", arguments);
    }

    [Theory]
    [InlineData("MsiExec.exe /I{2C0A1B4F-0000-4000-8000-000000000001}")]
    [InlineData("msiexec /i{2C0A1B4F-0000-4000-8000-000000000001}")]
    public void MsiInstallSwitch_IsTurnedIntoTheUninstallSwitch(string command)
    {
        var (file, arguments) = InstalledAppsService.SplitCommand(command);

        Assert.Equal("msiexec.exe", file);
        // /I significa "installa": lasciarlo riaprirebbe l'installazione.
        Assert.StartsWith("/X", arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2C0A1B4F", arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyUninstallString_YieldsNothingToRun()
    {
        var (file, arguments) = InstalledAppsService.SplitCommand("   ");

        Assert.Equal("", file);
        Assert.Equal("", arguments);
    }

    // ------------------------------------------------------------ winget

    private const string ItalianOutput = """
        Nome                           Id                        Versione     Disponibile  Origine
        ------------------------------------------------------------------------------------------
        7-Zip 24.09 (x64)              7zip.7zip                 24.09        25.00        winget
        Windows Terminal               Microsoft.WindowsTerminal 1.21.2911.0  1.22.3232.0  winget

        2 aggiornamenti disponibili.
        """;

    private const string EnglishOutput = """
        Name                Id                 Version   Available  Source
        -------------------------------------------------------------------
        Notepad++ (64-bit)  Notepad++.Notepad++ 8.6.9    8.7.1      winget

        1 upgrades available.
        """;

    [Fact]
    public void ItalianTable_IsReadWithoutDependingOnTheHeaderText()
    {
        var updates = WingetService.ParseUpgradeTable(ItalianOutput);

        Assert.Equal(2, updates.Count);
        Assert.Equal("7zip.7zip", updates[0].Id);
        Assert.Equal("24.09", updates[0].CurrentVersion);
        Assert.Equal("25.00", updates[0].AvailableVersion);
        Assert.Equal("Microsoft.WindowsTerminal", updates[1].Id);
        Assert.Equal("1.22.3232.0", updates[1].AvailableVersion);
    }

    [Fact]
    public void EnglishTable_IsReadTheSameWay()
    {
        var updates = WingetService.ParseUpgradeTable(EnglishOutput);

        var single = Assert.Single(updates);
        Assert.Equal("Notepad++.Notepad++", single.Id);
        Assert.Equal("8.7.1", single.AvailableVersion);
    }

    [Fact]
    public void SummaryLineAndSecondTable_AreNotReadAsPackages()
    {
        const string withSecondTable = """
            Nome              Id            Versione  Disponibile  Origine
            ----------------------------------------------------------------
            Esempio Uno       Vendor.Uno    1.0       1.1          winget

            1 aggiornamenti disponibili.
            I pacchetti seguenti richiedono un riferimento esplicito:
            Nome              Id            Versione  Disponibile  Origine
            ----------------------------------------------------------------
            Esempio Due       Vendor.Due    2.0       Sconosciuto  winget
            """;

        var updates = WingetService.ParseUpgradeTable(withSecondTable);

        Assert.Single(updates);
        Assert.Equal("Vendor.Uno", updates[0].Id);
    }


    /// <summary>
    /// Output reale di winget (italiano): il riepilogo finale segue le righe
    /// **senza riga vuota** di mezzo, e non deve diventare un pacchetto.
    /// </summary>
    private const string RealOutput = """
        Nome                                             Id                                Versione       Disponibile    Origine
        ------------------------------------------------------------------------------------------------------------------------
        Microsoft .NET SDK 10.0.301 (x64)                Microsoft.DotNet.SDK.10           10.0.301       10.0.400       winget
        Microsoft GameInput                              Microsoft.GameInput               3.3.221.0      3.4.218        winget
        Microsoft Windows Desktop Runtime - 8.0.23 (x64) Microsoft.DotNet.DesktopRuntime.8 8.0.23         8.0.30         winget
        Outlook for Windows                              Microsoft.Outlook                 1.2026.811.200 1.2026.812.100 winget
        Visual Studio Build Tools 2026                   Microsoft.VisualStudio.BuildTools 18.8.0         18.9.1         winget
        5 aggiornamenti disponibili.
        """;

    [Fact]
    public void RealOutput_IsParsedAndTheSummaryLineIsIgnored()
    {
        var updates = WingetService.ParseUpgradeTable(RealOutput);

        Assert.Equal(5, updates.Count);
        Assert.Equal("Microsoft.DotNet.SDK.10", updates[0].Id);
        Assert.Equal("10.0.400", updates[0].AvailableVersion);
        Assert.Equal("Microsoft Windows Desktop Runtime - 8.0.23 (x64)", updates[2].Name);
        Assert.Equal("1.2026.812.100", updates[3].AvailableVersion);
        Assert.DoesNotContain(updates, u => u.Id.Contains("aggiornamenti", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("winget non è riconosciuto come comando interno o esterno.")]
    public void OutputWithoutATable_YieldsNoUpdates(string output)
        => Assert.Empty(WingetService.ParseUpgradeTable(output));

    [Fact]
    public void ColumnStarts_FindsFiveColumnsEvenWithSpacedTitles()
    {
        var starts = WingetService.ColumnStarts(
            "Nome                Id            Versione  Disponibile  Origine");

        Assert.Equal(5, starts.Count);
        Assert.Equal(0, starts[0]);
    }
}
