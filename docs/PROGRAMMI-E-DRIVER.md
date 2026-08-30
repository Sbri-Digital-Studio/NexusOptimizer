# Programmi e driver

Sezione dedicata a cosa è installato sul PC: programmi, aggiornamenti disponibili
e driver di periferica. Tre aree in una sola pagina (`nav.software`).

Il principio è quello del resto del progetto: **Nexus non rimuove e non installa
nulla per conto proprio**. Avvia lo strumento che Windows o il produttore hanno
previsto per quell'operazione, e mostra ciò che quegli strumenti dichiarano.

## Programmi installati

Fonte: le chiavi `Uninstall` del Registro, nelle viste a 64 e 32 bit, per la
macchina e per l'utente corrente — le stesse che alimentano "App e funzionalità".
Vengono escluse le voci che Windows stesso nasconde: `SystemComponent`,
aggiornamenti (`ReleaseType`) e voci figlie (`ParentKeyName`).

Per ogni programma: nome, produttore, versione, data di installazione, dimensione
dichiarata, percorso, ambito (utente o macchina) e architettura. Ricerca per nome
o produttore, ordinamento per nome, dimensione o data.

**Disinstallazione**: si avvia l'`UninstallString` dichiarata dal programma, dopo
una conferma esplicita. Da quel momento comanda il disinstallatore dell'autore
del software, con la sua interfaccia e il suo eventuale prompt UAC. Il caso MSI
viene normalizzato (`/I` → `/X`): lasciare `/I` riaprirebbe l'installazione.

Cosa **non** viene fatto: nessuna rimozione di file, nessuna pulizia di chiavi
"residue". Cancellare i resti di un'installazione che non si conosce è il modo
più rapido per rompere un programma che funzionava, ed è esattamente ciò che il
progetto ha deciso di non fare.

## Aggiornamenti dei programmi

Fonte: **winget**, il gestore pacchetti incluso in Windows (`winget upgrade`).

Perché winget e non un catalogo interno: le versioni disponibili arrivano dai
manifest ufficiali dei produttori, verificati con hash, e l'aggiornamento esegue
l'installer originale. Un catalogo compilato a mano invecchia, sbaglia il
confronto fra versioni e finisce per scaricare binari da fonti non verificabili.

L'aggiornamento parte solo su comando, un programma alla volta
(`winget upgrade --id <id> --silent`). Un fallimento tipico è la mancanza di
privilegi di amministratore: viene detto, non nascosto. Se winget non è presente
sul PC, l'area lo dichiara e indica come installarlo, invece di fingere che sia
tutto aggiornato.

La lettura della tabella di winget non dipende dalla lingua: le colonne si
ricavano dalla posizione dei titoli nella riga di intestazione. I casi coperti dai
test includono l'output reale italiano e inglese, la riga di riepilogo attaccata
ai dati senza riga vuota e la seconda tabella dei pacchetti che richiedono un
riferimento esplicito.

## Driver

Fonte: `Win32_PnPSignedDriver` per l'inventario (periferica, classe, fornitore,
versione, data, firma) e `Win32_PnPEntity` per le periferiche con un codice di
errore di Gestione dispositivi, che vengono mostrate per prime e possono essere
isolate con un filtro.

**Ricerca aggiornamenti**: interroga **Windows Update** tramite l'agente di
sistema (`IsInstalled=0 and Type='Driver'`). Windows conosce la matrice di
compatibilità del tuo hardware meglio di qualunque classifica di versioni.

Nexus **non scarica e non installa driver**, e non lo farà: elenca ciò che Windows
propone e apre Windows Update, che resta l'unico a poter annullare l'operazione.
Installare il driver sbagliato è uno dei pochi modi per rendere un PC non
avviabile — la voce "driver-updater automatico" resta fra le cose che il progetto
non fa ([`ROADMAP.md`](ROADMAP.md)). Per ogni periferica riconosciuta è
disponibile anche il collegamento alla pagina ufficiale del produttore.

## Avvisi automatici

Nelle Impostazioni, sezione **AGGIORNAMENTI**, due interruttori (spenti di
default):

- avvisa quando un programma installato ha una versione più recente;
- avvisa quando Windows Update propone driver nuovi.

Con l'interruttore acceso il controllo parte al massimo **una volta ogni 24 ore**
e produce un avviso nella campanella ([`NOTIFICHE.md`](NOTIFICHE.md)), con
collegamento a questa sezione. Nessun download, nessuna installazione. La stessa
versione non viene annunciata due volte: chi rimanda un aggiornamento non
ritrova lo stesso messaggio ogni giorno.

## Chiamate di rete

Questa sezione introduce due sorgenti esterne, entrambe **su richiesta esplicita
o con interruttore acceso**:

| Funzione | Sorgente | Quando |
|---|---|---|
| Aggiornamenti programmi | winget (manifest ufficiali) | pulsante, oppure controllo automatico se attivato |
| Aggiornamenti driver | Windows Update | pulsante, oppure controllo automatico se attivato |

Con le impostazioni predefinite nessuna delle due parte: l'inventario di
programmi e driver è completamente locale.
