# Nexus Optimizer

[![Build e test](https://github.com/Sbri0110/NexusOptimizer/actions/workflows/build.yml/badge.svg)](https://github.com/Sbri0110/NexusOptimizer/actions/workflows/build.yml)

Alternativa moderna, sicura e trasparente ai sistemi "cleaner/optimizer" per Windows.

**Principi:** sicurezza > affidabilita' > trasparenza > leggerezza. Ogni operazione e' analizzata,
spiegata, stimata e reversibile. Modalita' predefinita **SAFE**. Telemetria **assente**: l'unica
chiamata di rete possibile e' il controllo aggiornamenti, spento di default e inerte senza un
canale configurato, quindi con le impostazioni predefinite **zero chiamate outbound**. Fail-safe:
se un dato non e' certo viene mostrato all'utente, mai cancellato.

## Stato del progetto

| Fase | Contenuto | Stato |
|---|---|---|
| 1 | Architettura, struttura cartelle, skeleton UI navigabile | completata |
| 2 | Design system Aurora Blue, icone vettoriali locali, Command Palette | **in corso** |
| 3 | Hardware Monitor, Smart Clean, Info Sistema, Performance, Processi, Avvio | completata |
| 4 | Safety Engine: Dry Run, quarantena AES-GCM/DPAPI, Undo, cronologia e Auto Safe Clean | completata |
| 5 | Diagnostics, Crash Analyzer, Recommendations, PC Health Score | completata |
| 6 | Test unit/integration e gate **deletion-safety** | completata |
| 7 | Modalità Gaming, livelli SAFE/BALANCED/EXPERT, telemetria GPU reale | completata |
| 7b | Centro avvisi reale, controllo aggiornamenti opt-in, istanza singola | completata |
| 8 | Packaging, firma, installer/portable | **in corso** (script di firma pronti) |

> **Questa non è una versione finale.** Il progetto è in sviluppo attivo e continuerà a
> ricevere aggiornamenti: funzioni nuove, correzioni e affinamenti. Alcune aree sono
> dichiarate "in corso" nella tabella qui sopra proprio perché lo sono.

Documentazione: [`docs/MODALITA-GAMING.md`](docs/MODALITA-GAMING.md) ·
[`docs/LIVELLI-MODALITA.md`](docs/LIVELLI-MODALITA.md) ·
[`docs/NOTIFICHE.md`](docs/NOTIFICHE.md) · [`docs/AGGIORNAMENTI.md`](docs/AGGIORNAMENTI.md) ·
[`docs/PROGRAMMI-E-DRIVER.md`](docs/PROGRAMMI-E-DRIVER.md) ·
[`docs/LOCALIZZAZIONE.md`](docs/LOCALIZZAZIONE.md) ·
[`docs/ARCHITETTURA.md`](docs/ARCHITETTURA.md) ·
[`docs/SICUREZZA.md`](docs/SICUREZZA.md) · [`docs/PRIVILEGI.md`](docs/PRIVILEGI.md) ·
[`docs/ROADMAP.md`](docs/ROADMAP.md) · [`docs/DESIGN-SYSTEM.md`](docs/DESIGN-SYSTEM.md) ·
[`docs/BRAND.md`](docs/BRAND.md) ·
[`docs/FEATURE-MATRIX.md`](docs/FEATURE-MATRIX.md)

## Requisiti

- Windows 10 22H2 o 11
- .NET SDK 8 o superiore (build/test verificati con SDK 10)

## Comandi

```powershell
dotnet build NexusOptimizer.slnx          # compila la soluzione
dotnet test  NexusOptimizer.slnx          # suite di test
dotnet run --project src/NexusOptimizer.App/NexusOptimizer.App.csproj
.\tools\Test-Phase6.ps1                   # suite completa + gate deletion-safety
```

Avvio diretto su una sezione (utile per una scorciatoia dedicata):

```powershell
NexusOptimizer.exe --page:nav.gaming
```

Per verificare l'interfaccia, `NEXUS_TRACE_BINDINGS=1` scrive gli errori di binding XAML in
`%LOCALAPPDATA%\NexusOptimizer\logs\bindings-<pid>.log` (disattivato di default).

## Build distribuibile

`.\Installer\Publish.ps1` produce due pacchetti, ciascuno nella forma migliore per il suo uso:

| Pacchetto | Percorso | Forma | Avvio | RAM privata |
|---|---|---|---|---|
| Installazione | `Installer/publish/win-x64/` | cartella self-contained | ~550 ms | ~146 MB |
| Portabile | `Installer/publish/portable-win-x64/NexusOptimizer.exe` + `Installer/NexusOptimizer-win-x64.zip` | eseguibile singolo | ~870 ms | ~146 MB |

Il bundle a file singolo **non** viene compresso: la compressione dimezza il file su disco ma
tiene circa 60 MB in più in memoria per tutta la sessione, e l'archivio ZIP risulta comunque
della stessa dimensione (58 MB). Valori misurati su questa macchina.
Per rigenerarli da zero: `.\Installer\Publish.ps1`. Lo script pulisce l'output precedente prima
di pubblicare, evitando DLL o EXE obsoleti. Lo script Inno Setup per l'installazione con
collegamenti Start/Desktop è [`Installer/NexusOptimizer.iss`](Installer/NexusOptimizer.iss).

La firma Authenticode è supportata da entrambi gli script — nessun certificato è incluso nel
repository, si indica il proprio:

```powershell
.\Installer\Publish.ps1        -CertificateThumbprint <thumbprint>   # app portabile firmata
.\Installer\BuildInstaller.ps1 -CertificateThumbprint <thumbprint>   # app + setup + disinstallatore
```

Senza questi parametri la build resta **non firmata** e lo script lo dichiara a video: su una
macchina altrui SmartScreen mostrerebbe l'avviso rosso. La verifica su una macchina pulita resta
un gate di release pubblica. Dettagli in [`docs/AGGIORNAMENTI.md`](docs/AGGIORNAMENTI.md).

## Struttura

```
src/NexusOptimizer.Core   Dominio: cleaning, safety (PathGuard/ProtectedPaths), config, logging,
                          regole degli avvisi (Notifications), canale aggiornamenti (Updates)
src/NexusOptimizer.App    UI WPF (MVVM), monitoraggio, Smart Clean, Modalità Gaming, Info Sistema,
                          Processi, Avvio, Programmi e driver, centro avvisi, tray
tests/            Suite xUnit separata in unit e integration; gate deletion-safety
docs/             Decisioni di progetto e specifiche viventi
tools/            Verifica Fase 6, pulizia workspace e generazione icona
```

## Cosa NON fa questo programma

Niente finto ottimizzazioni, niente "RAM cleaner", niente registry cleaner aggressivo,
niente disinstallazione driver automatica, niente claim antivirus. Nessun avviso allarmistico:
ogni notifica nasce da una soglia misurata e spiegata in `docs/NOTIFICHE.md`. Dettagli in
`docs/ROADMAP.md`.

## Perché esiste

Il mercato dei "PC optimizer" è pieno di programmi che promettono e non mantengono:
percentuali di velocità inventate, contatori di "errori di registro" che salgono da soli,
pulsanti che fingono di liberare RAM, avvisi allarmistici costruiti per vendere una licenza.
Nexus Optimizer nasce come reazione diretta a tutto questo.

La regola è una sola: **niente che non si possa misurare, spiegare e annullare.** Ogni
operazione dichiara in anticipo cosa tocca e quanto spazio libera davvero, viene eseguita
solo dopo un'anteprima, e resta reversibile tramite quarantena e cronologia. Dove Windows
non permette un intervento sicuro e verificabile, il programma lo dice invece di simularlo.
Nessuna telemetria, nessuna metrica gonfiata, nessuna funzione decorativa.

La sezione [Cosa NON fa questo programma](#cosa-non-fa-questo-programma) è parte del
progetto quanto l'elenco delle funzioni.

## Feedback e contributi

Feedback, consigli, segnalazioni di bug e proposte sono benvenuti: apri una
[Issue](../../issues) o una Pull Request. Sono particolarmente utili le segnalazioni su
hardware o configurazioni diverse da quelle su cui è stato sviluppato, i casi in cui una
stima si rivela imprecisa, e qualunque comportamento che sembri promettere più di quanto
mantenga — quel tipo di segnalazione colpisce il cuore del progetto.

## Licenza

Distribuito sotto **GNU General Public License v3.0** — vedi [`LICENSE`](LICENSE).

In sintesi: puoi usare, studiare, modificare e ridistribuire il programma, anche
commercialmente; chi distribuisce una versione modificata deve rilasciarne il sorgente
sotto la stessa licenza. È una scelta deliberata: in una categoria di software segnata da
rebranding opachi e adware, il copyleft garantisce che ogni derivato resti ispezionabile
quanto l'originale.

Copyright (C) 2026 Sbri Digital Studio
