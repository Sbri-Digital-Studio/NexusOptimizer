# Modalità Gaming

Sezione dedicata a preparare il PC prima di giocare. Non è un interruttore
decorativo: ogni voce corrisponde a un'azione reale sul sistema, misurata e
annullabile. Vale la stessa regola del resto del programma — se un dato non è
certo viene mostrato come `n.d.`, mai stimato.

## Principio operativo

1. **Nessuna azione parte da sola.** Il boost si applica solo quando l'utente
   preme *ATTIVA MODALITÀ GAMING*, e solo sulle voci selezionate.
2. **Tutto ciò che viene cambiato viene prima salvato.** Piano energetico,
   priorità dei processi e preferenze utente vengono letti e memorizzati prima
   della modifica, quindi ripristinati alla disattivazione, alla chiusura
   dell'app e anche in caso di errore.
3. **Perimetro intoccabile.** Processi di sistema, driver grafici, software di
   sicurezza, anti-cheat (EasyAntiCheat, BattlEye, Vanguard) e Steam non vengono
   mai chiusi né alterati, in nessuna modalità. L'elenco è in
   `ProtectedProcesses` (`src/NexusOptimizer.App/Services/BackgroundAppCatalog.cs`).
4. **Nessuna scrittura in HKEY_LOCAL_MACHINE**, nessun servizio arrestato senza
   privilegi già disponibili, nessun dato in uscita dal PC.

## Cosa fa davvero

| Azione | Come è implementata | Ripristino |
|---|---|---|
| Chiusura app in background | `CloseMainWindow()` sulle app selezionate dall'utente; chiusura forzata solo in EXPERT e solo se l'app non risponde entro 4 s | Pulsante *RIAPRI LE APP CHIUSE*: il percorso dell'eseguibile viene registrato prima della chiusura |
| Piano energetico | `powercfg /setactive` verso un piano **già presente** in Windows (Prestazioni elevate o Prestazioni max) | Il GUID del piano precedente viene salvato e riattivato all'uscita |
| Registrazione in background | `HKCU\System\GameConfigStore\GameDVR_Enabled = 0` | Valore precedente riscritto; se la chiave non esisteva viene rimossa |
| Modalità gioco di Windows | `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = 1` | Come sopra |
| Priorità al gioco | Priorità `High` al **solo** processo in primo piano al momento dell'attivazione; Nexus scende a `BelowNormal` | Priorità originale ripristinata |
| Compattazione memoria | `EmptyWorkingSet` una tantum sulle app utente residue non protette | Nessuno necessario: Windows ricarica le pagine su richiesta |
| Servizi ad alto I/O | `SysMain` e `WSearch` fermati **solo se Nexus è già in esecuzione come amministratore** | Riavviati alla disattivazione |

Il risultato non è dichiarato a parole: la RAM liberata è la **differenza reale**
di memoria fisica disponibile (`GlobalMemoryStatusEx`) prima e dopo il boost, e
il resoconto elenca per ogni azione se è stata applicata o no, con il motivo.

## Livelli modalità

Il livello scelto nella sidebar (o dalla barra del titolo) non cambia l'aspetto:
decide cosa la Modalità Gaming può proporre — e, dalla stessa impostazione,
anche cosa l'Optimizer può applicare (vedi [`LIVELLI-MODALITA.md`](LIVELLI-MODALITA.md)).

| Livello | App pre-selezionate | Chiusura forzata |
|---|---|---|
| **SAFE** | solo app che si riaprono da sole e non perdono nulla (sync cloud, suite RGB, updater) | non disponibile |
| **BALANCED** | aggiunge browser, musica e client di messaggistica | non disponibile |
| **EXPERT** | aggiunge launcher di gioco, Discord e le app non catalogate sopra 60 MB con finestra | disponibile, opzionale |

Le app non catalogate compaiono solo in EXPERT e non sono mai pre-selezionate.

## Come si apre

- Voce **Modalità Gaming** nella barra laterale;
- pulsante **Gaming** nella barra del titolo;
- riquadro *Strumenti rapidi* della Dashboard;
- da riga di comando: `NexusOptimizer.exe --page:nav.gaming` (utile per una
  scorciatoia dedicata sul desktop).

## Cosa non fa

- Non applica priorità o affinity di massa ai processi: agisce sul solo gioco in
  primo piano, su richiesta esplicita.
- Non crea piani energetici personalizzati e non tocca le soglie di risparmio
  energetico: si limita a selezionare un piano che Windows già espone.
- Non disattiva Defender, UAC, aggiornamenti o servizi di sicurezza.
- Non promette FPS: mostra ciò che ha realmente liberato e cosa ha cambiato.
