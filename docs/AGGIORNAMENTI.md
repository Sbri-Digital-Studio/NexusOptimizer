# Aggiornamenti e firma — Nexus Optimizer

## Il principio

Nexus Optimizer non effettua **nessuna** chiamata di rete, con una sola eccezione
dichiarata: il controllo aggiornamenti. È **spento di default** e resta inerte
finché non viene configurato un canale HTTPS. Con l'impostazione predefinita il
programma è, e resta, completamente offline.

## Cosa fa e cosa non fa

Quando è attivo e configurato:

- scarica un piccolo manifest JSON (massimo 64 KB) dall'indirizzo indicato;
- confronta la versione annunciata con quella in esecuzione;
- se è più recente, pubblica un avviso nella campanella con il collegamento alla
  pagina della release.

Non fa nient'altro. In particolare: **non invia alcun dato** (nessun
identificativo, nessuna statistica, nessun machine-ID: solo lo `User-Agent`
`NexusOptimizer/<versione>` che qualunque client HTTP dichiara), **non scarica
binari** e **non installa nulla**. L'aggiornamento vero e proprio lo decide e lo
esegue la persona, dal browser.

Quando parte: all'avvio al massimo una volta ogni 24 ore, e su richiesta esplicita
con **Controlla ora** nelle Impostazioni. La stessa versione viene annunciata una
sola volta (`lastSeenUpdateVersion`): chi sceglie di restare sulla build corrente
non rivede l'avviso a ogni avvio.

## Regole del canale

- Solo **HTTPS assoluto**, per il feed e per la pagina della release indicata nel
  manifest. In chiaro un intermediario potrebbe annunciare la versione che vuole.
- Manifest malformato, versione illeggibile o canale irraggiungibile: esito
  `Failed` mostrato nelle Impostazioni. Nessun tentativo automatico di ripetere,
  nessun downgrade silenzioso.
- Il confronto usa le prime tre cifre della versione; i suffissi di pre-release
  (`0.4.0-beta.1`) sono etichette editoriali e non alterano l'ordine.

## Formato del manifest

```json
{
  "version": "0.2.0",
  "url": "https://esempio.org/nexus/releases/0.2.0",
  "notes": "Centro avvisi, controllo aggiornamenti, istanza singola.",
  "publishedUtc": "2026-09-01T10:00:00Z"
}
```

`version` e `url` sono gli unici campi usati oggi. Il file va servito su HTTPS e
può stare su qualunque hosting statico.

## Configurazione

Impostazioni → **AGGIORNAMENTI**: interruttore, URL del manifest, pulsante
**Controlla ora**, esito dell'ultimo controllo. In `config.json` corrispondono a
`checkForUpdates`, `updateFeedUrl`, `lastUpdateCheckUtc`, `lastSeenUpdateVersion`.

Senza URL configurato la Dashboard dice "Nessun canale configurato": dichiarare
"controllo attivo" senza un canale sarebbe una promessa non mantenuta.

## Firma Authenticode

Senza firma, SmartScreen mostra l'avviso rosso a chiunque scarichi l'eseguibile.
Gli script di packaging la supportano, ma **non contengono alcun certificato**: va
indicato quello proprio.

```powershell
# App portabile firmata
.\Installer\Publish.ps1 -CertificateThumbprint <thumbprint>

# App + setup + disinstallatore firmati
.\Installer\BuildInstaller.ps1 -CertificateThumbprint <thumbprint>

# Firma manuale di un artefatto già prodotto (anche con PFX)
.\Installer\Sign.ps1 -Path .\Installer\output\NexusOptimizer-Setup.exe -CertificatePath .\cert.pfx
```

Dettagli: SHA-256 per firma e marca temporale (`/fd SHA256 /td SHA256 /tr`), con
verifica `signtool verify /pa` al termine. La marca temporale è obbligatoria,
altrimenti la firma scade insieme al certificato. Senza parametri di firma gli
script proseguono ma **dichiarano a video che la build non è firmata**.

Il certificato non entra mai nel repository: si usa il thumbprint di un
certificato già installato nell'archivio personale, oppure un PFX esterno.
