# Livelli modalità — SAFE · BALANCED · EXPERT

Il selettore in fondo alla barra laterale non è un'etichetta: decide **cosa il
programma può proporre**. Il valore è salvato in `config.json` (`mode`) e vale
per l'intera applicazione, non per la singola sessione.

La regola che li governa è una sola: **più si sale, più il programma può toccare
cose che restano cambiate anche quando Nexus è chiuso.** Nessun livello rende
un'operazione irreversibile o silenziosa.

## Optimizer

| Azione | Livello minimo | Perché lì |
|---|---|---|
| Riduci le app all'avvio | SAFE | agisce su voci utente già gestite da Startup Manager, con comando originale salvato |
| Pulisci cache e file temporanei | SAFE | i file vanno nel Cestino, recuperabili senza il nostro aiuto |
| Libera memoria in background | SAFE | operazione una tantum, non lascia alcuna traccia |
| Ottimizza le preferenze di Windows | **BALANCED** | scrive preferenze in `HKEY_CURRENT_USER` |
| Riduci gli effetti visivi | **BALANCED** | scrive preferenze in `HKEY_CURRENT_USER` |
| Piano energetico prestazionale | **EXPERT** | cambia il comportamento del PC anche a Nexus chiuso |

Le voci fuori livello restano **visibili**, con il motivo del blocco al posto dei
pulsanti: nascondere una funzione è meno onesto che spiegare perché non è
disponibile. Non entrano nel lotto di "Applica selezionate" e il loro comando di
applicazione è disabilitato anche a livello di motore, non solo nell'interfaccia.

> **Il livello non blocca mai l'annullamento.** Se un'ottimizzazione è già
> applicata e poi si torna a SAFE, il pulsante *Annulla* resta disponibile: sarebbe
> assurdo impedire di disfare qualcosa che il programma stesso ha fatto.

## Modalità Gaming

| Cosa cambia | SAFE | BALANCED | EXPERT |
|---|---|---|---|
| App pre-selezionate nell'elenco (su 54 catalogate) | 29 | 47 | 54 |
| Categorie aggiunte | sync cloud, suite RGB, updater | browser, musica, messaggistica | launcher di gioco, Discord |
| App non catalogate (>60 MB, con finestra) | no | no | sì, mai pre-selezionate |
| Chiusura forzata se l'app non risponde | no | no | sì, opzionale |

In ogni livello l'elenco completo resta visibile e la selezione finale è sempre
dell'utente: il livello decide solo cosa risulta già spuntato.

## Cosa NON cambia con il livello

Smart Clean, Privacy Guard, Startup Manager, Disk Manager e Diagnostica si
comportano allo stesso modo nei tre livelli: hanno già una conferma esplicita per
ogni operazione e non ci sono azioni "più aggressive" da sbloccare.
