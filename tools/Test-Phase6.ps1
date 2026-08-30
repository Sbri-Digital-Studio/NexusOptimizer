[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $workspaceRoot 'NexusOptimizer.slnx'

dotnet test $solution --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Suite completa non superata: exit code $LASTEXITCODE" }

dotnet test $solution --configuration $Configuration --no-build --no-restore `
    --filter 'Category=DeletionSafety'
if ($LASTEXITCODE -ne 0) { throw "Gate DeletionSafety non superato: exit code $LASTEXITCODE" }

Write-Host 'Fase 6 verificata: suite completa e gate DeletionSafety verdi.'
