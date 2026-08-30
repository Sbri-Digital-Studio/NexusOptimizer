[CmdletBinding()]
param(
    # Con il thumbprint di un certificato di code signing vengono firmati sia
    # l'eseguibile pubblicato sia il setup e il suo disinstallatore. Senza, il
    # pacchetto resta NON firmato e lo script lo dichiara a video.
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$publishArguments = @{}
if ($CertificateThumbprint) {
    $publishArguments.CertificateThumbprint = $CertificateThumbprint
    $publishArguments.TimestampUrl = $TimestampUrl
}

& (Join-Path $PSScriptRoot 'Publish.ps1') @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path -LiteralPath $_ }

if ($candidates.Count -ne 1) {
    throw "Inno Setup 6 non è installato. Installa Inno Setup 6, poi riesegui .\\Installer\\BuildInstaller.ps1."
}

$script = Join-Path $repoRoot 'Installer\NexusOptimizer.iss'
$isccArguments = @()

if ($CertificateThumbprint) {
    $signTool = & (Join-Path $PSScriptRoot 'SignToolPath.ps1')
    # $q e $f sono segnaposto di Inno Setup (virgoletta e file da firmare):
    # vanno passati letterali, non espansi da PowerShell.
    $signCommand = "/Snexussign=`$q$signTool`$q sign /fd SHA256 /td SHA256 /tr $TimestampUrl " +
        "/sha1 $CertificateThumbprint `$f"
    $isccArguments += '/DSignedBuild'
    $isccArguments += $signCommand
}
else {
    Write-Warning ('Setup NON firmato: SmartScreen mostrerà l''avviso rosso a chi lo scarica. ' +
        'Per firmare: .\Installer\BuildInstaller.ps1 -CertificateThumbprint <thumbprint>')
}

& $candidates[0] @isccArguments $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

Write-Host 'Nexus Optimizer setup written to Installer\output'
if ($CertificateThumbprint) {
    Write-Host 'Setup e disinstallatore firmati con Authenticode.'
}
