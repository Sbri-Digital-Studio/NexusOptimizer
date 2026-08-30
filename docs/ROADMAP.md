# Roadmap — Nexus Optimizer

## Stato implementazione corrente

- Dashboard e Performance con metriche locali reali.
- Dashboard premium allineata al riferimento: gauge WPF animati per CPU/RAM/disco,
  sparkline con colore per metrica e Health Score immediato e cromatico.
- Prima espansione funzionale del riferimento: Optimizer sicuro, RAM Manager live,
  Disk Manager sui volumi locali, Privacy Guard e hub Tools Windows.
- Smart Clean con analisi, Dry Run, conferma e perimetro PathGuard.
- Info Sistema con rilevamento hardware e VRAM a 64 bit.
- **Process Manager read-only**: CPU, RAM, thread, ricerca, dettagli, firma e SHA-256 su richiesta.
- Performance con grafici numerici interattivi, tooltip sui campioni e unità rete automatiche (KB/s, MB/s, GB/s).
- Smart Clean con Dry Run e pulsante **ELIMINA** separato: conferma esplicita e spostamento nel Cestino.
- **Startup Manager**: lettura Run/RunOnce, StartupApproved e StartupTask con stato coerente a Gestione attività.
- **Diagnostica locale (FASE 5)**: PC Health Score spiegabile (spazio disco, affidabilità app, uptime),
  crash analyzer in sola lettura su Event Viewer e raccomandazioni mai automatiche.
- **Optimizer operativo**: avvii automatici non essenziali, pulizia cache nel Cestino,
  preferenze utente di Windows, compattazione memoria ed effetti visivi. Ogni voce mostra
  lo stato reale prima di agire, riporta l'esito misurato e — dove tocca impostazioni
  persistenti — si annulla ripristinando il valore salvato in config.json.
- **Modalità Gaming (FASE 7)**: boost reale e interamente reversibile prima di giocare —
  chiusura guidata delle app in background, piano energetico, Game DVR/Modalità gioco,
  priorità al gioco in primo piano e compattazione memoria, con misura effettiva della RAM
  tornata disponibile. Dettagli in [`MODALITA-GAMING.md`](MODALITA-GAMING.md).
- **Livelli modalità SAFE / BALANCED / EXPERT** applicati sia alla Modalità Gaming sia
  all'Optimizer: SAFE esclude le azioni che scrivono preferenze di sistema, BALANCED
  sblocca le chiavi HKCU, EXPERT le modifiche che restano attive a Nexus chiuso.
  Le voci fuori livello restano visibili con il motivo del blocco e l'annullamento
  di ciò che è già applicato non viene mai impedito.
- **GPU completa in tempo reale**: utilizzo e VRAM da query PDH jolly su "GPU Engine"/
  "GPU Adapter Memory"; temperatura, frequenza e consumo in watt da NVML, la libreria
  ufficiale NVIDIA installata col driver (su GPU AMD/Intel i campi restano "n.d.").
  Temperatura CPU da "Thermal Zone Information" quando il firmware la espone.
- **"Il mio PC" premium**: intestazione con illustrazione del case, sintesi per componente
  e valori formattati (date leggibili, GHz, MB di cache, salute dei dischi).
- **Sistema di icone a tratto** coerente su tutta l'interfaccia (griglia 24x24, spessore
  costante a ogni dimensione).
- **Centro avvisi reale (FASE 7)**: campanella nella barra titolo alimentata solo da misure
  vere — temperatura CPU/GPU sostenuta, spazio disco sotto la soglia scelta, Cestino oltre
  2 GiB, nuove voci di avvio rispetto alla baseline salvata. Isteresi e cooldown perche' un
  avviso non diventi un assillo; i quattro interruttori della Dashboard ora governano davvero
  la valutazione delle regole e la modalita' silenziosa sopprime i fumetti della tray.
  Dettagli in [`NOTIFICHE.md`](NOTIFICHE.md).
- **Controllo aggiornamenti opt-in (FASE 7)**: spento di default, inerte senza un canale HTTPS
  configurato. Scarica solo un manifest JSON, non invia dati, non scarica ne' installa binari:
  annuncia la versione e apre la pagina della release. [`AGGIORNAMENTI.md`](AGGIORNAMENTI.md).
- **Istanza singola** per sessione utente: la seconda copia riporta in primo piano la prima
  invece di duplicare finestra, monitoraggio e scritture su `config.json`.
- **Firma Authenticode** supportata dagli script di packaging (`Publish.ps1`, `BuildInstaller.ps1`,
  `Sign.ps1`), con marca temporale e verifica; senza certificato la build viene dichiarata non
  firmata a video invece di sembrare pronta.
- **Localizzazione IT/EN completa**: tutta l'interfaccia e tutti i messaggi composti a runtime
  (esiti delle ottimizzazioni con singolare/plurale, resoconto del boost, schede WMI di "Il mio
  PC", frasi di stato, conferme, avvisi, dialoghi). 785 chiavi per lingua. Il **cambio lingua e'
  immediato**: i testi statici sono legati a `Locale.Live` e si riscrivono senza cambiare pagina.
  Numeri e date seguono la lingua scelta e non il sistema (`2,3 GB` / `2.3 GB`), marcatore dato
  assente incluso (`n.d.` / `n/a`). Gate di test su parita' delle chiavi, coerenza dei segnaposto
  e caricamento di tutte le viste. Dettagli in [`LOCALIZZAZIONE.md`](LOCALIZZAZIONE.md).
- **Campionamento corretto (difetto risolto)**: l'utilizzo CPU alternava valori reali e zeri.
  Due cause distinte: il timer di campionamento non aveva guardia di ri-entranza (un giro piu'
  lento dell'intervallo ne faceva partire un secondo in parallelo, e due letture PDH ravvicinate
  restituiscono ~0), e il contatore `Processor\% Processor Time` si aggiorna con granularita' di
  circa un secondo, quindi a cadenza piu' fitta restituisce zero. L'utilizzo CPU ora si calcola
  dai tempi di sistema del kernel (`GetSystemTimes`), la stessa base di Gestione attivita';
  l'inventario di processi e servizi e' su un timer separato, cosi' il giro veloce resta regolare.
  Verificato sul campo: 17 campioni consecutivi, nessuno a zero, cadenza 490-522 ms.
- **Cadenza configurabile**: `monitorIntervalMs` era nel file di configurazione ma non veniva
  letto da nessuno. Ora e' effettivo e scegliibile dalle Impostazioni (0,5 s / 1 s / 2 s); le
  finestre temporali dei grafici convertono secondi in campioni, cosi' l'asse dei tempi resta
  vero a qualunque cadenza. Le sparkline della Dashboard usano il raccordo morbido gia' presente
  nei grafici di Performance.
- **Sezione Programmi e driver**: inventario reale dei programmi installati (chiavi Uninstall del
  Registro, viste 64/32 bit, macchina e utente) con ricerca, ordinamento, dettagli e
  disinstallazione tramite il disinstallatore del produttore; rilevamento degli aggiornamenti dei
  programmi con **winget** e dei driver con **Windows Update**, con avviso automatico opzionale
  (al massimo una volta al giorno) e nessuna installazione automatica; inventario driver con le
  periferiche in errore in evidenza. Dettagli in [`PROGRAMMI-E-DRIVER.md`](PROGRAMMI-E-DRIVER.md).
- **Rifiniture d'uso**: loghi reali dei programmi estratti dai file che li contengono (valore
  `DisplayIcon` del Registro) e icone per classe di periferica nell'elenco driver; linguette di
  sezione colorate con l'area attiva evidenziata; card Optimizer della Dashboard scorrevole (l'ultima
  voce restava tagliata); Impostazioni con intestazione, uscita esplicita e conferma visibile del
  salvataggio, che prima avveniva in silenzio.
- **VRAM**: memoria video nel RAM Manager con anello, valori e andamento, e barra di riempimento
  nella card GPU della Dashboard. Il totale arriva da NVML; dove Windows non lo espone si mostra il
  solo valore in uso, senza percentuale inventata. Nexus non "libera" la VRAM: non esiste un'API per
  farlo, e prometterlo sarebbe falso.
- **Accessibilita'**: i comandi rappresentati da sola icona (indietro, campanella, selettore
  livello, riduci/ingrandisci/chiudi, ricerca globale, freccia dettagli dell'Optimizer) espongono
  un nome accessibile localizzato agli screen reader.
- Tema Aurora Blue, onboarding, impostazioni, tray e pacchetto portabile.

## Gate generali
Ogni fase lascia la soluzione **compilabile e funzionante** (build verde + test verdi quando presenti).

## V1 — Fondamenta (Fasi 1–4 core, poi 5–6 gate release)
- Renaming a Nexus Optimizer, struttura, skeleton MVVM navigabile (FASE 1)
- Design system completo light/dark/auto + accento configurabile, icone vettoriali offline e marchio Aurora Blue (Canva) (FASE 2)
- Dashboard live reale: CPU/RAM/Disk/Rete (PDH), GPU/VRAM (DXGI), uptime, process count (FASE 3)
- Smart Clean completo con cataloghi e spiegazioni per categoria (Explain Mode) (FASE 3)
- Storage Analyzer (scan treemap leggero, Pause/Resume/Cancel) (FASE 3)
- Safety Engine: Dry Run, Quarantine cifrata, Undo, Transaction Log, Cronologia operazioni (**FASE 4 completata**)
- Process manager read-only con firma digitale/hash su richiesta (FASE 3)
- Startup manager read/disable reversibile (mai delete) (FASE 3)
- System Info completo (CPU cache/socket/virtualizzazione, BIOS/TPM/SecureBoot, RAM SPD,
  scheda madre, monitor, rete MAC/IP toggle privacy, audio) (FASE 3)
- Health Score v1 senza admin + breakdown trasparente (**FASE 5 completata**)
- Diagnostics e Crash Analyzer: lettura locale dell'Application Event Log, senza messaggi o percorsi (**FASE 5 completata**)
- Recommendation Engine locale read-only, senza azioni automatiche (**FASE 5 completata**)
- Command Palette CTRL+K + Global Search base (FASE 2)
- Settings completi: tema, accento, lingua IT/EN, esclusioni/witelist UI (FASE 2/3)
- Tray icon con menu ripristina/esci, minimizzazione intelligente, quiet mode (FASE 1/2)
- Onboarding First Run (cosa fa, cosa NON fa, privacy, reversibilita'); nessuna scansione senza consenso (FASE 2)
- Auto Safe Clean ONLY categorie GREEN certificate + anteprima recuperabile (**FASE 4 completata**)
- Logging strutturato + export diagnostic anonimizzato (FASE 2/3)

**Definition of Done V1** — misurata sulla build pubblicata (28/08/2026):

| Criterio | Soglia | Misura |
|---|---|---|
| test deletion-safety | ≥95% verdi | 19/19 (100%) |
| avvio | <800 ms | 552 ms (build installata) |
| CPU a riposo | ≈0% | 0,12-0,17% |
| RAM interfaccia | <150 MB | 145-147 MB privati |
| chiamate outbound | 0 con impostazioni predefinite | 0 (controllo aggiornamenti opt-in, senza canale non contatta nulla) |

La RAM rientrava nel limite solo abbandonando la compressione del bundle a file singolo, che
costava ~60 MB permanenti: vedi la tabella dei pacchetti nel README.

### Fase 6 — gate test completato

- Suite separate `NexusOptimizer.Tests.Unit` e `NexusOptimizer.Tests.Integration`.
- 149 test complessivi verdi; 19 test `DeletionSafety` verdi (100%, soglia richiesta ≥95%).
- Fase 7: aggiunti i gate su regole degli avvisi, canale aggiornamenti, parita' IT/EN e
  Apply/Revert delle preferenze utente su una chiave di registro di prova (creata e rimossa dal
  test, nessuna impostazione reale di Windows viene toccata dalla suite).
- Casi coperti: root drive/categoria, cartelle personali, esclusioni, prefix collision,
  junction/symlink, elementi fuori perimetro, quarantena AES-GCM manomessa e Undo senza overwrite.
- Gate ripetibile con `.\tools\Test-Phase6.ps1`.

## V2 — Diagnostica approfondita
Privileged Broker Service · SMART/throttle admin opt-in · Crash Analyzer + reliability history
umana · Duplicate Finder SHA-256 pipeline completa · App Manager residual-scan on-consenso ·
Privacy Center esteso · Network per-process connections viewer · Memory Intelligence avanzata
(leak-growth tracker) · Recommendation Engine v2 sulla storia locale · Auto-updater firmato +
rollback + canale beta · Portable build · Regression performance suite automatica.

## V3 — Espansione responsabile
Game/Laptop Intelligence completa (battery report, cicli, suggerimenti energia) · Treemap
interattivo stile Explorer + export PDF · Registry checker LIMITATO a startup/uninstall rotte
con backup pre-change · Scheduling manutenzione · Plugin locale sandboxed (valutazione
sicurezza) · Remote monitoring opt-in esplicito · Localizzazioni aggiuntive · Enterprise flags.

---

## Cosa NON faremo (vincoli etici/tecnici)

| Funzione | Perche' NO | Alternativa onesta |
|---|---|---|
| Registry cleaner aggressivo | beneficio misurabile ~zero documentato; alto rischio corruzione/boot | checker solo voci rotte con backup (V3) |
| "RAM cleaner"/EmptyWorkingSet continuo | svuota working set in loop → thrashing, peggiora la realta' | Compattazione una tantum, solo su richiesta esplicita e con la RAM liberata misurata davvero |
| Fake VRAM cleaning | nessuna API reale; sarebbe bugiardo | monitor VRAM per-process DXGI + hint applicativi |
| Disabilitare Defender/UAC | rompe la sicurezza dell'utente | lettura e riflessione STATUS senza modificare |
| Priorita'/affinity di massa sui processi | instabilita', BSoD patterns eterogenei, benefit ~zero | Modalita' Gaming: priorita' alta al SOLO gioco in primo piano, su richiesta e ripristinata all'uscita |
| Driver-updater automatico | rischio BSoD/version-rank inaffidabile | **implementato come rilevamento**: inventario driver, ricerca su Windows Update (non su cataloghi terzi), avviso e collegamento alla pagina IHV originale. L'installazione resta a Windows e all'utente |
| Cookie/session/password cleanup default | distruzione autenticazioni = reclamo #1 CCleaner | EXPERT item-per-item opt-in con warning grande |
| Claim AV/anti-malware | serve certificazione; falso senso di sicurezza | segnalare processi sconosciuti SENZA classificarli malware |
| Creare piani energetici personalizzati | tweak non verificabili, effetti su energia/batteria | Passaggio temporaneo a un piano gia' presente in Windows, con ripristino garantito all'uscita |
| Cloud account obbligatorio / online scan | viola privacy-by-design | tutto offline; remote console futura SOLO opt-in chiaro |
