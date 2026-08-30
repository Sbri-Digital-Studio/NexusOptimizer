# Centro avvisi — Nexus Optimizer

Gli avvisi nascono **solo da una misura reale**. Se Windows non espone la metrica
(per esempio la temperatura su una macchina senza sensori ACPI o senza GPU NVIDIA)
la regola corrispondente non viene valutata e non produce nulla: nessun avviso
inventato, nessuna stima.

Dominio: [`Core/Notifications`](../src/NexusOptimizer.Core/Notifications) —
`SystemAlertEvaluator` (regole, isteresi, cooldown) e `NotificationCenter`
(cronologia in memoria). Raccolta dati: [`NotificationService`](../src/NexusOptimizer.App/Services/NotificationService.cs).
Interfaccia: campanella nella barra titolo (`NotificationsViewModel`).

## Regole attive

| Regola | Sorgente misurata | Soglia | Gravità | Interruttore |
|---|---|---|---|---|
| Temperatura CPU | `Thermal Zone Information` (ACPI) | ≥ 85 °C per 15 campioni consecutivi; ≥ 95 °C = critico | Warning / Critical | `temperatureAlerts` |
| Temperatura GPU | NVML (driver NVIDIA) | ≥ 87 °C per 15 campioni consecutivi; ≥ 96 °C = critico | Warning / Critical | `temperatureAlerts` |
| Spazio disco | `DriveInfo` sui volumi fissi | libero ≤ soglia utente (5/10/15/20 %); ≤ 5 % = critico | Warning / Critical | `notifyLowDisk` |
| Spazio recuperabile | API shell del Cestino | ≥ 2 GiB | Info | `notifyRecoverableSpace` |
| Nuova voce di avvio | `StartupService` vs baseline salvata | qualunque voce nuova | Warning | `startupMonitoring` |
| Aggiornamento disponibile | manifest del canale configurato | versione superiore alla corrente | Info | `checkForUpdates` |

Ogni interruttore è nelle Impostazioni (sezione **AVVISI**); i quattro storici
restano anche fra i toggle rapidi della Dashboard. **Con l'interruttore spento la
regola non viene nemmeno valutata**: non è un filtro sulla visualizzazione.

## Perché un avviso non si ripete

- **Isteresi**: la regola si riarma solo quando il valore rientra oltre un margine
  (3 punti percentuali per il disco, 6 °C per le temperature). Un valore che
  oscilla intorno alla soglia non genera una raffica di avvisi.
- **Cooldown**: disco 6 ore, temperature 30 minuti, spazio recuperabile 24 ore.
- **Persistenza**: una nuova voce di avvio si annuncia una volta sola; la
  fotografia delle voci note vive in `config.json` (`startupBaseline`), quindi
  viene rilevata anche un'app aggiunta mentre Nexus era chiuso.
- **Deduplica finale**: il `NotificationCenter` scarta comunque la stessa chiave
  entro 5 minuti e conserva al massimo 50 voci.

Al primo avvio la baseline delle voci di avvio viene solo registrata: quello che
c'era prima di Nexus non è una novità e segnalarlo sarebbe rumore.

## Dove appaiono

- **Campanella nella barra titolo**: badge con il numero di non letti; aprendo il
  pannello tutto risulta letto. Ogni voce apre la sezione collegata (disco,
  Smart Clean, avvio, monitor).
- **Fumetto della tray**: solo quando l'applicazione è ridotta nella tray, dove la
  campanella non sarebbe raggiungibile.
- **Modalità silenziosa** (`quietMode`): sopprime i fumetti; gli avvisi restano
  nella campanella. È l'unico effetto della modalità silenziosa, ed è reale.

## Cadenza e costo

- Temperature: valutate sul campione già prodotto dal `SystemMonitor` (1 Hz),
  nessuna lettura aggiuntiva.
- Disco, Cestino e voci di avvio: primo controllo 45 secondi dopo l'avvio, poi
  ogni 10 minuti, su thread di lavoro. Con `notifyRecoverableSpace` spento il
  Cestino non viene nemmeno interrogato.

## Test

`SystemAlertEvaluatorTests` e `NotificationCenterTests`
([`tests/NexusOptimizer.Tests.Unit/NotificationTests.cs`](../tests/NexusOptimizer.Tests.Unit/NotificationTests.cs))
coprono soglie, isteresi, cooldown, interruttori spenti, sensore assente,
deduplica e tetto della cronologia. Il tempo è passato dal test, non letto
dall'orologio: le regole sono verificabili senza attese.
