[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Salta la build portabile (utile quando serve solo il payload dell'installer).
    [switch]$SkipPortable,

    # Firma Authenticode degli eseguibili prima di creare l'archivio portabile.
    # Senza uno di questi parametri la build resta NON firmata e lo script lo dichiara.
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [securestring]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\NexusOptimizer.App\NexusOptimizer.App.csproj'
$publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'publish'))
$installed = [IO.Path]::GetFullPath((Join-Path $publishRoot $Runtime))
$portable = [IO.Path]::GetFullPath((Join-Path $publishRoot "portable-$Runtime"))
$archive = Join-Path $PSScriptRoot "NexusOptimizer-$Runtime.zip"

function Assert-InsidePublishRoot([string]$path) {
    if (!$path.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Percorso publish non valido: $path"
    }
}

function Reset-Directory([string]$path) {
    Assert-InsidePublishRoot $path
    # Ogni publish parte vuoto: nessun DLL/EXE di build precedenti nel pacchetto.
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Invoke-Sign([string]$file) {
    if (!$CertificateThumbprint -and !$CertificatePath) { return }
    $arguments = @{ Path = $file; TimestampUrl = $TimestampUrl }
    if ($CertificateThumbprint) {
        $arguments.CertificateThumbprint = $CertificateThumbprint
    }
    else {
        $arguments.CertificatePath = $CertificatePath
        if ($CertificatePassword) { $arguments.CertificatePassword = $CertificatePassword }
    }
    & (Join-Path $PSScriptRoot 'Sign.ps1') @arguments
}

# ---------------------------------------------------------------------------
# 1. Build installata: cartella self-contained, NON impacchettata in un file
#    singolo. E' la forma piu' leggera in memoria e la piu' rapida ad avviarsi
#    (misurato: ~550 ms e ~147 MB contro ~690 ms e ~206 MB del bundle compresso),
#    ed e' quella che l'installer copia in Programmi.
# ---------------------------------------------------------------------------
Reset-Directory $installed

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    --output $installed

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (installed) failed with exit code $LASTEXITCODE"
}

Invoke-Sign (Join-Path $installed 'NexusOptimizer.exe')
Write-Host "Build installabile: $installed"

# ---------------------------------------------------------------------------
# 2. Build portabile: un solo eseguibile, senza compressione del bundle. La
#    compressione dimezza il file su disco ma tiene ~60 MB in piu' in memoria
#    per tutta la sessione, e l'archivio ZIP risulta comunque della stessa
#    dimensione: non vale il costo su un'app residente nella tray.
# ---------------------------------------------------------------------------
if (!$SkipPortable) {
    Reset-Directory $portable

    dotnet publish $project `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false `
        -p:DebugType=None `
        --output $portable

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish (portable) failed with exit code $LASTEXITCODE"
    }

    Invoke-Sign (Join-Path $portable 'NexusOptimizer.exe')

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Archivio portabile: $archive"
}

if (!$CertificateThumbprint -and !$CertificatePath) {
    Write-Warning ('Build NON firmata: SmartScreen mostrerà l''avviso rosso a chi la scarica. ' +
        'Per firmare: .\Installer\Publish.ps1 -CertificateThumbprint <thumbprint>')
}
