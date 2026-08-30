namespace NexusOptimizer.App.Services;

/// <summary>Categoria funzionale usata per raggruppare le app in background.</summary>
public enum BackgroundAppCategory
{
    Sincronizzazione,
    Comunicazione,
    Musica,
    Browser,
    Periferiche,
    Creativita,
    Launcher,
    Aggiornamenti,
    Altro,
}

/// <summary>
/// Voce catalogata: descrive un processo utente noto per restare in background.
/// <paramref name="DefaultFromLevel"/> è il livello minimo in cui la voce risulta
/// pre-selezionata; sotto quel livello viene comunque mostrata ma non spuntata.
/// </summary>
public sealed record BackgroundAppEntry(
    string ProcessName,
    string DisplayName,
    BackgroundAppCategory Category,
    NexusOptimizer.Core.Configuration.AppModeLevel DefaultFromLevel,
    string Note);

/// <summary>
/// Catalogo curato di applicazioni in background note. Nessuna euristica cieca:
/// una voce entra qui solo se la chiusura è (a) non distruttiva, (b) riavviabile
/// dall'utente e (c) priva di effetti su servizi di sistema o sicurezza.
/// I processi di sistema, driver e sicurezza vivono in <see cref="ProtectedProcesses"/>
/// e non vengono mai toccati, nemmeno in modalità EXPERT.
/// </summary>
public static class BackgroundAppCatalog
{
    private static readonly Core.Configuration.AppModeLevel Safe = Core.Configuration.AppModeLevel.Safe;
    private static readonly Core.Configuration.AppModeLevel Balanced = Core.Configuration.AppModeLevel.Balanced;
    private static readonly Core.Configuration.AppModeLevel Expert = Core.Configuration.AppModeLevel.Expert;

    private static readonly BackgroundAppEntry[] Entries =
    [
        // --- Sincronizzazione cloud: riprendono da soli al riavvio, zero perdita dati ---
        new("onedrive", "Microsoft OneDrive", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("dropbox", "Dropbox", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("googledrivefs", "Google Drive", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("megasync", "MEGAsync", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("pcloud", "pCloud", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("nextcloud", "Nextcloud", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("icloudservices", "iCloud", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),

        // --- Periferiche e RGB: occupano RAM e polling costante, nessun effetto sul driver ---
        new("icue", "Corsair iCUE", BackgroundAppCategory.Periferiche, Safe, "I profili restano salvati nelle periferiche."),
        new("razer synapse 3", "Razer Synapse", BackgroundAppCategory.Periferiche, Safe, "I profili restano salvati nelle periferiche."),
        new("razer central", "Razer Central", BackgroundAppCategory.Periferiche, Safe, "Servizio di contorno di Synapse."),
        new("lghub", "Logitech G HUB", BackgroundAppCategory.Periferiche, Safe, "I profili restano salvati nelle periferiche."),
        new("lghub_agent", "Logitech G HUB Agent", BackgroundAppCategory.Periferiche, Safe, "Agente di supporto di G HUB."),
        new("logioptionsplus_agent", "Logi Options+", BackgroundAppCategory.Periferiche, Safe, "I profili restano salvati nelle periferiche."),
        new("steelseriesgg", "SteelSeries GG", BackgroundAppCategory.Periferiche, Safe, "I profili restano salvati nelle periferiche."),
        new("armourycrate.userSessionHelper", "Armoury Crate Helper", BackgroundAppCategory.Periferiche, Safe, "Helper della suite ASUS."),
        new("openrgb", "OpenRGB", BackgroundAppCategory.Periferiche, Safe, "Illuminazione: nessun impatto sul sistema."),
        new("nzxt cam", "NZXT CAM", BackgroundAppCategory.Periferiche, Safe, "Monitoraggio di contorno."),

        // --- Aggiornatori e helper: rientrano da soli alla successiva pianificazione ---
        new("googleupdate", "Google Update", BackgroundAppCategory.Aggiornamenti, Safe, "Riparte alla successiva pianificazione."),
        new("adobearm", "Adobe Updater", BackgroundAppCategory.Aggiornamenti, Safe, "Riparte alla successiva pianificazione."),
        new("ccxprocess", "Adobe CCX Process", BackgroundAppCategory.Aggiornamenti, Safe, "Componente accessorio di Creative Cloud."),
        new("adobeipcbroker", "Adobe IPC Broker", BackgroundAppCategory.Aggiornamenti, Safe, "Componente accessorio di Creative Cloud."),
        new("adobenotificationclient", "Adobe Notification", BackgroundAppCategory.Aggiornamenti, Safe, "Solo notifiche Adobe."),
        new("creative cloud", "Adobe Creative Cloud", BackgroundAppCategory.Creativita, Safe, "Nessun progetto aperto viene toccato."),
        new("coresync", "Adobe Core Sync", BackgroundAppCategory.Sincronizzazione, Safe, "La sincronizzazione riprende alla riapertura."),
        new("jusched", "Java Update Scheduler", BackgroundAppCategory.Aggiornamenti, Safe, "Riparte alla successiva pianificazione."),
        new("nvidia app", "NVIDIA App", BackgroundAppCategory.Aggiornamenti, Safe, "Overlay e aggiornamenti: il driver non viene toccato."),
        new("nvidia share", "NVIDIA Overlay", BackgroundAppCategory.Aggiornamenti, Safe, "Overlay ShadowPlay: il driver non viene toccato."),
        new("nvidia web helper", "NVIDIA Web Helper", BackgroundAppCategory.Aggiornamenti, Safe, "Helper GeForce Experience."),
        new("radeonsoftware", "AMD Radeon Software", BackgroundAppCategory.Aggiornamenti, Safe, "Interfaccia utente: il driver non viene toccato."),

        // --- Musica e streaming ---
        new("spotify", "Spotify", BackgroundAppCategory.Musica, Balanced, "La riproduzione viene interrotta."),
        new("itunes", "iTunes", BackgroundAppCategory.Musica, Balanced, "La riproduzione viene interrotta."),
        new("applemusic", "Apple Music", BackgroundAppCategory.Musica, Balanced, "La riproduzione viene interrotta."),
        new("deezer", "Deezer", BackgroundAppCategory.Musica, Balanced, "La riproduzione viene interrotta."),
        new("tidal", "TIDAL", BackgroundAppCategory.Musica, Balanced, "La riproduzione viene interrotta."),

        // --- Comunicazione: chiusura pulita, le conversazioni restano sul server ---
        new("slack", "Slack", BackgroundAppCategory.Comunicazione, Balanced, "I messaggi restano sul server."),
        new("ms-teams", "Microsoft Teams", BackgroundAppCategory.Comunicazione, Balanced, "Le chiamate attive verrebbero interrotte."),
        new("teams", "Microsoft Teams", BackgroundAppCategory.Comunicazione, Balanced, "Le chiamate attive verrebbero interrotte."),
        new("skype", "Skype", BackgroundAppCategory.Comunicazione, Balanced, "Le chiamate attive verrebbero interrotte."),
        new("telegram", "Telegram", BackgroundAppCategory.Comunicazione, Balanced, "I messaggi restano sul server."),
        new("whatsapp", "WhatsApp", BackgroundAppCategory.Comunicazione, Balanced, "I messaggi restano sul server."),
        new("discord", "Discord", BackgroundAppCategory.Comunicazione, Expert, "Molti giochi usano Discord in vocale: chiudilo solo se non ti serve."),

        // --- Browser: il recupero sessione ripristina le schede alla riapertura ---
        new("chrome", "Google Chrome", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),
        new("msedge", "Microsoft Edge", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),
        new("firefox", "Mozilla Firefox", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),
        new("opera", "Opera", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),
        new("brave", "Brave", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),
        new("vivaldi", "Vivaldi", BackgroundAppCategory.Browser, Balanced, "Chiusura ordinata: le schede vengono ripristinate alla riapertura."),

        // --- Launcher: servono per avviare i giochi, quindi mai pre-selezionati ---
        new("epicgameslauncher", "Epic Games Launcher", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi Epic."),
        new("epicwebhelper", "Epic Web Helper", BackgroundAppCategory.Launcher, Balanced, "Helper del launcher Epic."),
        new("battle.net", "Battle.net", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi Blizzard."),
        new("eadesktop", "EA App", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi EA."),
        new("upc", "Ubisoft Connect", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi Ubisoft."),
        new("galaxyclient", "GOG Galaxy", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi GOG."),
        new("riotclientservices", "Riot Client", BackgroundAppCategory.Launcher, Expert, "Serve per avviare i giochi Riot."),
    ];

    private static readonly Dictionary<string, BackgroundAppEntry> Index =
        Entries.GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static BackgroundAppEntry? Find(string processName)
        => Index.GetValueOrDefault(processName ?? string.Empty);

    public static string Describe(BackgroundAppCategory category) => Locale.T(category switch
    {
        BackgroundAppCategory.Sincronizzazione => "gam.cat.sync",
        BackgroundAppCategory.Comunicazione => "gam.cat.chat",
        BackgroundAppCategory.Musica => "gam.cat.music",
        BackgroundAppCategory.Browser => "gam.cat.browser",
        BackgroundAppCategory.Periferiche => "gam.cat.devices",
        BackgroundAppCategory.Creativita => "gam.cat.creative",
        BackgroundAppCategory.Launcher => "gam.cat.launcher",
        BackgroundAppCategory.Aggiornamenti => "gam.cat.updates",
        _ => "gam.cat.other",
    });
}

/// <summary>
/// Perimetro intoccabile. Vale per chiusura, priorità e compattazione memoria:
/// se un nome compare qui il processo viene ignorato in ogni modalità.
/// </summary>
public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel, sessione e shell
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "fontdrvhost", "dwm", "sihost", "taskhostw", "ctfmon",
        "explorer", "runtimebroker", "searchhost", "searchindexer", "searchapp",
        "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "applicationframehost", "dllhost", "conhost", "audiodg", "wudfhost",
        "spoolsv", "wmiprvse", "backgroundtaskhost", "systemsettings",
        "lockapp", "useroobebroker", "widgets", "widgetservice", "phoneexperiencehost",
        // Driver grafici e piattaforma
        "nvcontainer", "nvdisplay.container", "nvsphelper64", "igfxem", "igfxext",
        "amdow", "atieclxx", "atiesrxx", "rtkauduservice64", "realtekaudservice64",
        // Sicurezza (mai toccata: potrebbe essere richiesta dalle policy aziendali)
        "msmpeng", "nissrv", "mpdefendercoreservice", "securityhealthservice",
        "securityhealthsystray", "smartscreen", "sgrmbroker", "mssense", "sensecncproxy",
        "avp", "avpui", "avastui", "avastsvc", "avgui", "avgsvc", "bdagent", "vsserv",
        "mcshield", "masvc", "ekrn", "egui", "nortonsecurity", "nsservice", "ns",
        "mbamservice", "mbam", "sophosui", "savservice", "cylancesvc", "csfalconservice",
        // Piattaforma di gioco che non va interrotta durante una partita
        "steam", "steamwebhelper", "steamservice", "gameoverlayui", "easyanticheat",
        "battleye", "beservice", "vgtray", "vgc",
        // Nexus stesso
        "nexusoptimizer",
    };

    public static bool IsProtected(string processName)
        => Names.Contains(processName ?? string.Empty);
}
