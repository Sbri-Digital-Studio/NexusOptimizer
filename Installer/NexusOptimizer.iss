; Nexus Optimizer — Inno Setup 6 script
; Compilare con: .\Installer\BuildInstaller.ps1

#define MyAppName "Nexus Optimizer"
#define MyAppVersion "0.1.0-alpha.1"
#define MyAppPublisher "Nexus Optimizer"
#define MyAppExeName "NexusOptimizer.exe"

[Setup]
AppId={{A6EF2DD4-67B6-42C2-80B5-959F5F4C3C10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=NexusOptimizer-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\NexusOptimizer.App\Assets\NexusOptimizer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

; Firma Authenticode: attivata da BuildInstaller.ps1 con /DSignedBuild e lo
; strumento "nexussign". Senza il define il setup viene compilato non firmato.
#ifdef SignedBuild
SignTool=nexussign
SignedUninstaller=yes
#endif

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; La build installata non e' impacchettata in un file singolo: e' la forma piu'
; leggera in memoria e la piu' rapida ad avviarsi. Si copia l'intera cartella.
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti aggiuntivi:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia Nexus Optimizer"; Flags: nowait postinstall skipifsilent
