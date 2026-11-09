<#
.SYNOPSIS
    Generates the app logo PNGs and the .ico used by the MSIX package and the executable.
.DESCRIPTION
    The logo is drawn in code so it can be regenerated at any size: a rounded blue tile with
    a stylised context menu (three rows, the middle one highlighted).
    Run from the repository root:  pwsh build/New-AppIcons.ps1
#>
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\NeoContext.App\Assets')
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws the logo into a square of $size pixels centred in a $width x $height canvas.
function New-Logo([int]$width, [int]$height, [double]$scale = 1.0) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $size = [Math]::Min($width, $height) * $scale
    $x = ($width - $size) / 2
    $y = ($height - $size) / 2

    $tile = New-RoundedRectanglePath $x $y $size $size ($size * 0.22)
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($x, $y), [System.Drawing.PointF]::new($x + $size, $y + $size),
        [System.Drawing.Color]::FromArgb(255, 0, 120, 212), [System.Drawing.Color]::FromArgb(255, 0, 76, 153))
    $g.FillPath($gradient, $tile)

    # Menu panel
    $panelX = $x + $size * 0.24; $panelY = $y + $size * 0.2
    $panelW = $size * 0.52; $panelH = $size * 0.6
    $panel = New-RoundedRectanglePath $panelX $panelY $panelW $panelH ($size * 0.07)
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::White), $panel)

    # Three rows; the middle one is the highlighted "selected" item.
    $rowH = $panelH / 3
    for ($i = 0; $i -lt 3; $i++) {
        $rowY = $panelY + $rowH * $i + $rowH * 0.32
        $barH = $rowH * 0.36
        if ($i -eq 1) {
            $hl = New-RoundedRectanglePath ($panelX + $size * 0.04) ($panelY + $rowH * $i + $rowH * 0.12) ($panelW - $size * 0.08) ($rowH * 0.76) ($size * 0.04)
            $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 204, 228, 247)), $hl)
        }
        $color = if ($i -eq 1) { [System.Drawing.Color]::FromArgb(255, 0, 95, 184) } else { [System.Drawing.Color]::FromArgb(255, 120, 130, 140) }
        $bar = New-RoundedRectanglePath ($panelX + $size * 0.09) $rowY ($panelW * 0.62) $barH ($barH / 2)
        $g.FillPath([System.Drawing.SolidBrush]::new($color), $bar)
    }

    $g.Dispose()
    return $bitmap
}

$pngs = @{
    'StoreLogo.png'          = @(50, 50, 1.0)
    'Square44x44Logo.png'    = @(44, 44, 1.0)
    'Square150x150Logo.png'  = @(150, 150, 0.6)
    'Wide310x150Logo.png'    = @(310, 150, 0.6)
    'LockScreenLogo.png'     = @(24, 24, 1.0)
}

foreach ($name in $pngs.Keys) {
    $w, $h, $s = $pngs[$name]
    $bmp = New-Logo $w $h $s
    $bmp.Save((Join-Path $OutputDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Multi-size .ico (PNG-compressed entries, supported since Windows Vista).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Logo $s $s 1.0
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$ico = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($ico)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([byte]($s % 256)); $writer.Write([byte]($s % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $writer.Write($img) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'NeoContext.ico'), $ico.ToArray())

Write-Host "Icons written to $OutputDirectory"
