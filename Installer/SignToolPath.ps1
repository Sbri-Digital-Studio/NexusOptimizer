<#
.SYNOPSIS
    Individua signtool.exe del Windows SDK (usato da Sign.ps1 e BuildInstaller.ps1).
.DESCRIPTION
    Restituisce il percorso completo dell'ultima versione installata; se il
    componente "Windows SDK Signing Tools" non c'è, l'errore lo dice esplicitamente
    invece di far fallire la firma con un messaggio oscuro.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
if ($command) { return $command.Source }

$roots = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
    (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$architecture = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
foreach ($root in $roots) {
    $found = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "$architecture\signtool.exe" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if ($found) { return $found }
}

throw 'signtool.exe non trovato: installa il Windows SDK (componente "Windows SDK Signing Tools").'
