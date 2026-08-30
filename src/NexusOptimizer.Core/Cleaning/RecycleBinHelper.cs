using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NexusOptimizer.Core.Cleaning;

/// <summary>Operazioni sul cestino tramite API Shell ufficiali.</summary>
public static class RecycleBinHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>Dimensione totale e numero elementi nel cestino. Ritorna null se non determinabile.</summary>
    public static (long Bytes, int Items)? Query()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            if (!SHQueryRecycleBin(string.Empty, ref info)) return null;
            return (info.i64Size, (int)info.i64NumItems);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Svuota il cestino senza ulteriore conferma grafica (la conferma è responsabilità della UI).</summary>
    /// <returns>true se svuotato; false se non disponibile o negato dal sistema.</returns>
    public static bool Empty()
    {
        try
        {
            var flags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND;
            var hr = SHEmptyRecycleBin(IntPtr.Zero, string.Empty, flags);
            return hr == 0 || hr == unchecked((int)0x80070091); // ERROR_ALREADY_EMPTY
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Sposta un singolo file o una directory nel cestino tramite Shell API.</summary>
    public static bool SendToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        IntPtr from = IntPtr.Zero;
        try
        {
            // SHFileOperation richiede una lista di percorsi terminata da due NUL.
            from = Marshal.StringToCoTaskMemUni(path + "\0");
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT),
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (from != IntPtr.Zero) Marshal.FreeCoTaskMem(from);
        }
    }
}
