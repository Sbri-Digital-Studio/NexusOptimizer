# Sicurezza — Nexus Optimizer

La Fase 6 mantiene un gate xUnit dedicato `DeletionSafety`: deve restare verde almeno al 95%.
La baseline corrente è 19/19 test superati e include percorsi protetti, reparse point,
confinamento delle categorie, quarantena manomessa e ripristino senza sovrascrittura.

Minacce primarie e difese. Ogni difesa e' verificabile nei test (Fase 6).

## 1. Cancellazione fuori perimetro (path traversal / junction escape)
- Canonizzazione SEMPRE prima dell'uso (`Path.GetFullPath`); accettiamo solo percorsi dentro le
  **root autorizzate dalla categoria** (`PathGuard.ValidateForDelete` — già implementato).
- Reparse point (junction/symlink) = veto di attraversamento nello scanner (implementato) e
  rigetto della cancellazione su qualsiasi elemento individuato come reparse.
- **TOCTOU**: ri-validazione immediatamente prima di ogni Delete; se il percorso e' cambiato
  tra scansione ed eliminazione → abort + avviso senza esporre il percorso nel registro (**implementato in Fase 4**).
## 2. Regole invalicabili dal dominio
- `ProtectedPaths.CriticalRoots`: Windows, Program Files, ProgramData, System32, SystemX86.
- Cartelle personali protette: Documenti, Desktop, Immagini, Video, Musica, Downloads.
- Radici di drive: mai cancellabili (controllo path-root già in `PathGuard`).
- Eccezioni documentate SOLO dove tecnicamente giustificate: `C:\Windows\Temp` (categoria Temp).
  Prefetch volutamente escluso (degraderebbe l'avvio).
- Le policy SAFE/BALANCED/EXPERT risiedono nel Core: nessun flag UI puo' abilitare l'impensabile.
## 3. DLL planting / sostituzione binari
- Manifest `asInvoker`; `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32|APPLICATION_DIR)`
  al boot; P/Invoke con moduli espliciti; scritture solo in `%LOCALAPPDATA%\NexusOptimizer` (da Fase 3).
## 4. Integrità dati
- `ConfigStore.Save` atomica (tmp + move) — implementato.
- Quarantine cifrata AES-GCM a blocchi con key DPAPI CurrentUser; quota massima di 1 GiB con
  purge LRU delle operazioni scadute (**implementato in Fase 4**). Percorsi e nomi file sono
  cifrati nei metadati della quarantena, mai nella cronologia.
- SQLite WAL + integrity check on start (quando introdotto, Fase 3/4).
## 5. Privilegi
- Nessuna componente elevata permanentemente. Vedi `PRIVILEGI.md`.
## 6. Aggiornamenti
- **Implementato (Fase 7)**: controllo opt-in, spento di default e inerte senza un canale
  configurato. Solo HTTPS assoluto (feed e pagina della release), manifest limitato a 64 KB,
  fallimento dichiarato e mai silenzioso, nessun downgrade. Il programma **non scarica e non
  installa** binari: annuncia la versione e apre la pagina della release nel browser.
  Dettagli e formato del manifest in [`AGGIORNAMENTI.md`](AGGIORNAMENTI.md).
- Rimandato a V2 (auto-updater vero): download con verifica Authenticode (WinVerifyTrust) e
  hash del manifest, rollback che conserva `PreviousVersion\`, canale beta.
## 7. Logging sicuro (§42)
- Nessuna password/token/cookie/contenuto documenti nei log; export diagnostic anonimizzato opzionale.
## 8. Privacy by design
- Telemetria **OFF** e assente. Le sole chiamate outbound possibili sono tre, tutte opt-in o su
  richiesta esplicita, e tutte spente con le impostazioni predefinite:
  1. **controllo versione di Nexus** — scarica un manifest JSON da un canale HTTPS configurato
     dall'utente, senza inviare alcun dato (al massimo una volta ogni 24 ore);
  2. **aggiornamenti dei programmi** — `winget upgrade`, il gestore pacchetti di Windows, che
     confronta le versioni installate con i manifest ufficiali dei produttori;
  3. **aggiornamenti dei driver** — ricerca su Windows Update tramite l'agente di sistema.
  Le ultime due partono da un pulsante o dal rispettivo interruttore nelle Impostazioni, e
  producono un avviso: **nessun download e nessuna installazione automatica**
  ([`PROGRAMMI-E-DRIVER.md`](PROGRAMMI-E-DRIVER.md)). Con le impostazioni predefinite:
  **zero chiamate outbound**.
- Nessun account, nessun machine-ID, nessuna vendita dati.
- Gli avvisi restano locali: nascono da metriche misurate sul PC e non lasciano la macchina
  ([`NOTIFICHE.md`](NOTIFICHE.md)).
## 9. Istanza singola
- Mutex e evento di attivazione con prefisso `Local\`: confinati alla sessione dell'utente.
  Una seconda copia non apre una finestra gemella sulla stessa configurazione, riporta in primo
  piano quella gia' aperta e termina. Evita scritture concorrenti su `config.json`.

## 10. No dark patterns (§25)
- Health score cliccabile con formula breakdown.
- Messaggi rossi solo su metriche certe (es. SSD pieno ≥90%).
- Nessun popup intimidatorio, nessun "problemi trovati" gonfiato, nessun bundle sponsorizzato.
## 11. Input validation
- Tutti i percorsi da config/argomenti passano per PathGuard; regex restrittive sugli input UI numerici.
