using System.Windows.Media;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Icona vettoriale del catalogo interno, disegnata su una griglia 24x24.
/// <paramref name="Stroked"/> distingue le icone a tratto (stile linea, la maggior
/// parte dell'interfaccia) da quelle a superficie piena: senza questa distinzione
/// una sagoma pensata come contorno verrebbe riempita e diventerebbe una macchia.
/// </summary>
public sealed record AppIcon(Geometry Geometry, bool Stroked, double Thickness = 1.9);

/// <summary>
/// Catalogo interno di geometrie vettoriali (scala 24x24). Disegnate a mano:
/// zero asset binari, zero dipendenze, tema-agnostico (tinta via brush).
/// Le icone di navigazione condividono lo stesso stile a tratto, spessore e
/// raccordi arrotondati, così la barra laterale resta coerente a ogni dimensione.
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, AppIcon> Cache = Build();

    public static AppIcon? Get(string kind)
        => Cache.GetValueOrDefault(kind ?? string.Empty);

    private static Dictionary<string, AppIcon> Build()
    {
        Dictionary<string, AppIcon> d = new(StringComparer.Ordinal);

        void Add(string key, string pathData, bool stroked = true, double thickness = 1.9)
        {
            try
            {
                var g = Geometry.Parse(pathData);
                g.Freeze();
                d[key] = new AppIcon(g, stroked, thickness);
            }
            catch { /* mai rompere la build per una forma */ }
        }

        void Fill(string key, string pathData) => Add(key, pathData, stroked: false);

        // ------------------------------------------------------------- marchio
        // nexus: monogramma N geometrico per il lock-up dell'app
        Fill("nexus", "M4 4 H8 L16 14 V4 H20 V20 H16 L8 10 V20 H4 Z");

        // ------------------------------------------------- navigazione (tratto)
        // home: casa con tetto e porta
        Add("home", "M3.4 11 L12 4 L20.6 11 M5.6 9.2 V19.6 H18.4 V9.2 M9.9 19.6 V14 H14.1 V19.6");

        // gamepad: modalità gaming (piena: a 19 px il tratto perderebbe leggibilità)
        Fill("gamepad", "F0 M7 7.5 H17 C19.9 7.5 22 10.2 22 13.2 C22 15.5 20.7 17.2 18.8 17.2 C17.7 17.2 17 16.6 16.4 15.9 L15.3 14.7 H8.7 L7.6 15.9 C7 16.6 6.3 17.2 5.2 17.2 C3.3 17.2 2 15.5 2 13.2 C2 10.2 4.1 7.5 7 7.5 Z M6 9.8 V11.2 H4.6 V12.6 H6 V14 H7.4 V12.6 H8.8 V11.2 H7.4 V9.8 Z M15.6 10 A1.15 1.15 0 1 0 15.61 10 Z M18.2 12.6 A1.15 1.15 0 1 0 18.21 12.6 Z");

        // info: cerchio informativo per "Il mio PC"
        Add("info", "M12 3.2 A8.8 8.8 0 1 0 12.01 3.2 M12 11 V16.6 M12 7.6 V7.9");

        // broom: pennello/scopa con setola (Smart Clean)
        Add("broom", "M9.4 11.8 L17.2 4 A2.9 2.9 0 0 1 21.2 8 L13.4 15.8 Z M7.2 15 C5.4 15 4 16.4 4 18.2 C4 19.5 2.4 19.8 2.4 20.4 C3.6 21.4 5 22 6.6 22 C8.9 22 10.6 20.2 10.6 18 C10.6 16.3 9.1 15 7.2 15 Z");

        // gear: ingranaggio a tratto per Optimizer e Impostazioni
        Add("gear", "M12 9.2 A2.8 2.8 0 1 0 12.01 9.2 M19.4 12 C19.4 12.6 19.3 13.1 19.2 13.6 L21 15 L19.5 17.6 L17.3 16.8 C16.6 17.4 15.7 17.9 14.8 18.2 L14.4 20.5 H11.6 L11.2 18.2 C10.3 17.9 9.4 17.4 8.7 16.8 L6.5 17.6 L5 15 L6.8 13.6 C6.7 13.1 6.6 12.6 6.6 12 C6.6 11.4 6.7 10.9 6.8 10.4 L5 9 L6.5 6.4 L8.7 7.2 C9.4 6.6 10.3 6.1 11.2 5.8 L11.6 3.5 H14.4 L14.8 5.8 C15.7 6.1 16.6 6.6 17.3 7.2 L19.5 6.4 L21 9 L19.2 10.4 C19.3 10.9 19.4 11.4 19.4 12 Z");

        // chip: processore stilizzato (RAM Manager, scheda madre)
        Add("chip", "M8.6 8.6 H15.4 V15.4 H8.6 Z M5.4 5.4 H18.6 V18.6 H5.4 Z M9.2 5.4 V2.6 M14.8 5.4 V2.6 M9.2 21.4 V18.6 M14.8 21.4 V18.6 M5.4 9.2 H2.6 M5.4 14.8 H2.6 M21.4 9.2 H18.6 M21.4 14.8 H18.6");

        // disk: cilindro storage
        Add("disk", "M12 3.4 C7.6 3.4 4.6 4.8 4.6 6.4 V17.6 C4.6 19.2 7.6 20.6 12 20.6 C16.4 20.6 19.4 19.2 19.4 17.6 V6.4 C19.4 4.8 16.4 3.4 12 3.4 Z M4.6 6.4 C4.6 8 7.6 9.4 12 9.4 C16.4 9.4 19.4 8 19.4 6.4 M4.6 12 C4.6 13.6 7.6 15 12 15 C16.4 15 19.4 13.6 19.4 12");

        // rocket: avvio applicazioni
        Add("rocket", "M12 2.6 C15.4 5.4 16.8 9.4 16.4 13.2 L13.4 15.4 L12 21 L10.6 15.4 L7.6 13.2 C7.2 9.4 8.6 5.4 12 2.6 Z M12 8.4 A1.6 1.6 0 1 0 12.01 8.4 M6.4 15.6 C4.8 17.2 4.4 19.6 4.4 20.8 C5.6 20.8 8 20.4 9.6 18.8");

        // shield: privacy con spunta
        Add("shield", "M12 2.8 L4.6 5.6 V11.8 C4.6 16.4 7.8 19.9 12 21.2 C16.2 19.9 19.4 16.4 19.4 11.8 V5.6 Z M8.9 11.9 L11.2 14.2 L15.4 9.8");

        // apps: griglia applicazioni
        Add("apps", "M4.2 4.2 H10 V10 H4.2 Z M14 4.2 H19.8 V10 H14 Z M4.2 14 H10 V19.8 H4.2 Z M14 14 H19.8 V19.8 H14 Z");

        // chart: monitoraggio in tempo reale
        Add("chart", "M3.6 20 H20.6 M6.4 20 V13.4 M11 20 V8.6 M15.6 20 V15.4 M20.2 20 V5.6");

        // history: ripristino e cronologia
        Add("history", "M4.4 12 A7.6 7.6 0 1 0 6.9 6.3 L4.2 8.8 M4 4.6 V9.2 H8.6 M12 7.8 V12.2 L15.2 14");

        // restoreCenter: scudo e freccia di ritorno, icona premium del centro modifiche
        Add("restoreCenter", "M12 2.8 L4.8 5.5 V11.6 C4.8 16.2 7.8 19.5 12 20.9 C15.3 19.8 17.9 17.5 18.8 14.4 M8.3 13 A4.8 4.8 0 1 0 9.5 8.1 M8.2 5.8 V8.6 H11");

        // undoShield: ripristino esatto da snapshot locale
        Add("undoShield", "M12 2.8 L4.8 5.5 V11.7 C4.8 16.2 7.9 19.6 12 20.9 C16.1 19.6 19.2 16.2 19.2 11.7 V5.5 Z M8.3 12.4 A3.9 3.9 0 1 0 9.7 9.4 M8 7.1 V9.8 H10.7");

        // undo: freccia circolare usata nelle azioni di annullamento
        Add("undo", "M5 8.7 H9.4 V4.3 M5.3 8.4 A7.7 7.7 0 1 1 4.5 14.5");

        // windowsRestore: logo Windows con arco di ripristino
        Add("windowsRestore", "M4 5.2 L10.6 4.3 V10.7 H4 Z M13.4 3.9 L20 3 V10.7 H13.4 Z M4 13.3 H10.6 V19.7 L4 18.8 Z M13.4 13.3 H20 V21 L13.4 20.1 Z M7.2 22 A5.2 5.2 0 0 1 3 16.9 M3 16.9 V20 M3 16.9 H6.1");

        // scan: lente con piccoli indicatori di analisi
        Add("scan", "M10.4 4 A6.4 6.4 0 1 0 10.41 4 M15 15 L20.4 20.4 M10.4 7.2 V9.1 M10.4 11.8 V13.7 M7.2 10.4 H9.1 M11.8 10.4 H13.7");

        // warning: triangolo informativo per stati variati fuori da Nexus
        Add("warning", "M12 3.2 L21 20.2 H3 Z M12 8.5 V13.8 M12 17.1 V17.3");

        // lock: deposito cifrato locale
        Add("lock", "M6 10 H18 V20 H6 Z M8.4 10 V7.4 A3.6 3.6 0 0 1 15.6 7.4 V10 M12 14 V16.8");

        // pulse: diagnostica
        Add("pulse", "M2.6 12 H7 L9.2 6.6 L13.2 17.6 L15.2 12 H21.4");

        // globe: rete e browser
        Add("globe", "M12 3 A9 9 0 1 0 12.01 3 M3.2 9.2 H20.8 M3.2 14.8 H20.8 M12 3 C8.8 6.4 8.8 17.6 12 21 C15.2 17.6 15.2 6.4 12 3 Z");

        // ---------------------------------------------------- utility e palette
        Add("search", "M10.8 3.6 A7.2 7.2 0 1 0 10.81 3.6 M16.2 16.2 L20.8 20.8");
        Add("close", "M5.6 5.6 L18.4 18.4 M18.4 5.6 L5.6 18.4");
        Add("check", "M4.6 12.6 L9.8 17.8 L19.4 6.6");
        Add("chevronRight", "M9.4 5.4 L16 12 L9.4 18.6");
        Add("folderPlus", "M3.4 6 H9.2 L11.2 8.2 H20.6 V19 H3.4 Z M14 12.4 V16.6 M11.9 14.5 H16.1");
        Add("trash", "M4.4 7 H19.6 M9.4 7 V4.4 H14.6 V7 M6.8 7 L7.8 19.8 H16.2 L17.2 7 M10.2 10.6 V16.2 M13.8 10.6 V16.2");
        Add("copy", "M8.4 8.4 H19.6 V19.6 H8.4 Z M5.4 15.6 H4.4 V4.4 H15.6 V5.4");
        Add("shredder", "M6 3.4 H14.6 L18.4 7.2 V12.4 H6 Z M14.6 3.4 V7.2 H18.4 M3.6 15 H20.4 M7 17.8 L8.6 21 M11 17.8 L12.6 21 M15 17.8 L16.6 21");
        Add("wrench", "M14.6 4.2 A5 5 0 0 0 9.6 9.2 L4.4 14.4 A2.6 2.6 0 1 0 8 18 L13.2 12.8 A5 5 0 0 0 18.2 7.8 L15.2 9.8 L12.6 7.2 Z");
        Add("toolbox", "M4.2 8.4 H19.8 V19.6 H4.2 Z M8.4 8.4 V5.4 H15.6 V8.4 M4.2 12.6 H19.8 M10.2 12.6 V15.2 H13.8 V12.6");

        // ------------------------------------------------- hardware e riepiloghi
        Add("monitor", "M3.4 4.6 H20.6 V15.8 H3.4 Z M8.6 19.6 H15.4 M12 15.8 V19.6");
        Add("cpuMini", "M8.6 8.6 H15.4 V15.4 H8.6 Z M5.4 5.4 H18.6 V18.6 H5.4 Z M9.2 5.4 V3 M14.8 5.4 V3 M9.2 21 V18.6 M14.8 21 V18.6 M5.4 9.2 H3 M5.4 14.8 H3 M21 9.2 H18.6 M21 14.8 H18.6");
        Add("memory", "M3.4 7.4 H20.6 V16.6 H3.4 Z M7 7.4 V4.6 M11 7.4 V4.6 M15 7.4 V4.6 M19 7.4 V4.6 M7 16.6 V19.4 M11 16.6 V19.4 M15 16.6 V19.4 M19 16.6 V19.4 M7.4 11 H16.6 V13.6 H7.4 Z");
        Add("gpu", "M2.6 6.6 H21.4 V16.4 H2.6 Z M2.6 16.4 V20.4 M6.2 16.4 V19 M8.8 11.5 A3.3 3.3 0 1 0 8.81 11.5 M15 9.6 H19 M15 12.4 H19 M15 15 H17.6");
        // motherboard: piastra con socket, slot RAM e piste
        Add("motherboard", "M3.4 3.4 H20.6 V20.6 H3.4 Z M7.2 7.2 H12.4 V12.4 H7.2 Z M15.4 6.6 V13.4 M17.9 6.6 V13.4 M7.2 15.8 H12.8 M7.2 18.2 H10.4");
        Add("clock", "M12 3 A9 9 0 1 0 12.01 3 M12 6.8 V12.2 L15.6 14.4");
        Add("leaf", "M20.2 4 C11.4 4 5.4 7 5.4 13.6 C5.4 17.4 8.2 19.6 12 19.6 C17.8 19.6 20.2 13.8 20.2 4 Z M4.2 20.6 C8 15.8 12 12 17.6 8.4");
        Add("tower", "M6.4 3.4 H17.6 V20.6 H6.4 Z M9.4 6.8 H14.6 M9.4 10 H14.6 M9.4 16.8 A1.1 1.1 0 1 0 9.41 16.8");

        // ------------------------------------------------ categorie e stato app
        Fill("bolt", "M13.4 2 L4 13.6 H10 L9.2 22 L20 9.8 H13.2 Z");
        Fill("chat", "F0 M3 4 H21 V16.5 H12.8 L7.6 20.6 V16.5 H3 Z M6.8 8.8 A1.15 1.15 0 1 0 6.81 8.8 Z M11.9 8.8 A1.15 1.15 0 1 0 11.91 8.8 Z M17 8.8 A1.15 1.15 0 1 0 17.01 8.8 Z");
        Fill("music", "M9 3.5 L19 1.8 V4.6 L11 6.1 V16 C11 17.9 9.4 19.4 7.4 19.4 C5.5 19.4 4 18.2 4 16.6 C4 15 5.5 13.8 7.4 13.8 C8 13.8 8.5 13.9 9 14.1 Z");
        Fill("cloud", "M6.5 19 C4 19 2 17 2 14.5 C2 12.3 3.6 10.5 5.7 10.1 C6.3 7.2 8.9 5 12 5 C15.3 5 18 7.5 18.4 10.7 C20.4 11.1 22 12.9 22 15 C22 17.2 20.2 19 18 19 Z");
        Fill("keyboard", "F0 M2 6 H22 V18 H2 Z M4 8.2 H6 V10.2 H4 Z M7.5 8.2 H9.5 V10.2 H7.5 Z M11 8.2 H13 V10.2 H11 Z M14.5 8.2 H16.5 V10.2 H14.5 Z M18 8.2 H20 V10.2 H18 Z M4 11.6 H6 V13.6 H4 Z M7.5 11.6 H9.5 V13.6 H7.5 Z M11 11.6 H13 V13.6 H11 Z M14.5 11.6 H16.5 V13.6 H14.5 Z M18 11.6 H20 V13.6 H18 Z M7 15 H17 V16.6 H7 Z");
        Fill("bell", "F0 M12 2.5 C8.7 2.5 6 5.2 6 8.5 V13 L4 16.2 V17.6 H20 V16.2 L18 13 V8.5 C18 5.2 15.3 2.5 12 2.5 Z M9.8 19 H14.2 C14.2 20.2 13.2 21.2 12 21.2 C10.8 21.2 9.8 20.2 9.8 19 Z");
        Fill("moon", "M21 14.6 A9.6 9.6 0 1 1 10.4 3 A7.6 7.6 0 0 0 21 14.6 Z");

        // ------------------------------------------------------- pagina Tools
        // Icone a superficie piena: restano nitide anche a 32 px nelle card grandi.
        Fill("taskManager", "F0 M3 4 H21 V20 H3 Z M5 6 V18 H19 V6 Z M7 8 H10 V11 H7 Z M12 8 H17 V9.5 H12 Z M7 13 H10 V16 H7 Z M12 12.5 H17 V14 H12 Z M12 15.5 H16 V17 H12 Z");
        Fill("resourceMonitor", "F0 M12 2 A10 10 0 1 0 12 22 A10 10 0 1 0 12 2 Z M12 5 A7 7 0 1 1 12 19 A7 7 0 1 1 12 5 Z M5 13 H8 L10 8 L13 16 L15 11 H19 V13 H16 L13 20 L10 13 L9 16 H5 Z");
        Fill("reliability", "F0 M3 19 H21 V21 H3 Z M4 16 L8 12 L11 14 L16 7 L18 9 L11 18 L8 16 L6 18 Z M15 4 L18 4 L18 7 Z");
        Fill("systemInfo", "F0 M3 3 H21 V17 H3 Z M5 5 V15 H19 V5 Z M9 20 H15 V22 H9 Z M11 8 H13 V10 H11 Z M11 11 H13 V14 H11 Z");
        Fill("installedApps", "F0 M4 7 H20 V21 H4 Z M7 10 V18 H17 V10 Z M8 3 H16 L18 7 H6 Z M9 12 H12 V15 H9 Z M13 12 H16 V15 H13 Z");
        Fill("storageSettings", "M12 3 C7 3 4 4.6 4 6.5 V17.5 C4 19.4 7 21 12 21 C17 21 20 19.4 20 17.5 V6.5 C20 4.6 17 3 12 3 Z M4 6.5 C4 8.4 7 10 12 10 C17 10 20 8.4 20 6.5 V9 C18.7 10.2 16.2 11 12 11 C7.8 11 5.3 10.2 4 9 Z M12 14 A3 3 0 1 0 12.01 14 Z M11 12 H13 V16 H11 Z M9 13 H15 V15 H9 Z");
        Fill("systemRestore", "F0 M12 2 L4 5 V12 C4 17 7.5 20.7 12 22 C16.5 20.7 20 17 20 12 V5 Z M12 6 A6 6 0 1 0 12 18 A6 6 0 1 0 12 6 Z M12 8 V12 L15 14 L14 15.5 L10 13 V8 Z M7 7 H11 V9 H8.5 V11 H6 V8 Z");
        Fill("diskCleanup", "M5 3 H8 L13 13 L10 15 Z M7 2 C8.2 1.5 9.5 2.2 10 3.4 L14 12 L12 13 L8 5 Z M9 15 L13 12 L20 18 C16 21 10 22 4 20 C6.5 19 8 17.5 9 15 Z M13 15 L16 19 L18 18 L15 14 Z");

        return d;
    }
}
