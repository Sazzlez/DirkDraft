# Generates Assets\app.ico plus a magnified preview sheet for reviewing legibility.
#
# Mark: three rising bars, the tallest in gold. Blue and gold is the League palette, a bar chart says
# "this is built on statistics", and the highlighted tallest bar says "this one is the pick". It stays
# readable at 16 px, which is the size that actually matters in a taskbar.
#
# Every layer is drawn at 4x and downscaled, because GDI+ antialiasing alone gets mushy below 32 px.
[CmdletBinding()]
param(
    # Where to write the magnified review sheet. Defaults to the temp folder.
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir  = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\DraftPilot.App\Assets'
if (-not $PreviewPath) { $PreviewPath = Join-Path $env:TEMP "draftpilot-icon-preview.png" }
$preview = $PreviewPath
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir "app.ico"

function New-Squircle([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x,           $y,           $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y,           $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d,   0, 90)
  $p.AddArc($x,           $y + $h - $d, $d, $d,  90, 90)
  $p.CloseFigure()
  return $p
}

function Render-Icon([int]$size) {
  $ss = 4
  $s  = $size * $ss

  $big = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($big)
  $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.Clear([System.Drawing.Color]::Transparent)

  # --- Background: diagonal blue gradient in a squircle ---
  $radius = [single]($s * 0.235)
  $inset  = [single]($s * 0.015)
  $side   = [single]($s - 2 * $inset)
  $shape  = New-Squircle $inset $inset $side $side $radius

  $from = [System.Drawing.Color]::FromArgb(255, 124, 151, 255)
  $to   = [System.Drawing.Color]::FromArgb(255,  46,  66, 176)
  $rect = New-Object System.Drawing.RectangleF($inset, $inset, $side, $side)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $from, $to, 55.0)
  $g.FillPath($grad, $shape)

  # Soft highlight across the top, clipped to the shape: gives depth without a visible edge.
  $state = $g.Save()
  $g.SetClip($shape)
  $hiRect = New-Object System.Drawing.RectangleF($inset, $inset, $side, ($side * 0.55))
  $hiFrom = [System.Drawing.Color]::FromArgb(46, 255, 255, 255)
  $hiTo   = [System.Drawing.Color]::FromArgb(0,  255, 255, 255)
  $hi = New-Object System.Drawing.Drawing2D.LinearGradientBrush($hiRect, $hiFrom, $hiTo, 90.0)
  $g.FillRectangle($hi, $hiRect)
  $g.Restore($state)

  # Glass edge. Skipped on the tiny layers, where a 1 px rim only muddies the shape.
  if ($size -gt 24) {
    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(54, 255, 255, 255), [single]($s * 0.012))
    $g.DrawPath($edge, $shape)
  }

  # --- Mark: three rising bars, tallest one gold ---
  # The small layers get thicker bars and tighter gaps. Proportions that look right at 256 px turn
  # into three grey smudges at 16, which is the size a taskbar actually shows.
  $small = $size -le 24
  $barW    = [single]($s * $(if ($small) { 0.180 } else { 0.145 }))
  $gap     = [single]($s * $(if ($small) { 0.058 } else { 0.077 }))
  $blockW  = 3 * $barW + 2 * $gap
  $left    = [single](($s - $blockW) / 2 + $barW / 2)
  $base    = [single]($s * $(if ($small) { 0.775 } else { 0.755 }))
  $heights = $(if ($small) { @(0.250, 0.375, 0.500) } else { @(0.215, 0.330, 0.455) })
  $colors  = @(
    [System.Drawing.Color]::FromArgb(150, 255, 255, 255),
    [System.Drawing.Color]::FromArgb(205, 255, 255, 255),
    [System.Drawing.Color]::FromArgb(255, 255, 201,  98)
  )

  for ($i = 0; $i -lt 3; $i++) {
    $pen = New-Object System.Drawing.Pen($colors[$i], $barW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $x = [single]($left + $i * ($barW + $gap))
    $g.DrawLine($pen, $x, $base, $x, [single]($base - $s * $heights[$i]))
    $pen.Dispose()
  }

  $g.Dispose()

  # Downscale to the target size.
  $small = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $gs = [System.Drawing.Graphics]::FromImage($small)
  $gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $gs.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $gs.Clear([System.Drawing.Color]::Transparent)
  $gs.DrawImage($big, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
  $gs.Dispose()
  $big.Dispose()

  return $small
}

$sizes  = @(16, 20, 24, 32, 48, 64, 128, 256)
$bitmaps = @{}
$pngs = @()

foreach ($size in $sizes) {
  $bmp = Render-Icon $size
  $bitmaps[$size] = $bmp
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,($ms.ToArray())
  $ms.Dispose()
}

# --- ICO container: 6-byte header, 16 bytes per entry, then the PNG payloads ---
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the ICO format
  $bw.Write([Byte]$dim)
  $bw.Write([Byte]$dim)
  $bw.Write([Byte]0)
  $bw.Write([Byte]0)
  $bw.Write([UInt16]1)
  $bw.Write([UInt16]32)
  $bw.Write([UInt32]$pngs[$i].Length)
  $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

# --- Preview sheet: the 256 px art plus each small size magnified 8x in one strip ---
$mags = @(48, 32, 24, 16)
$pad = 24
$stripW = 0
foreach ($s in $mags) { $stripW += $s * 8 + 18 }
$width  = $pad + 256 + $pad + $stripW + $pad
$height = 384 + $pad * 2 + 40
$sheet = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gp = [System.Drawing.Graphics]::FromImage($sheet)
$gp.Clear([System.Drawing.Color]::FromArgb(255, 24, 26, 31))
$gp.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$gp.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$gp.DrawImage($bitmaps[256], (New-Object System.Drawing.Rectangle($pad, $pad, 256, 256)))

$font = New-Object System.Drawing.Font("Segoe UI", 11)
$label = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 158, 175))
$x = $pad + 256 + $pad
foreach ($s in $mags) {
  $mag = $s * 8
  $gp.DrawImage($bitmaps[$s], (New-Object System.Drawing.Rectangle($x, $pad, $mag, $mag)))
  $gp.DrawString("$s px", $font, $label, [single]$x, [single]($pad + $mag + 6))
  # True size beside the label, for an honest impression.
  $gp.DrawImage($bitmaps[$s], (New-Object System.Drawing.Rectangle(($x + 46), ($pad + $mag + 8), $s, $s)))
  $x += $mag + 18
}

$gp.Dispose()
$sheet.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
foreach ($b in $bitmaps.Values) { $b.Dispose() }

"wrote $out ($((Get-Item $out).Length) bytes, layers: $($sizes -join ', '))"
"preview $preview"