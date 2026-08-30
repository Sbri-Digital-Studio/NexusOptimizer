[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$assetDirectory = Join-Path $repoRoot 'src\NexusOptimizer.App\Assets'
$outputPath = Join-Path $assetDirectory 'NexusOptimizer.ico'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return ,$path
}

function Get-PngBytes([int]$pixelSize) {
    $bitmap = New-Object System.Drawing.Bitmap($pixelSize, $pixelSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $pixelSize / 256.0
    $background = New-RoundedRectanglePath (8 * $scale) (8 * $scale) (240 * $scale) (240 * $scale) (56 * $scale)
    $backgroundBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#101418'))
    $graphics.FillPath($backgroundBrush, $background)

    $nPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([single](57 * $scale), [single](54 * $scale)),
        [System.Drawing.PointF]::new([single](95 * $scale), [single](54 * $scale)),
        [System.Drawing.PointF]::new([single](159 * $scale), [single](153 * $scale)),
        [System.Drawing.PointF]::new([single](159 * $scale), [single](54 * $scale)),
        [System.Drawing.PointF]::new([single](198 * $scale), [single](54 * $scale)),
        [System.Drawing.PointF]::new([single](198 * $scale), [single](202 * $scale)),
        [System.Drawing.PointF]::new([single](160 * $scale), [single](202 * $scale)),
        [System.Drawing.PointF]::new([single](95 * $scale), [single](102 * $scale)),
        [System.Drawing.PointF]::new([single](95 * $scale), [single](202 * $scale)),
        [System.Drawing.PointF]::new([single](57 * $scale), [single](202 * $scale))
    )
    $nBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#4F8CFF'))
    $graphics.FillPolygon($nBrush, $nPoints)
    $mintBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#34C759'))
    $graphics.FillEllipse($mintBrush, 181 * $scale, 181 * $scale, 28 * $scale, 28 * $scale)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = $stream.ToArray()

    $stream.Dispose()
    $mintBrush.Dispose()
    $nBrush.Dispose()
    $backgroundBrush.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return ,$result
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($iconSize in $sizes) {
    $images += ,(Get-PngBytes -pixelSize $iconSize)
}
$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize

$fileStream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $image = $images[$index]
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$image.Length)
    $writer.Write([UInt32]$offset)
    $offset += $image.Length
}

foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()
$fileStream.Dispose()

Write-Host "Nexus Optimizer icon written to $outputPath"
