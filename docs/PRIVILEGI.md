# Privilegi Administrator — Nexus Optimizer

Principio: **least privilege**, `asInvoker` permanente, elevazione breve e motivata.

## Matrice operativa

| Operazione | Admin? | Strategia |
|---|---|---|
| Pulizia %TEMP% utente, cache browser, thumbcache, DXCache, cestino | NO | UI asInvoker diretta |
| `C:\Windows\Temp`, SoftwareDistribution\Download | SI | Elevazione on-demand con finestra che dichiara COSA verra' toccato; richiesta SOLI per quella operazione |
| Cambio start-type servizi, restore point, SMART counters admin | SI | V1 = child process elevato motivato; V2 = Privileged Broker |
| Lettura processi, network monitor, storage analyzer, duplicate finder | NO | Mai elevata |

## V1 — Elevazione on-demand
- App principale: manifest `asInvoker` (avvio normale, badge UI "modalità non elevata").
- Singola operazione → piccolo EXE satellite `NexusOptimizer.Elevated.exe` (`requireAdministrator`):
  - input SOLO via pipe anonima creata PRIMA dell'elevazione (evita injection command-line);
  - nonce 128 bit + CRC sul canale di ritorno;
  - registra su transaction log; exit entro pochi secondi; nessuna UI residua.

## V2 — Privileged Broker Service
- Windows Service `LocalService` con named pipe `\\.\pipe\nexusoptimizer.agent`;
- ACL limitata al SID utente corrente;
- protocollo message-based con ID operazione enum chiuso (whitelist); nessuno spawn arbitrario;
- audit entry su ogni mutazione; doppio consenso Core-side.

## Regole dure
1. Mai task scheduler con privilegi alti per pulizie schedulate (si usa il servizio/broker).
2. Mai `Invoke-CommandAs`, mai RunAs generico con argomenti liberi.
3. Il token elevato vive solo la durata dell'operazione.
