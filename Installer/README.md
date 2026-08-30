# Nexus Optimizer — packaging

`Publish.ps1` produce l'app Windows self-contained: l'utente non deve installare
.NET, Visual Studio o altri strumenti di sviluppo. Il target predefinito è `win-x64`;
ARM64 è disponibile quando tutti i pacchetti della macchina di build lo supportano.

```powershell
.\Installer\Publish.ps1
```

Lo script produce due pacchetti:

- `Installer\publish\win-x64\` — cartella self-contained, **payload dell'installer**. Non e'
  impacchettata in un file singolo: e' la forma piu' rapida ad avviarsi e la piu' leggera in
  memoria (~550 ms e ~146 MB contro ~690 ms e ~206 MB del bundle compresso, misurati).
- `Installer\publish\portable-win-x64\NexusOptimizer.exe` — eseguibile unico per l'uso portabile,
  archiviato anche in `Installer\NexusOptimizer-win-x64.zip`. Il bundle non viene compresso: la
  compressione tiene ~60 MB in piu' in memoria per tutta la sessione e l'archivio ZIP risulta
  comunque della stessa dimensione.

Con `-SkipPortable` si genera solo il payload dell'installer. Le cartelle di destinazione vengono
svuotate prima della pubblicazione, quindi non conservano binari di versioni precedenti.

## Installer Windows

`NexusOptimizer.iss` è lo script Inno Setup con installazione per utente, collegamenti
Start/Desktop, icona e avvio opzionale al termine del setup. Per creare il setup serve
Inno Setup 6 (`ISCC.exe`) installato localmente:

```powershell
.\Installer\BuildInstaller.ps1
```

Il setup compilato viene scritto in `Installer\output`.

## Firma Authenticode

Senza firma SmartScreen mostra l'avviso rosso a chiunque scarichi l'eseguibile: e' un gate di
release, non un dettaglio. Gli script non contengono alcun certificato, si indica il proprio.

```powershell
# app portabile firmata
.\Installer\Publish.ps1 -CertificateThumbprint <thumbprint>

# app + setup + disinstallatore firmati
.\Installer\BuildInstaller.ps1 -CertificateThumbprint <thumbprint>

# firma di un artefatto gia' prodotto, anche con PFX
.\Installer\Sign.ps1 -Path .\Installer\output\NexusOptimizer-Setup.exe -CertificatePath .\cert.pfx
```

Firma e marca temporale SHA-256, con verifica `signtool verify /pa` al termine; serve il
componente "Windows SDK Signing Tools". Senza parametri di firma gli script proseguono ma
**dichiarano a video che la build non e' firmata**. La verifica su una macchina pulita resta
obbligatoria prima di una distribuzione pubblica. Dettagli in
[`../docs/AGGIORNAMENTI.md`](../docs/AGGIORNAMENTI.md).
