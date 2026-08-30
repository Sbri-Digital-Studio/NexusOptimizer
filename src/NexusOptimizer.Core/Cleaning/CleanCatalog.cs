namespace NexusOptimizer.Core.Cleaning;

/// <summary>Catalogo delle categorie di pulizia con livelli di sicurezza e directory autorizzate.</summary>
public static class CleanCatalog
{
    private static string Env(params string[] parts) => Path.Combine(parts);

    public static readonly IReadOnlyList<CleanCategoryDef> Categories = new[]
    {
        new CleanCategoryDef(
            "user_temp", "Cat_UserTemp", SecurityLevel.Green, true, false,
            new[] { Path.GetTempPath() }),

        new CleanCategoryDef(
            "windows_temp", "Cat_WindowsTemp", SecurityLevel.Green, false, true,
            new[] { Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp") }),

        new CleanCategoryDef(
            "thumbnail_cache", "Cat_Thumbnails", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer") }),

        new CleanCategoryDef(
            "dx_shader_cache", "Cat_DxShaderCache", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache") }),

        new CleanCategoryDef(
            "crash_dumps", "Cat_CrashDumps", SecurityLevel.Yellow, false, false,
            new[]
            {
                Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Minidump"),
            }),

        new CleanCategoryDef(
            "error_reports", "Cat_ErrorReports", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER") }),

        new CleanCategoryDef(
            "windows_update_cache", "Cat_UpdateCache", SecurityLevel.Yellow, false, true,
            new[] { Environment.ExpandEnvironmentVariables(@"%SystemRoot%\SoftwareDistribution\Download") }),

        new CleanCategoryDef(
            "edge_cache", "Cat_EdgeCache", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache") }),

        new CleanCategoryDef(
            "chrome_cache", "Cat_ChromeCache", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache") }),

        new CleanCategoryDef(
            "firefox_cache", "Cat_FirefoxCache", SecurityLevel.Yellow, false, false,
            new[] { Env(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozilla", "Firefox", "Profiles") }),

        // Cestino: gestito separatamente via shell API, non da scanner file.
        new CleanCategoryDef(
            "recycle_bin", "Cat_RecycleBin", SecurityLevel.Green, false, false,
            Array.Empty<string>()),
    };

    public static CleanCategoryDef? GetById(string id) => Categories.FirstOrDefault(c => c.Id == id);
}
