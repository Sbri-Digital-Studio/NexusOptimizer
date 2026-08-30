using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NexusOptimizer.App.Services;

public sealed record ProcessSnapshot(
    int Pid,
    string Name,
    double? CpuPercent,
    long WorkingSetBytes,
    int ThreadCount);

public sealed record ProcessDetails(
    int Pid,
    string Name,
    string Path,
    string Publisher,
    string CommandLine,
    string StartedAt,
    string ParentPid,
    string Privilege,
    string Signature,
    string Sha256);

/// <summary>
/// Lettura processi esclusivamente informativa. Nessuna API di terminazione,
/// modifica priorità o affinity è esposta dal servizio.
/// </summary>
public sealed class ProcessService
{
    private readonly object _sampleLock = new();
    private Dictionary<int, CpuSample> _previous = [];

    public IReadOnlyList<ProcessSnapshot> Collect()
    {
        var now = DateTime.UtcNow;
        var next = new Dictionary<int, CpuSample>();
        var rows = new List<ProcessSnapshot>();
        Dictionary<int, CpuSample> previous;
        lock (_sampleLock) previous = new Dictionary<int, CpuSample>(_previous);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var pid = process.Id;
                    var totalCpu = process.TotalProcessorTime;
                    var sample = new CpuSample(now, totalCpu);
                    next[pid] = sample;

                    double? cpu = null;
                    if (previous.TryGetValue(pid, out var old))
                    {
                        var elapsedMs = (now - old.AtUtc).TotalMilliseconds;
                        var cpuMs = (totalCpu - old.TotalCpu).TotalMilliseconds;
                        if (elapsedMs > 0 && cpuMs >= 0)
                            cpu = Math.Clamp(cpuMs / (elapsedMs * Environment.ProcessorCount) * 100d, 0d, 100d);
                    }

                    rows.Add(new ProcessSnapshot(
                        pid,
                        string.IsNullOrWhiteSpace(process.ProcessName) ? $"PID {pid}" : process.ProcessName,
                        cpu,
                        Math.Max(0, process.WorkingSet64),
                        Math.Max(0, process.Threads.Count)));
                }
                catch (Exception)
                {
                    // Il processo può terminare o negare l'accesso durante la lettura.
                }
            }
        }

        lock (_sampleLock) _previous = next;
        return rows
            .OrderByDescending(row => row.CpuPercent ?? -1)
            .ThenByDescending(row => row.WorkingSetBytes)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ProcessDetails CollectDetails(int pid, bool verifyFile)
    {
        string name = $"PID {pid}";
        string path = "";
        string startedAt = "—";
        string privilege = "Non disponibile";

        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
            try { path = process.MainModule?.FileName ?? ""; } catch (Exception) { }
            try { startedAt = process.StartTime.ToString("g", CultureInfo.CurrentCulture); } catch (Exception) { }
            privilege = ReadElevation(process);
        }
        catch (Exception) { }

        var commandLine = "—";
        var parentPid = "—";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ExecutablePath, CommandLine, ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
            foreach (var item in searcher.Get().Cast<ManagementBaseObject>().Take(1))
            {
                path = string.IsNullOrWhiteSpace(path) ? item["ExecutablePath"]?.ToString() ?? "" : path;
                commandLine = item["CommandLine"]?.ToString() ?? "—";
                parentPid = item["ParentProcessId"]?.ToString() ?? "—";
            }
        }
        catch (Exception) { }

        var publisher = "—";
        if (File.Exists(path))
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                publisher = string.IsNullOrWhiteSpace(version.CompanyName) ? "—" : version.CompanyName;
            }
            catch (Exception) { }
        }

        var signature = verifyFile && File.Exists(path) ? VerifySignature(path) : "Non verificata";
        var hash = verifyFile && File.Exists(path) ? ComputeSha256(path) : "Calcola su richiesta";
        return new ProcessDetails(pid, name, Dash(path), publisher, commandLine, startedAt,
            parentPid, privilege, signature, hash);
    }

    private static string ReadElevation(Process process)
    {
        const uint TokenQuery = 0x0008;
        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out var token)) return "Non disponibile";
            try
            {
                var elevation = new TokenElevation();
                var size = Marshal.SizeOf<TokenElevation>();
                return GetTokenInformation(token, 20, ref elevation, size, out _)
                    ? elevation.TokenIsElevated != 0 ? "Elevato" : "Standard"
                    : "Non disponibile";
            }
            finally { CloseHandle(token); }
        }
        catch (Exception) { return "Non disponibile"; }
    }

    private static string VerifySignature(string path)
    {
        var filePath = Marshal.StringToCoTaskMemUni(path);
        var fileInfoPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePath,
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                UnionChoice = 1,
                FileInfoPointer = fileInfoPointer,
                ProviderFlags = 0x00001000,
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0
                ? "Valida"
                : "Assente o non valida";
        }
        catch (Exception) { return "Non verificabile"; }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePath);
        }
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception) { return "Non disponibile"; }
    }

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private sealed record CpuSample(DateTime AtUtc, TimeSpan TotalCpu);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation { public int TokenIsElevated; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        ref TokenElevation tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint WinVerifyTrust(IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData trustData);
}
