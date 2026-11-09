# Generates Assets/app.ico and Assets/app.png for MacroMaster.
#
# The mark: a solid periwinkle rounded tile with a bold, flat-capped "M" in near-black.
# No gradients, glows or outlines. Every icon size is rendered natively from vector
# shapes (not downscaled), and the M's vertical strokes are snapped to whole pixels
# so 16/20/24/32 px stay sharp in the taskbar and tray.
#
# Usage (Windows PowerShell):  powershell -ExecutionPolicy Bypass -File generate_icon.ps1

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$TileColor  = [System.Windows.Media.Color]::FromRgb(0x7C, 0x83, 0xFF)
$GlyphColor = [System.Windows.Media.Color]::FromRgb(0x11, 0x12, 0x2B)

# The "M" is a single filled polygon (unit tile coordinates, 0..1):
#   flat tops, vertical stems of width W, diagonals whose horizontal width H gives the
#   same visual weight as the stems, meeting in a sharp V. The same numbers are used
#   for the in-app logo (LogoMark in UI/Themes/DarkTheme.xaml, scaled to a 24 grid).
$Lx = 0.25; $W = 0.12; $Rx = 0.75; $Top = 0.30; $Bottom = 0.70; $H = 0.15; $VeeBottom = 0.62

function Render-Mark([int]$s) {
    $margin = if ($s -le 24) { 0 } else { [math]::Round($s / 32.0) }
    $t      = $s - 2 * $margin
    $radius = $t * 0.22

    # Stems snap to whole pixels at icon sizes so their edges are razor sharp
    $px = { param($u) $margin + $u * $t }
    $snap = ($s -le 128)
    $sw  = [math]::Max(2, $W * $t); if ($snap) { $sw = [math]::Round($sw) }
    $l0  = & $px $Lx;  if ($snap) { $l0 = [math]::Round($l0) }
    $r1  = & $px $Rx;  if ($snap) { $r1 = $s - $l0 }          # mirror exactly
    $l1  = $l0 + $sw;  $r0 = $r1 - $sw
    $tp  = & $px $Top;    if ($snap) { $tp = [math]::Round($tp) }
    $bt  = & $px $Bottom; if ($snap) { $bt = [math]::Round($bt) }
    $c   = ($l0 + $r1) / 2
    $h   = $H * $t
    $vb  = & $px $VeeBottom
    $m   = ($vb - $tp) / ($c - $l0)            # slope of the diagonals
    $vt  = $tp + $m * ($c - $l0 - $h)          # inner apex of the V
    $ti  = $tp + $m * ($l1 - $l0)              # where the diagonal leaves the stem

    $pts = @(
        @($l0, $bt), @($l0, $tp), @(($l0 + $h), $tp), @($c, $vt), @(($r1 - $h), $tp), @($r1, $tp),
        @($r1, $bt), @($r0, $bt), @($r0, $ti), @($c, $vb), @($l1, $ti), @($l1, $bt)
    )

    $fig = New-Object System.Windows.Media.PathFigure
    $fig.StartPoint = New-Object System.Windows.Point($pts[0][0], $pts[0][1])
    $fig.IsClosed = $true
    $fig.IsFilled = $true
    foreach ($pt in $pts[1..($pts.Count - 1)]) {
        $fig.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point($pt[0], $pt[1])), $true)))
    }
    $geo = New-Object System.Windows.Media.PathGeometry
    $geo.Figures.Add($fig)

    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRoundedRectangle((New-Object System.Windows.Media.SolidColorBrush($TileColor)), $null,
        (New-Object System.Windows.Rect($margin, $margin, $t, $t)), $radius, $radius)
    $dc.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush($GlyphColor)), $null, $geo)
    $dc.Close()

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    return $bmp
}

function To-Png($bmp) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

# Classic 32-bit DIB icon frame (BITMAPINFOHEADER + bottom-up BGRA rows + empty AND mask).
# Every icon loader understands these, unlike PNG frames.
function To-Dib($bmp) {
    $s = $bmp.PixelWidth
    $straight = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bmp, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $stride = $s * 4
    $px = New-Object byte[] ($stride * $s)
    $straight.CopyPixels($px, $stride, 0)

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($stride * $s)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($px, $y * $stride, $stride) }
    $maskStride = [math]::Ceiling($s / 32.0) * 4
    $w.Write((New-Object byte[] ($maskStride * $s)))
    $w.Flush()
    return ,$ms.ToArray()
}

$assets = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# Large PNG (used for the README / in-app image)
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.png'), (To-Png (Render-Mark 1024)))

# Multi-size ICO: bitmap frames up to 128 px, PNG for 256 px (the standard Windows layout)
$sizes  = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @($sizes | ForEach-Object {
    $bmp = Render-Mark $_
    if ($_ -ge 256) { ,(To-Png $bmp) } else { ,(To-Dib $bmp) }
})

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())

"Wrote Assets/app.png (1024) and Assets/app.ico ($($sizes -join ', '))"
