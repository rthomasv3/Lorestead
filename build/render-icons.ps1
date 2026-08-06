# Renders every icon asset the platforms need from icon/icon.svg via Inkscape, so
# icon/ stays the single source of truth: update the SVG, run this script, done.
#
# Produces (all in icon/):
#   - icon-<size>.png       plain renders (desktop/general, Android mipmaps, iOS appiconset)
#   - icon-fg-<size>.png    Android adaptive-icon foreground layers: the SVG rendered
#                           with transparent padding so the art sits in the launcher
#                           mask's safe zone (content = 66/108 of the canvas)
#   - icon.ico              Windows icon (PNG-compressed entries: 16..256)
#   - icon.icns             macOS icon (PNG entries: 16..1024)
#
# Consumers:
#   - Lorestead.Client.csproj packs the pngs into Android mipmaps / iOS appiconset
#   - The adaptive background is a flat color (values/colors.xml) matching the SVG's
#     parchment tile (#ede5d2), so the tile blends into the layer behind it

$ErrorActionPreference = 'Stop'

# The .com console wrapper blocks until the export completes; the GUI .exe returns
# immediately and the files appear asynchronously.
$inkscape = 'C:\Program Files\Inkscape\bin\inkscape.com'
if (-not (Test-Path $inkscape)) {
    throw "Inkscape not found at $inkscape"
}

$iconDir = Join-Path $PSScriptRoot '..\icon'
$svg = Join-Path $iconDir 'icon.svg'

# ---- Plain square renders ----------------------------------------------------

$sizes = 16, 20, 24, 29, 32, 40, 48, 58, 60, 64, 72, 76, 80, 87, 96,
         120, 128, 144, 152, 167, 180, 192, 256, 512, 1024

foreach ($size in $sizes) {
    $out = Join-Path $iconDir "icon-$size.png"
    Write-Host "Rendering $out"
    & $inkscape $svg --export-type=png --export-filename=$out `
        --export-width=$size --export-height=$size 2>$null
    if (-not (Test-Path $out)) {
        throw "Inkscape failed to produce $out"
    }
}

# ---- Android adaptive-icon foreground layers ---------------------------------
# The adaptive canvas is 108dp; launchers mask the center ~72dp and the recommended
# safe zone is a 66dp circle, so the art occupies 66/108 of the canvas with
# transparent padding around it. Inkscape renders the art at the exact content size
# (66/108 of every density is a whole number), then System.Drawing centers it on a
# transparent canvas - no --export-area unit pitfalls (it takes px, this document's
# user units are mm), no resampling. Corners of the parchment tile that the launcher
# mask crops match the background layer's color, so the crop is invisible.
# Densities: mdpi..xxxhdpi at 108dp.

Add-Type -AssemblyName System.Drawing

$fgSizes = 108, 162, 216, 324, 432

foreach ($size in $fgSizes) {
    $content = $size * 66 / 108
    $tmp = Join-Path $iconDir "icon-fg-content.tmp.png"
    & $inkscape $svg --export-type=png --export-filename=$tmp `
        --export-width=$content --export-height=$content 2>$null
    if (-not (Test-Path $tmp)) {
        throw "Inkscape failed to produce the $content px adaptive content render"
    }

    $out = Join-Path $iconDir "icon-fg-$size.png"
    Write-Host "Composing $out"
    $art = [System.Drawing.Image]::FromFile($tmp)
    $canvas = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.DrawImage($art, [int](($size - $content) / 2), [int](($size - $content) / 2), $content, $content)
    $g.Dispose()
    $art.Dispose()
    $canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Remove-Item $tmp -Force
}

# ---- icon.ico (Windows) ------------------------------------------------------
# ICO is a tiny directory header over image blobs; PNG-compressed entries are
# supported since Vista and are what most tooling emits now.

function New-IcoFromPngs {
    param([int[]] $Sizes, [string] $OutPath)

    $blobs = foreach ($s in $Sizes) {
        ,@($s, [System.IO.File]::ReadAllBytes((Join-Path $iconDir "icon-$s.png")))
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $bw.Write([uint16]0)                # reserved
    $bw.Write([uint16]1)                # type: icon
    $bw.Write([uint16]$blobs.Count)

    $offset = 6 + 16 * $blobs.Count
    foreach ($entry in $blobs) {
        $s = $entry[0]; $bytes = $entry[1]
        $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256
        $bw.Write([byte]$dim)           # width
        $bw.Write([byte]$dim)           # height
        $bw.Write([byte]0)              # palette size
        $bw.Write([byte]0)              # reserved
        $bw.Write([uint16]1)            # color planes
        $bw.Write([uint16]32)           # bits per pixel
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($entry in $blobs) {
        $bw.Write([byte[]]$entry[1])
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
    Write-Host "Wrote $OutPath"
}

New-IcoFromPngs -Sizes 16, 24, 32, 48, 64, 128, 256 -OutPath (Join-Path $iconDir 'icon.ico')

# ---- icon.icns (macOS) -------------------------------------------------------
# ICNS is 'icns' + big-endian total length, then per-image chunks of
# type code + big-endian chunk length (including the 8-byte chunk header) + PNG data.
# Modern type codes take raw PNG: icp4/icp5 (16/32) and ic07..ic14 (128..1024,
# including the @2x aliases ic11=16@2x(32px), ic12=32@2x(64px), ic13=128@2x(256px),
# ic14=256@2x(512px)).

function Write-BigEndianUInt32 {
    param([System.IO.BinaryWriter] $Writer, [uint32] $Value)
    $bytes = [System.BitConverter]::GetBytes($Value)
    [Array]::Reverse($bytes)
    $Writer.Write($bytes)
}

function New-IcnsFromPngs {
    param([string] $OutPath)

    $chunks = @(
        @('icp4', 16), @('icp5', 32), @('ic11', 32), @('ic12', 64),
        @('ic07', 128), @('ic08', 256), @('ic13', 256), @('ic09', 512),
        @('ic14', 512), @('ic10', 1024)
    )

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $body = foreach ($chunk in $chunks) {
        ,@($chunk[0], [System.IO.File]::ReadAllBytes((Join-Path $iconDir "icon-$($chunk[1]).png")))
    }

    $total = 8
    foreach ($entry in $body) { $total += 8 + $entry[1].Length }

    $bw.Write([System.Text.Encoding]::ASCII.GetBytes('icns'))
    Write-BigEndianUInt32 -Writer $bw -Value ([uint32]$total)

    foreach ($entry in $body) {
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes($entry[0]))
        Write-BigEndianUInt32 -Writer $bw -Value ([uint32](8 + $entry[1].Length))
        $bw.Write([byte[]]$entry[1])
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
    Write-Host "Wrote $OutPath"
}

New-IcnsFromPngs -OutPath (Join-Path $iconDir 'icon.icns')

Write-Host "Done."
