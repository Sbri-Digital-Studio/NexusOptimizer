[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$IncludePackages
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Remove-WorkspaceItem([string]$Path, [switch]$Recursive) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $prefix = $workspaceRoot + [IO.Path]::DirectorySeparatorChar
    if (!$absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Rimozione rifiutata fuori dal workspace: $absolute"
    }
    if (!(Test-Path -LiteralPath $absolute)) { return }
    if ($PSCmdlet.ShouldProcess($absolute, 'Rimuovi artefatto rigenerabile')) {
        Remove-Item -LiteralPath $absolute -Force -Recurse:$Recursive
    }
}

foreach ($projectRoot in @(
    (Join-Path $workspaceRoot 'src'),
    (Join-Path $workspaceRoot 'tests')
)) {
    Get-ChildItem -LiteralPath $projectRoot -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        ForEach-Object { Remove-WorkspaceItem $_.FullName -Recursive }
}

if ($IncludePackages) {
    Remove-WorkspaceItem (Join-Path $workspaceRoot 'Installer\publish') -Recursive
    Remove-WorkspaceItem (Join-Path $workspaceRoot 'Installer\output') -Recursive
    Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'Installer') -File -Force |
        Where-Object { $_.Name.StartsWith('NexusOptimizer-', [StringComparison]::OrdinalIgnoreCase)
                       -and $_.Extension.Equals('.zip', [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Remove-WorkspaceItem $_.FullName }
}

Write-Host 'Workspace pulito. Tutti gli elementi rimossi sono rigenerabili.'
