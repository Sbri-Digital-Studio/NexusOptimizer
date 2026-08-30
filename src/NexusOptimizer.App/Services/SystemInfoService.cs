using System.Management;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NexusOptimizer.App.Services;

/// <summary>Sezione espandibile con righe chiave/valore per la pagina "Il mio PC".</summary>
public sealed record SystemInfoRow(string Key, string Value);

public sealed class SystemInfoSection
{
    public string Title { get; init; } = "";

    /// <summary>Icona del catalogo interno mostrata nell'intestazione della card.</summary>
    public string IconKind { get; init; } = "info";

    /// <summary>Colore funzionale del componente (stessa palette della dashboard).</summary>
    public WpfBrush Accent { get; init; } = WpfBrushes.SlateGray;

    /// <summary>Riga di sintesi: il dato più importante, leggibile senza scorrere.</summary>
    public string Caption { get; set; } = "";

    public List<SystemInfoRow> Rows { get; } = [];

    public bool HasCaption => Caption.Length > 0;
}

/// <summary>
/// Raccolta di informazioni hardware/sistema tramite WMI documentato (Win32_*).
/// Ogni lettura è tollerante: un dato assente diventa "—" (mai valori inventati).
/// Nessuna elevazione: i campi che richiedono admin non vengono simulati.
/// </summary>
public sealed class SystemInfoService
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";

    public List<SystemInfoSection> Collect()
    {
        var sections = new List<SystemInfoSection>();
        sections.Add(SystemSection());
        sections.Add(CpuSection());
        sections.Add(RamSection());
        sections.Add(StorageSection());
        sections.Add(GpuSection());
        sections.Add(MotherboardSection());
        return sections;
    }

    /// <summary>
    /// Riepilogo della dashboard: le stesse fonti WMI della pagina "Il mio PC",
    /// ridotte a righe con titolo e dettaglio. Ogni valore assente diventa "n.d."
    /// e una singola lettura fallita non interrompe le altre.
    /// </summary>
    public DashboardSystemSummary CollectDashboardSummary()
    {
        var facts = new List<DashboardSystemFact>();

        var os = ReadOperatingSystemFact();
        if (os is not null) facts.Add(os);

        var board = ReadMotherboardFact();
        if (board is not null) facts.Add(board);

        var cpu = ReadProcessorFact();
        if (cpu is not null) facts.Add(cpu);

        var memory = ReadMemoryFact();
        if (memory is not null) facts.Add(memory);

        var (gpu, vram) = ReadGraphicsFact();
        if (gpu is not null) facts.Add(gpu);

        var disks = ReadDashboardDisks();
        var primary = disks.FirstOrDefault(d => d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                      ?? (disks.Count > 0 ? disks[0] : null);
        if (primary is not null)
            facts.Add(new DashboardSystemFact("disk", primary.Model, primary.TypeText, WpfBrushes.CornflowerBlue));

        return new DashboardSystemSummary(facts, primary?.Name ?? Unavailable, disks, vram);
    }

    private static string Unavailable => Formatter.Unavailable;

    private static DashboardSystemFact? ReadOperatingSystemFact()
    {
        string caption = "", version = "", build = "";
        QueryFirst("Win32_OperatingSystem", mo =>
        {
            caption = moStr(mo, "Caption").Replace("Microsoft ", "", StringComparison.OrdinalIgnoreCase);
            version = moStr(mo, "Version");
            build = moStr(mo, "BuildNumber");
        });
        if (caption.Length == 0) return null;

        var architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        var display = ReadRegistryString(CurrentVersionKey, "DisplayVersion");
        var revision = ReadRegistryInt(CurrentVersionKey, "UBR");

        var title = display.Length > 0
            ? $"{caption} {architecture} ({display})"
            : $"{caption} {architecture}";
        var detail = build.Length == 0
            ? Locale.F("pc.os.version", [version])
            : Locale.F("pc.os.build", [revision is int ubr ? $"{build}.{ubr}" : build]);
        return new DashboardSystemFact("monitor", title.Trim(), detail, WpfBrushes.DeepSkyBlue);
    }

    private static DashboardSystemFact? ReadMotherboardFact()
    {
        string manufacturer = "", product = "";
        QueryFirst("Win32_BaseBoard", mo =>
        {
            manufacturer = moStr(mo, "Manufacturer");
            product = moStr(mo, "Product");
        });
        string bios = "", biosDate = "";
        QueryFirst("Win32_BIOS", mo =>
        {
            bios = moStr(mo, "SMBIOSBIOSVersion");
            biosDate = FormatWmiDate(moStr(mo, "ReleaseDate"));
        });

        var title = JoinNonEmpty(manufacturer, product);
        if (title == "—") return null;
        var detail = bios.Length == 0
            ? Locale.T("pc.bios.none")
            : biosDate.Length == 0
                ? Locale.F("pc.bios.version", [bios])
                : Locale.F("pc.bios.versiondate", [bios, biosDate]);
        return new DashboardSystemFact("motherboard", title, detail, WpfBrushes.LightSlateGray);
    }

    private static DashboardSystemFact? ReadProcessorFact()
    {
        var name = "";
        var cores = 0;
        var threads = 0;
        var maxClock = 0;
        QueryFirst("Win32_Processor", mo =>
        {
            name = moStr(mo, "Name");
            cores = ToInt(mo["NumberOfCores"]);
            threads = ToInt(mo["NumberOfLogicalProcessors"]);
            maxClock = ToInt(mo["MaxClockSpeed"]);
        });
        if (name.Length == 0) return null;

        var parts = new List<string>();
        if (cores > 0) parts.Add(Locale.P(cores, "pc.cpu.core.one", "pc.cpu.core.many"));
        if (threads > 0) parts.Add(Locale.P(threads, "pc.cpu.thread.one", "pc.cpu.thread.many"));
        if (maxClock > 0)
            parts.Add(Locale.F("pc.cpu.baseclock",
                [(maxClock / 1000d).ToString("0.00", CultureInfo.CurrentCulture)]));
        return new DashboardSystemFact("cpuMini", name,
            parts.Count == 0 ? Unavailable : string.Join(" · ", parts), WpfBrushes.LightSlateGray);
    }

    private static DashboardSystemFact? ReadMemoryFact()
    {
        long total = 0;
        var banks = 0;
        var speed = 0;
        var type = "";
        QueryAll("Win32_PhysicalMemory", mo =>
        {
            banks++;
            total += Convert.ToInt64(mo["Capacity"] ?? 0, CultureInfo.InvariantCulture);
            speed = Math.Max(speed, ToInt(mo["Speed"]));
            var memoryType = DescribeMemoryType(ToInt(mo["SMBIOSMemoryType"]));
            if (memoryType.Length > 0) type = memoryType;
        });
        if (total <= 0) return null;

        var title = Bytes(total)
                    + (type.Length > 0 ? " " + type : "")
                    + (speed > 0 ? $" {speed} MHz" : "");
        var detail = banks switch
        {
            0 => Unavailable,
            1 => Locale.T("pc.ram.onebank"),
            _ => Locale.F("pc.ram.banks", [banks.ToString(CultureInfo.CurrentCulture), Bytes(total / banks)]),
        };
        return new DashboardSystemFact("memory", title, detail, WpfBrushes.LightSlateGray);
    }

    private static (DashboardSystemFact? Fact, double? VramBytes) ReadGraphicsFact()
    {
        string name = "", driver = "";
        double? vram = null;
        var found = false;
        QueryAll("Win32_VideoController", mo =>
        {
            if (found) return;
            found = true;
            name = moStr(mo, "Name");
            driver = moStr(mo, "DriverVersion");
            var exact = TryReadDedicatedVideoMemory(name);
            var wmi = Convert.ToInt64(mo["AdapterRAM"] ?? 0, CultureInfo.InvariantCulture);
            vram = exact is > 0 ? exact : wmi > 0 ? wmi : null;
        });
        if (name.Length == 0) return (null, null);

        var parts = new List<string>();
        if (vram is double bytes) parts.Add(Locale.F("pc.gpu.vram", [Bytes((long)bytes)]));
        if (driver.Length > 0) parts.Add(Locale.F("pc.gpu.driver", [driver]));
        return (new DashboardSystemFact("gpu", name,
            parts.Count == 0 ? Unavailable : string.Join(" · ", parts), WpfBrushes.YellowGreen), vram);
    }

    /// <summary>
    /// Volumi locali arricchiti con i dati del disco fisico (modello, bus, tipo
    /// supporto, stato di salute). Le associazioni WMI mancanti degradano a "n.d.".
    /// </summary>
    private static IReadOnlyList<DashboardDiskSummary> ReadDashboardDisks()
    {
        var physical = ReadPhysicalDisksByLetter();
        var result = new List<DashboardDiskSummary>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                        continue;
                    var total = drive.TotalSize;
                    var free = drive.AvailableFreeSpace;
                    if (total <= 0) continue;

                    var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
                    var used = Math.Clamp(100d * (total - free) / total, 0, 100);
                    var ratio = free / (double)total;
                    var brush = ratio >= .20 ? WpfBrushes.MediumSeaGreen
                        : ratio >= .10 ? WpfBrushes.Goldenrod
                        : WpfBrushes.IndianRed;

                    var info = physical.GetValueOrDefault(letter);
                    var model = info is not null
                        ? $"{info.Model} ({letter})"
                        : string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? $"{Locale.T("pc.disk.local")} ({letter})"
                            : $"{drive.VolumeLabel} ({letter})";
                    var typeText = info is not null
                        ? $"{info.Media} — {Bytes(info.Size > 0 ? info.Size : total)} ({letter})"
                        : $"{drive.DriveFormat} — {Bytes(total)} ({letter})";

                    result.Add(new DashboardDiskSummary(
                        letter,
                        model,
                        Locale.F("pc.disk.usage", [Bytes(total - free), Bytes(free)]),
                        Locale.F("pc.disk.health",
                            [info?.Health is { Length: > 0 } health ? health : Unavailable]),
                        typeText,
                        used,
                        brush));
                }
                catch { /* il volume puo' smontarsi durante la scansione */ }
            }
        }
        catch { /* DriveInfo non disponibile: la home mostra un elenco vuoto */ }
        return result;
    }

    private sealed record PhysicalDiskInfo(string Model, string Media, string Health, long Size);

    /// <summary>
    /// Mappa lettera di unita' -> disco fisico usando le associazioni WMI documentate
    /// (Win32_DiskPartition e Win32_LogicalDiskToPartition).
    /// </summary>
    private static Dictionary<string, PhysicalDiskInfo> ReadPhysicalDisksByLetter()
    {
        var byIndex = new Dictionary<int, PhysicalDiskInfo>();
        var storage = ReadStorageDetails();
        try
        {
            using var drives = new ManagementObjectSearcher(
                "SELECT Index, Model, Size, InterfaceType, Status FROM Win32_DiskDrive");
            foreach (var drive in drives.Get().Cast<ManagementObject>())
            {
                using (drive)
                {
                    var index = ToInt(drive["Index"]);
                    var model = moStr(drive, "Model");
                    var size = Convert.ToInt64(drive["Size"] ?? 0, CultureInfo.InvariantCulture);
                    var detail = storage.GetValueOrDefault(index);
                    var media = detail?.Media is { Length: > 0 } m ? m : moStr(drive, "InterfaceType");
                    var health = detail?.Health is { Length: > 0 } h ? h : moStr(drive, "Status");
                    byIndex[index] = new PhysicalDiskInfo(
                        model.Length == 0 ? Locale.T("pc.disk.local") : model,
                        media.Length == 0 ? Unavailable : media,
                        health,
                        size);
                }
            }
        }
        catch { /* WMI non disponibile: si resta sui dati DriveInfo */ }

        var byLetter = new Dictionary<string, PhysicalDiskInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var partitions = new ManagementObjectSearcher(
                "SELECT DiskIndex, DeviceID FROM Win32_DiskPartition");
            foreach (var partition in partitions.Get().Cast<ManagementObject>())
            {
                using (partition)
                {
                    var diskIndex = ToInt(partition["DiskIndex"]);
                    if (!byIndex.TryGetValue(diskIndex, out var info)) continue;
                    var partitionId = moStr(partition, "DeviceID");
                    if (partitionId.Length == 0) continue;

                    using var logical = new ManagementObjectSearcher(
                        "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + partitionId + "'}"
                        + " WHERE AssocClass = Win32_LogicalDiskToPartition");
                    foreach (var disk in logical.Get().Cast<ManagementObject>())
                    {
                        using (disk)
                        {
                            var id = moStr(disk, "DeviceID");
                            if (id.Length > 0) byLetter[id] = info;
                        }
                    }
                }
            }
        }
        catch { /* associazioni non disponibili: il riepilogo resta senza modello */ }
        return byLetter;
    }

    private sealed record StorageDetail(string Media, string Health);

    /// <summary>MSFT_PhysicalDisk espone bus, tipo di supporto e salute reali.</summary>
    private static Dictionary<int, StorageDetail> ReadStorageDetails()
    {
        var result = new Dictionary<int, StorageDetail>();
        try
        {
            var scope = new ManagementScope(StorageNamespace);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DeviceId, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk"));
            foreach (var disk in searcher.Get().Cast<ManagementObject>())
            {
                using (disk)
                {
                    if (!int.TryParse(moStr(disk, "DeviceId"), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var id)) continue;
                    var media = DescribeMedia(ToInt(disk["MediaType"]), ToInt(disk["BusType"]));
                    var health = ToInt(disk["HealthStatus"]) switch
                    {
                        0 => "OK",
                        1 => Locale.T("pc.health.warning"),
                        2 => Locale.T("pc.health.critical"),
                        _ => "",
                    };
                    result[id] = new StorageDetail(media, health);
                }
            }
        }
        catch { /* namespace Storage non disponibile: fallback su Win32_DiskDrive */ }
        return result;
    }

    private static string DescribeMedia(int mediaType, int busType)
    {
        var media = mediaType switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };
        var bus = busType switch { 17 => "NVMe", 11 => "SATA", 10 => "SAS", 8 => "RAID", 7 => "USB", _ => "" };
        var text = string.Join(" ", new[] { bus, media }.Where(v => v.Length > 0));
        return text;
    }

    private static string DescribeMemoryType(int smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        _ => "",
    };

    private static int ToInt(object? value)
    {
        try { return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception) { return 0; }
    }

    private static string ReadRegistryString(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as string ?? "";
        }
        catch (Exception) { return ""; }
    }

    private static int? ReadRegistryInt(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as int?;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Converte una data WMI (yyyyMMdd...) nel formato locale breve.</summary>
    private static string FormatWmiDate(string value)
    {
        if (value.Length < 8) return "";
        return DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : "";
    }

    private static string JoinNonEmpty(params string[] values)
    {
        var clean = values.Where(v => !string.IsNullOrWhiteSpace(v) && v != "—").ToArray();
        return clean.Length == 0 ? "—" : string.Join(" ", clean);
    }

    private static SystemInfoSection SystemSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.os"),
            IconKind = "monitor",
            Accent = WpfBrushes.DeepSkyBlue,
        };
        QueryFirst("Win32_OperatingSystem", mo =>
        {
            var caption = moStr(mo, "Caption").Replace("Microsoft ", "", StringComparison.OrdinalIgnoreCase);
            s.Caption = caption;
            AddRow(s, Locale.T("pc.row.edition"), caption);
            var display = ReadRegistryString(CurrentVersionKey, "DisplayVersion");
            AddRow(s, Locale.T("pc.row.version"), display.Length > 0 ? display : moStr(mo, "Version"));
            var revision = ReadRegistryInt(CurrentVersionKey, "UBR");
            var build = moStr(mo, "BuildNumber");
            AddRow(s, Locale.T("pc.row.build"), revision is int ubr && build.Length > 0 ? $"{build}.{ubr}" : build);
            AddRow(s, Locale.T("pc.row.architecture"), moStr(mo, "OSArchitecture"));
            // WMI restituisce la data nel formato CIM (yyyyMMddHHmmss.ffffff+UUU):
            // senza conversione l'utente leggerebbe una stringa di 25 cifre.
            AddRow(s, Locale.T("pc.row.installed"), mo["InstallDate"] is DateTime installed
                ? installed.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
                : FormatWmiDate(moStr(mo, "InstallDate")));
        });
        QueryFirst("Win32_ComputerSystem", mo =>
        {
            AddRow(s, Locale.T("pc.row.computer"), moStr(mo, "Name"));
            AddRow(s, Locale.T("pc.row.user"), moStr(mo, "UserName"));
            AddRow(s, Locale.T("pc.row.model"), JoinNonEmpty(moStr(mo, "Manufacturer"), moStr(mo, "Model")));
        });
        return s;
    }

    private static SystemInfoSection CpuSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.cpu"),
            IconKind = "cpuMini",
            Accent = WpfBrushes.CornflowerBlue,
        };
        QueryFirst("Win32_Processor", mo =>
        {
            s.Caption = moStr(mo, "Name");
            AddRow(s, Locale.T("pc.row.model"), moStr(mo, "Name"));
            AddRow(s, Locale.T("pc.row.vendor"), moStr(mo, "Manufacturer"));
            var cores = ToInt(mo["NumberOfCores"]);
            var threads = ToInt(mo["NumberOfLogicalProcessors"]);
            AddRow(s, Locale.T("pc.row.corethread"), cores > 0 && threads > 0 ? $"{cores} / {threads}" : "");
            AddRow(s, Locale.T("pc.row.baseclock"), Megahertz(ToInt(mo["MaxClockSpeed"])));
            AddRow(s, Locale.F("pc.row.cache", ["L2"]), Kilobytes(ToInt(mo["L2CacheSize"])));
            AddRow(s, Locale.F("pc.row.cache", ["L3"]), Kilobytes(ToInt(mo["L3CacheSize"])));
            AddRow(s, Locale.T("pc.row.socket"), moStr(mo, "SocketDesignation"));
            AddRow(s, Locale.T("pc.row.virtualization"), moBool(mo, "VirtualizationFirmwareEnabled"));
        });
        return s;
    }

    private static SystemInfoSection RamSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.ram"),
            IconKind = "memory",
            Accent = WpfBrushes.MediumPurple,
        };
        long total = 0;
        var banks = 0;
        var type = "";
        var speed = 0;
        QueryAll("Win32_PhysicalMemory", mo =>
        {
            banks++;
            var capacity = Convert.ToInt64(mo["Capacity"] ?? 0, CultureInfo.InvariantCulture);
            total += capacity;
            speed = Math.Max(speed, ToInt(mo["Speed"]));
            var memoryType = DescribeMemoryType(ToInt(mo["SMBIOSMemoryType"]));
            if (memoryType.Length > 0) type = memoryType;

            var slot = moStr(mo, "DeviceLocator");
            var details = new List<string> { Bytes(capacity) };
            if (memoryType.Length > 0) details.Add(memoryType);
            var bankSpeed = ToInt(mo["Speed"]);
            if (bankSpeed > 0) details.Add($"{bankSpeed} MHz");
            var manufacturer = moStr(mo, "Manufacturer");
            if (manufacturer.Length > 0) details.Add(manufacturer);
            AddRow(s, Locale.F("pc.row.bank", [slot.Length > 0 ? slot : banks.ToString(CultureInfo.CurrentCulture)]),
                string.Join(" · ", details));
        });

        if (total > 0)
        {
            s.Caption = Bytes(total) + (type.Length > 0 ? " " + type : "")
                        + (speed > 0 ? $" {speed} MHz" : "");
            s.Rows.Insert(0, new SystemInfoRow(Locale.T("pc.row.raminstalled"), Bytes(total)));
            s.Rows.Insert(1, new SystemInfoRow(Locale.T("pc.row.rambanks"),
                banks.ToString(CultureInfo.CurrentCulture)));
        }
        return s;
    }
    private static SystemInfoSection StorageSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.storage"),
            IconKind = "disk",
            Accent = WpfBrushes.MediumSeaGreen,
        };
        var storage = ReadStorageDetails();
        var disks = 0;
        QueryAll("Win32_DiskDrive", mo =>
        {
            disks++;
            var model = moStr(mo, "Model");
            var size = Convert.ToInt64(mo["Size"] ?? 0, CultureInfo.InvariantCulture);
            var detail = storage.GetValueOrDefault(ToInt(mo["Index"]));
            var media = detail?.Media is { Length: > 0 } m ? m : moStr(mo, "InterfaceType");
            var health = detail?.Health is { Length: > 0 } h ? " · " + Locale.F("pc.disk.healthinline", [h]) : "";
            AddRow(s, model.Length > 0 ? model : Locale.F("pc.row.disk", [Text(disks)]),
                $"{Bytes(size)} · {media}{health}");
        });

        var volumes = 0;
        QueryAll("Win32_LogicalDisk WHERE DriveType = 3", mo =>
        {
            volumes++;
            var letter = moStr(mo, "DeviceID");
            var label = moStr(mo, "VolumeName");
            var free = Convert.ToInt64(mo["FreeSpace"] ?? 0, CultureInfo.InvariantCulture);
            var size = Convert.ToInt64(mo["Size"] ?? 0, CultureInfo.InvariantCulture);
            var percent = size > 0 ? 100d * (size - free) / size : 0;
            AddRow(s, label.Length > 0 ? $"{letter} {label}" : Locale.F("pc.row.volume", [letter]),
                Locale.F("pc.volume.usage",
                    [Bytes(size - free), Bytes(size), percent.ToString("0", CultureInfo.CurrentCulture),
                     moStr(mo, "FileSystem")]));
        });

        s.Caption = disks == 0
            ? ""
            : Locale.P(disks, "pc.storage.disk.one", "pc.storage.disk.many")
              + " · " + Locale.P(volumes, "pc.storage.volume.one", "pc.storage.volume.many");
        return s;
    }

    private static SystemInfoSection GpuSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.gpu"),
            IconKind = "gpu",
            Accent = WpfBrushes.YellowGreen,
        };
        var gpuIndex = 0;
        QueryAll("Win32_VideoController", mo =>
        {
            var name = moStr(mo, "Name");
            var exactVram = TryReadDedicatedVideoMemory(name);
            var wmiVram = Convert.ToInt64(mo["AdapterRAM"] ?? 0, CultureInfo.InvariantCulture);
            var vramText = exactVram is > 0
                ? Bytes(exactVram.Value)
                : wmiVram >= 4_000_000_000
                    ? Locale.T("pc.gpu.wmilimit")
                    : Bytes(wmiVram);

            gpuIndex++;
            if (gpuIndex == 1) s.Caption = name;
            AddRow(s, $"GPU {gpuIndex}", name);
            AddRow(s, Locale.T("pc.row.vram"), vramText);
            AddRow(s, Locale.T("pc.row.driver"), moStr(mo, "DriverVersion"));
            var horizontal = moInt(mo, "CurrentHorizontalResolution");
            var vertical = moInt(mo, "CurrentVerticalResolution");
            if (!string.IsNullOrWhiteSpace(horizontal) && !string.IsNullOrWhiteSpace(vertical))
                AddRow(s, Locale.T("pc.row.resolution"), $"{horizontal} × {vertical}");
        });
        return s;
    }

    /// <summary>
    /// AdapterRAM di Win32_VideoController è uint32 e satura intorno a 4 GB.
    /// I driver moderni espongono anche HardwareInformation.qwMemorySize (QWORD),
    /// che conserva la dimensione completa della memoria video dedicata.
    /// </summary>
    private static long? TryReadDedicatedVideoMemory(string adapterName)
    {
        const string videoKeyPath = @"SYSTEM\CurrentControlSet\Control\Video";
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(videoKeyPath);
            if (videoKey is null) return null;

            foreach (var adapterKeyName in videoKey.GetSubKeyNames())
            {
                using var adapterKey = videoKey.OpenSubKey(adapterKeyName);
                if (adapterKey is null) continue;

                foreach (var instanceName in adapterKey.GetSubKeyNames())
                {
                    using var instanceKey = adapterKey.OpenSubKey(instanceName);
                    if (instanceKey is null) continue;

                    var registryName = instanceKey.GetValue("HardwareInformation.AdapterString") as string
                                       ?? instanceKey.GetValue("DriverDesc") as string
                                       ?? string.Empty;
                    if (!NamesMatch(adapterName, registryName)) continue;

                    var value = ReadPositiveInt64(instanceKey.GetValue("HardwareInformation.qwMemorySize"));
                    if (value is > 0) return value;
                }
            }
        }
        catch (Exception)
        {
            // Il fallback WMI resta disponibile e viene etichettato come limitato.
        }
        return null;
    }

    private static bool NamesMatch(string left, string right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
               || right.Contains(left, StringComparison.OrdinalIgnoreCase));

    private static long? ReadPositiveInt64(object? value)
    {
        try
        {
            return value switch
            {
                long signed when signed > 0 => signed,
                ulong unsigned when unsigned is > 0 and <= long.MaxValue => (long)unsigned,
                byte[] bytes when bytes.Length >= sizeof(long) => BitConverter.ToInt64(bytes, 0),
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }

    private static SystemInfoSection MotherboardSection()
    {
        var s = new SystemInfoSection
        {
            Title = Locale.T("pc.sec.board"),
            IconKind = "motherboard",
            Accent = WpfBrushes.Goldenrod,
        };
        QueryFirst("Win32_BaseBoard", mo =>
        {
            s.Caption = JoinNonEmpty(moStr(mo, "Manufacturer"), moStr(mo, "Product"));
            AddRow(s, Locale.T("pc.row.vendor"), moStr(mo, "Manufacturer"));
            AddRow(s, Locale.T("pc.row.model"), moStr(mo, "Product"));
            AddRow(s, Locale.T("pc.row.revision"), moStr(mo, "Version"));
        });
        QueryFirst("Win32_BIOS", mo =>
        {
            AddRow(s, "BIOS", moStr(mo, "Manufacturer"));
            AddRow(s, Locale.T("pc.row.biosversion"), moStr(mo, "SMBIOSBIOSVersion"));
            AddRow(s, Locale.T("pc.row.biosdate"), FormatWmiDate(moStr(mo, "ReleaseDate")));
        });
        return s;
    }

    // --------------------------------------------------------------- helpers
    private static void AddRow(SystemInfoSection s, string key, string value)
        => s.Rows.Add(new SystemInfoRow(key, string.IsNullOrWhiteSpace(value) ? "—" : value));

    private static string moStr(ManagementBaseObject o, string key)
    {
        try { return o[key]?.ToString()?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static string moInt(ManagementBaseObject o, string key)
    {
        try { return o[key] is null ? "" : $"{Convert.ToInt64(o[key], CultureInfo.InvariantCulture):N0}"; }
        catch { return ""; }
    }

    private static string moBool(ManagementBaseObject o, string key)
    {
        try { return (o[key] is bool b && b) ? Locale.T("pc.yes") : (o[key] is null ? "" : Locale.T("pc.no")); }
        catch { return ""; }
    }

    /// <summary>Conteggio come testo per i segnaposto dei messaggi localizzati.</summary>
    private static string Text(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>MHz leggibili: sopra 1000 diventano GHz con due decimali.</summary>
    private static string Megahertz(int megahertz)
        => megahertz <= 0
            ? ""
            : megahertz >= 1000
                ? (megahertz / 1000d).ToString("0.00", CultureInfo.CurrentCulture) + " GHz"
                : megahertz.ToString(CultureInfo.CurrentCulture) + " MHz";

    /// <summary>Le cache WMI sono in KB: le portiamo a MB quando ha senso.</summary>
    private static string Kilobytes(int kilobytes)
        => kilobytes <= 0 ? "" : Bytes((long)kilobytes * 1024);

    private static string Bytes(long b)
    {
        const double K = 1024;
        if (b >= K * K * K) return $"{(b / (K * K * K)):N1} GB";
        if (b >= K * K) return $"{(b / (K * K)):N0} MB";
        return $"{b:N0} B";
    }

    private static void QueryFirst(string query, Action<ManagementBaseObject> apply)
    {
        try
        {
            using var mos = new ManagementObjectSearcher(ToWql(query));
            foreach (var mo in mos.Get().Cast<ManagementBaseObject>().Take(1))
                apply(mo);
        }
        catch { /* WMI non disponibile per questa classe: sezione parziale */ }
    }

    private static void QueryAll(string query, Action<ManagementBaseObject> apply)
    {
        try
        {
            using var mos = new ManagementObjectSearcher(ToWql(query));
            foreach (var mo in mos.Get().Cast<ManagementBaseObject>())
                apply(mo);
        }
        catch { /* tollerato */ }
    }

    private static string ToWql(string classNameOrQuery)
        => classNameOrQuery.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            ? classNameOrQuery
            : $"SELECT * FROM {classNameOrQuery}";
}

/// <summary>Riga hardware del pannello "IL MIO PC": titolo reale e dettaglio misurato.</summary>
public sealed record DashboardSystemFact(string IconKind, string Title, string Detail, WpfBrush Accent);

/// <summary>Riepilogo leggero usato dalla home: solo dati già letti localmente.</summary>
public sealed record DashboardSystemSummary(
    IReadOnlyList<DashboardSystemFact> Facts,
    string PrimaryDisk,
    IReadOnlyList<DashboardDiskSummary> Disks,
    double? GraphicsVramBytes);

public sealed record DashboardDiskSummary(
    string Name,
    string Model,
    string UsageText,
    string HealthText,
    string TypeText,
    double UsedPercent,
    WpfBrush StateBrush)
{
    public string UsedPercentText => $"{UsedPercent:0}%";

    /// <summary>Proporzioni della barra di riempimento (usato / libero).</summary>
    public GridLength UsedStar => new(Math.Max(UsedPercent, 0.001), GridUnitType.Star);
    public GridLength FreeStar => new(Math.Max(100 - UsedPercent, 0.001), GridUnitType.Star);
}
