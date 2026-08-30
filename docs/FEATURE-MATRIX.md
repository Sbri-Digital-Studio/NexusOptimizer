# Matrice funzionale — riferimento Nexus Optimizer

L'immagine `anteprima NexusOptimizer.png` è il riferimento di prodotto. Le funzioni vengono
implementate con dati reali e senza simulare azioni che Windows non rende sicure o verificabili.

| Area del riferimento | Implementazione corrente |
|---|---|
| Dashboard | Gauge e grafici reali CPU/RAM/GPU/disco/rete/processi, uptime, piano energetico e Health Score |
| Il mio PC | Inventario hardware con sintesi per componente e valori formattati (WMI, registro, NVML) |
| Smart Clean | Analisi, anteprima, Dry Run, Cestino, quarantena e Undo |
| Optimizer | Cinque ottimizzazioni reali con stato letto dal sistema, esito misurato e annullamento |
| RAM Manager | Memoria fisica live e storico; nessun falso RAM cleaner |
| Disk Manager | Capacità, utilizzo e spazio libero dei volumi locali |
| Startup Manager | Lettura e disattivazione reversibile delle voci di avvio |
| Privacy Guard | Stato telemetria/dati locali e accesso esplicito alle impostazioni |
| Tools | Otto strumenti Windows originali, senza elevazione automatica |
| Monitor in tempo reale | Grafici CPU/RAM/disco/rete con palette per metrica |
| Backup & Restore | Cronologia operazioni, quarantena e ripristino senza overwrite |
| Modalità Gaming | Boost reale e reversibile prima di giocare, con RAM liberata misurata |

## Incrementi successivi

- Storage Analyzer visuale e Duplicate Finder con pipeline SHA-256 completa.
- Privacy Center esteso e strumenti rapidi aggiuntivi, mantenendo conferme e reversibilità.
