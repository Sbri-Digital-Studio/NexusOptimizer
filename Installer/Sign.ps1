<#
.SYNOPSIS
    Firma Authenticode dei binari di Nexus Optimizer.

.DESCRIPTION
    Senza firma SmartScreen mostra l'avviso rosso a chiunque scarichi l'eseguibile:
    la firma e' quindi un gate di release, non un dettaglio. Lo script non contiene
    alcun certificato: si indica quello gia' installato nell'archivio personale
    (thumbprint) oppure un file PFX. La marca temporale e' obbligatoria, altrimenti
    la firma scade insieme al certificato.

.EXAMPLE
    .\Installer\Sign.ps1 -Path .\Installer\publish\win-x64\NexusOptimizer.exe -CertificateThumbprint ABCD...

.EXAMPLE
    .\Installer\Sign.ps1 -Path .\Installer\output\NexusOptimizer-Setup.exe -CertificatePath .\cert.pfx
#>
[CmdletBinding(DefaultParameterSetName = 'Store')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path,

    [Parameter(Mandatory = $true, ParameterSetName = 'Store')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $true, ParameterSetName = 'File')]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = 'File')]
    [securestring]$CertificatePassword,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'


$signTool = & (Join-Path $PSScriptRoot 'SignToolPath.ps1')
Write-Host "signtool: $signTool"

$targets = @()
foreach ($item in $Path) {
    $resolved = Resolve-Path -LiteralPath $item -ErrorAction Stop
    $targets += $resolved.Path
}

$arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl)

if ($PSCmdlet.ParameterSetName -eq 'Store') {
    $arguments += @('/sha1', $CertificateThumbprint)
}
else {
    if (!(Test-Path -LiteralPath $CertificatePath)) {
        throw "Certificato non trovato: $CertificatePath"
    }
    $arguments += @('/f', (Resolve-Path -LiteralPath $CertificatePath).Path)
    if ($CertificatePassword) {
        # La password non compare mai in chiaro negli argomenti dello script.
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringUni(
            [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($CertificatePassword))
        $arguments += @('/p', $plain)
    }
}

& $signTool @arguments @targets
if ($LASTEXITCODE -ne 0) {
    throw "Firma non riuscita (signtool exit code $LASTEXITCODE)."
}

if (!$SkipVerify) {
    & $signTool 'verify' '/pa' '/all' @targets
    if ($LASTEXITCODE -ne 0) {
        throw "Verifica della firma non riuscita (signtool exit code $LASTEXITCODE)."
    }
}

foreach ($target in $targets) {
    Write-Host "Firmato: $target"
}
