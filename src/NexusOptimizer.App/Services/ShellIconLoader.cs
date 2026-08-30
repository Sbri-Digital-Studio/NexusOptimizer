using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Estrae l'icona reale di un programma dal file che la contiene, come fa la
/// shell di Windows. La sorgente è il valore <c>DisplayIcon</c> dichiarato dal
/// programma stesso nel Registro: nessun logo scaricato da internet, nessun
/// catalogo di immagini nostro.
///
/// Le immagini vengono congelate (<c>Freeze</c>) perché l'estrazione avviene su
/// un thread di lavoro e la lista le mostra sul thread dell'interfaccia.
/// </summary>
public static class ShellIconLoader
{
    private const int IconSize = 32;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, int count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Carica l'icona descritta da un valore <c>DisplayIcon</c> ("percorso" oppure
    /// "percorso,indice"). Restituisce null quando il file non esiste o non
    /// contiene icone: in quel caso la lista mostra un segnaposto neutro, non un
    /// logo scelto a caso.
    /// </summary>
    public static BitmapSource? Load(string? specification)
    {
        if (string.IsNullOrWhiteSpace(specification)) return null;
        var (path, index) = Split(specification);
        if (path.Length == 0) return null;

        try
        {
            if (!File.Exists(path)) return null;
            return Extract(path, index);
        }
        catch (Exception)
        {
            // Un file corrotto o un formato non gestito non deve fermare l'elenco.
            return null;
        }
    }

    private static BitmapSource? Extract(string path, int index)
    {
        var large = new IntPtr[1];
        var extracted = ExtractIconEx(path, index, large, null, 1);
        if (extracted <= 0 || large[0] == IntPtr.Zero)
        {
            // Indice non valido: si riprova con la prima icona del file.
            if (index == 0) return null;
            extracted = ExtractIconEx(path, 0, large, null, 1);
            if (extracted <= 0 || large[0] == IntPtr.Zero) return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                large[0],
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(IconSize, IconSize));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(large[0]);
        }
    }

    /// <summary>Separa "percorso,indice" tenendo conto delle virgolette.</summary>
    internal static (string Path, int Index) Split(string specification)
    {
        var text = specification.Trim();
        if (text.Length == 0) return ("", 0);

        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 0)
            {
                var quoted = text[1..end];
                var rest = text[(end + 1)..].TrimStart(',', ' ');
                return (quoted, ParseIndex(rest));
            }
        }

        var comma = text.LastIndexOf(',');
        if (comma > 0 && comma < text.Length - 1
            && int.TryParse(text[(comma + 1)..].Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return (text[..comma].Trim(), parsed);

        // "percorso," senza numero: l'indice implicito è zero.
        return (text.TrimEnd(',').Trim(), 0);
    }

    private static int ParseIndex(string text)
        => int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var index) ? index : 0;
}
