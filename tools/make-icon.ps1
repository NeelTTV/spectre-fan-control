# Génère src\app.ico (ventilateur blanc sur carré bleu arrondi), plusieurs tailles en PNG
param([string]$Out = (Join-Path $PSScriptRoot '..\src\app.ico'))
Add-Type -AssemblyName System.Drawing

function New-FanPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = $size * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $s = $size - 1
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2, $s - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $size, $size), ([System.Drawing.Color]::FromArgb(80, 170, 255)), ([System.Drawing.Color]::FromArgb(30, 90, 220))
    $g.FillPath($grad, $path)

    $c = $size / 2.0
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    for ($i = 0; $i -lt 4; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($c, $c)
        $g.RotateTransform($i * 90 + 20)
        $blade = New-Object System.Drawing.Drawing2D.GraphicsPath
        $blade.AddBezier(0, 0, $size * 0.10, (-$size * 0.18), $size * 0.32, (-$size * 0.20), $size * 0.36, (-$size * 0.04))
        $blade.AddBezier($size * 0.36, (-$size * 0.04), $size * 0.24, $size * 0.02, $size * 0.10, $size * 0.04, 0, 0)
        $g.FillPath($white, $blade)
        $g.Restore($state)
    }
    $hub = $size * 0.13
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 90, 220))), $c - $hub, $c - $hub, $hub * 2, $hub * 2)
    $g.FillEllipse($white, $c - $hub * 0.55, $c - $hub * 0.55, $hub * 1.1, $hub * 1.1)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($sz in $sizes) { , (New-FanPng $sz) }

$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# Aperçu pour le README
$preview = Join-Path (Split-Path $Out) '..\docs\icon.png'
New-Item -ItemType Directory -Force (Split-Path $preview) | Out-Null
[System.IO.File]::WriteAllBytes($preview, (New-FanPng 128))
