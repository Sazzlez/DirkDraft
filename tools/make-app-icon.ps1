<#
.SYNOPSIS
    Renders src\DraftPilot.App\Assets\app.ico from the vector definition below.

.DESCRIPTION
    The icon is a stroked coffee mug on a blue-to-green gradient tile. Keeping the drawing here
    rather than only as a binary means the next change is an edit, not a redraw from scratch.

    Every size is drawn from the vector form instead of downscaling one large bitmap: a hairline
    stroke turns to mush when resampled, and 16 px is where an icon is actually looked at.

    Kept deliberately ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a byte-order mark as
    ANSI, and any non-ASCII character in here turns into a parse error on a German system.

.PARAMETER OutIco
    Target file. Defaults to the app's Assets\app.ico.

.PARAMETER PreviewDir
    Optional. Also writes one PNG per size plus a 1024 px version, for eyeballing the result.
#>
[CmdletBinding()]
param(
    [string]$OutIco,
    [string]$PreviewDir,
    [string]$Blue  = '#FF3B82F6',
    [string]$Green = '#FF22C55E'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

if (-not $OutIco) {
    $OutIco = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\DraftPilot.App\Assets\app.ico'
}

# Everything is laid out on a 1024 grid; each target size is drawn fresh from it.
$DESIGN = 1024.0
$CORNER = 200.0
$STROKE = 34.0

$cupPath    = 'M 305,413 H 607 A 16,16 0 0 1 623,429 V 636 A 92,92 0 0 1 531,728 H 381 A 92,92 0 0 1 289,636 V 429 A 16,16 0 0 1 305,413 Z'
$handlePath = 'M 623,440 A 95,95 0 1 1 623,612'
$steamPath  = 'M 362,260 V 305 M 460,260 V 305 M 560,260 V 305'

function New-IconBitmap([int]$size) {
    $k = $size / $DESIGN

    # Stroke floor: 34/1024 comes to half a pixel at 16 px, which renders as nothing at all.
    # Small sizes therefore carry a deliberately heavier line than the design calls for.
    $strokePx = [Math]::Max($size * ($STROKE / $DESIGN), 1.15)
    $strokeDesign = $strokePx / $k

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($k, $k)))

    $brush = New-Object System.Windows.Media.LinearGradientBrush(
        [System.Windows.Media.ColorConverter]::ConvertFromString($Blue),
        [System.Windows.Media.ColorConverter]::ConvertFromString($Green),
        (New-Object System.Windows.Point(0, 0)),
        (New-Object System.Windows.Point(1, 1)))

    $dc.DrawRoundedRectangle($brush, $null, (New-Object System.Windows.Rect(0, 0, $DESIGN, $DESIGN)), $CORNER, $CORNER)

    # Optical sizing: the mug fills about half the tile. That is right at 256 px and wasteful at
    # 16 px, where every pixel counts, so the drawing grows as the canvas shrinks. The pen is
    # divided by the same factor, so the shape gets bigger while the line stays at its floor.
    $artScale = if ($size -le 20) { 1.30 } elseif ($size -le 32) { 1.15 } else { 1.0 }
    if ($artScale -ne 1.0) {
        $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($artScale, $artScale, 512, 495)))
    }

    $pen = New-Object System.Windows.Media.Pen([System.Windows.Media.Brushes]::White, ($strokeDesign / $artScale))
    $pen.StartLineCap = 'Round'
    $pen.EndLineCap   = 'Round'
    $pen.LineJoin     = 'Round'

    foreach ($d in @($cupPath, $handlePath, $steamPath)) {
        $dc.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse($d))
    }

    if ($artScale -ne 1.0) { $dc.Pop() }
    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    return $rtb
}

function Get-PngBytes($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return $stream.ToArray()
}

# 16/20/24/32/40 cover the notification area at 100 to 250 percent scaling; the rest is Explorer.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

# A typed list, not an object array: PowerShell 5.1 flattens a byte array stored in a plain
# collection down to a single element, and the .ico then contains headers and nothing else.
$blobs = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($s in $sizes) {
    [byte[]]$png = Get-PngBytes (New-IconBitmap $s)
    $blobs.Add($png)

    if ($PreviewDir) {
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$s.png"), $png)
    }
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    if ($blobs[$i].Length -lt 100) { throw "PNG for $($sizes[$i]) px is only $($blobs[$i].Length) bytes." }
}

if ($PreviewDir) {
    [byte[]]$big = Get-PngBytes (New-IconBitmap 1024)
    [IO.File]::WriteAllBytes((Join-Path $PreviewDir 'icon-1024.png'), $big)
}

# ICO container: header, one 16-byte entry per image, then the PNG blocks. PNG-in-ICO is fine
# from Windows Vista on, and it keeps the file a third of the size of bitmap entries.
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    # 256 is written as 0: the field is one byte wide.
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $len = $blobs[$i].Length
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$len)
    $bw.Write([uint32]$offset)
    $offset += $len
}
for ($i = 0; $i -lt $sizes.Count; $i++) { $bw.Write($blobs[$i], 0, $blobs[$i].Length) }
$bw.Flush()

[IO.File]::WriteAllBytes($OutIco, $ms.ToArray())
Write-Host "Geschrieben: $OutIco ($([Math]::Round((Get-Item $OutIco).Length / 1KB, 1)) KB, $($sizes.Count) Groessen)"
