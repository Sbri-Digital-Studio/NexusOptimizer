# Architettura — Nexus Optimizer

Documento vivente FASE 1. Decisioni vincolanti per tutte le fasi successive.

## 1. Stack tecnologico

| Livello | Scelta | Motivazione |
|---|---|---|
| Linguaggio | C# latest su .NET 8 LTS | Runtime gia' installato sulla macchina; LTS fino al 2026-11, upgrade .NET 10 pianificato a runtime consolidati |
| UI | WPF + design system Fluent custom | Maturo, controllabile, nessun problema di packaging come WinUI 3; grafici leggeri autodisegnati (target <150 MB RAM UI) |
| MVVM | Manual INPC in Fase 1 → CommunityToolkit.Mvvm in Fase 2 quando i VM crescono | Zero dipendenze superflue finche' lo scheletro e' piccolo |
| Metriche | PerformanceCounter (PDH), WMI System.Management, DXGI P/Invoke | API documentate Microsoft; zero licenze |
| Persistenza | ConfigStore JSON atomico (già presente) → Microsoft.Data.Sqlite + DPAPI in Fase 3/4 | Local-first, niente cloud obbligatorio |
| Sicurezza temperature | Modulo opt-in LibreHardwareMonitor (MPL-2.0), solo su consenso utente esplicito | Windows NON espone temperature affidabili via API pubbliche: assenza di sensore = "dato non disponibile", mai valori finti |
| Test | xUnit + coverlet | Standard de facto; i test deletion-safety sono l'asset principale |
| DI | Microsoft.Extensions.DependencyInjection | Gia' referenziato; composizione modulare testabile |

Dipendenze esterne totali V1: solo pacchetti Microsoft ufficiali (+ LHM opzionale MPL-2.0).

## 2. Layer diagram

```
PRESENTAZIONE      NexusOptimizer.App (WPF, MVVM) — Pages, DesignSystem, Command Palette, Tray
                       ▲ ViewModel (INPC → CommunityToolkit in Fase 2)
SERVIZI APP        ScanCoordinator · MonitoringScheduler(adattivo) · RecommendationEngine
                       ▲ interfacce pure
DOMINIO            NexusOptimizer.Core — CleaningEngine · SafetyEngine(PathGuard, Quarantine,
                   Undo, TxLog) · ProcessMonitor · StartupManager · StorageAnalyzer ...
                       ▲ astrazioni (ISensorProvider, IProcessSource…)
INFRASTRUTTURA     NexusOptimizer.Hardware (PDH/WMI/DXGI/SMART opt-in) · NexusOptimizer.Data (SQLite+DPAPI)
                       │ privilegi
UI asInvoker ⇄ operazioni elevate ON-DEMAND (V1: exe satellite motivato) ⇄ Broker Service (V2)
```

Regole architetturali:
1. Ogni motore indipendentemente testabile.
2. Operazione lunga ⇒ sempre `async` + `CancellationToken` (Pause/Resume/Cancel, requisito §36).
3. Refresh metriche **adattivo**: finestra visibile 1 s · minimizzata/tray 10–30 s o fermo · Game Mode ridotto ai sensori chiave.
4. Le policy SAFE/BALANCED/EXPERT vivono nel **Core**: il motore rifiuta l'operazione, non solo la UI che la nasconde.
5. Fail-safe: dato incerto ⇒ mostrato all'utente, MAI eliminato (§45).
6. Dry-run nativo su ogni motore mutante (§39; `CleanOptions.DryRun` già nel codice).

## 3. Moduli (registro)

M01 Hardware Monitor (PDH/WMI/DXGI) · M02 Thermal opt-in · M03 System Information (WMI)
M04 Cleaning Engine · M05 Safety Engine (Quarantine/Undo/TxLog) · M06 Process Manager
M07 Startup Manager (disable reversibile, mai delete) · M08 Services Manager (SAFE=read-only)
M09 Storage Analyzer · M10 Duplicate Finder (size→fast-hash→SHA-256, preview obbligatoria)
M11 App Manager · M12 Privacy Center (explain-mode item-by-item) · M13 Network Monitor
M14 Diagnostics & Crash Analyzer (EventLog read) · M15 PC Health Score (formula trasparente)
M16 Recommendation Engine locale · M17 Notifiche & Modes (toast solo eventi materiali)
M18 Command Palette + Global Search · M19 Persistence (JSON→SQLite) · M20 Update Engine (V2, firmato HTTPS+hash+rollback)

Stato dettagliato dei moduli per fase: vedi `ROADMAP.md`.

### Diagnostica locale — Fase 5

M14–M16 sono implementati in `NexusOptimizer.Core/Health`. Il punteggio V1 usa esclusivamente tre
evidenze locali e leggibili: spazio libero sul disco di sistema (40 punti), arresti/blocchi
applicativi riconosciuti negli ultimi 7 giorni (40 punti) e uptime Windows (20 punti). Se una
fonte non è disponibile, il suo peso viene escluso e il risultato è normalizzato sui punti
restanti. Il reader del registro eventi conserva e mostra soltanto data, origine e ID: non legge
né registra il messaggio dell'evento, che potrebbe contenere percorsi o dati personali.

## 4. Struttura cartelle corrente

Vedi README; nuovi progetti si aggiungono nel momento in cui servono
(`NexusOptimizer.Data` alla Fase 3/4, `NexusOptimizer.Hardware` alla Fase 3, `NexusOptimizer.Service` broker in V2):
progetti vuoti creati "preventivamente" sono rumore, non architettura.
