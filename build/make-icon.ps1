<#
.SYNOPSIS
  Малює іконку PoE Noob Man (бурштинове кільце зі стрілкою «рівень вгору») і пакує її в .ico.
  Результат: src/PoeNoobMan.App/Assets/icon.ico та icon.png (256 px).
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "src/PoeNoobMan.App/Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $amber = [System.Drawing.Color]::FromArgb(255, 0xEF, 0x9F, 0x27)
    $light = [System.Drawing.Color]::FromArgb(255, 0xF7, 0xF3, 0xE8)
    $small = $s -le 24

    # Фон: темний заокруглений квадрат з легким градієнтом
    $pad = [float]($s * 0.04)
    $bg = New-RoundedRect $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ([float]($s * 0.22))
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s), ([System.Drawing.Color]::FromArgb(255, 0x2E, 0x2A, 0x24)), ([System.Drawing.Color]::FromArgb(255, 0x14, 0x13, 0x11))
    $g.FillPath($grad, $bg)
    if (-not $small) {
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x5A, 0x4A, 0x2E)), ([float][Math]::Max(1, $s * 0.012))
        $g.DrawPath($edge, $bg)
    }

    # Дві висхідні шеврони — «рівень вгору»: нижня бурштинова, верхня світла
    $strokeW = [float][Math]::Max(2, $s * ($(if ($small) { 0.15 } else { 0.11 })))
    $cx = $s * 0.5; $half = $s * 0.25; $rise = $s * 0.2
    foreach ($row in @(@{ Y = 0.66; Color = $amber }, @{ Y = 0.40; Color = $light })) {
        $pen = New-Object System.Drawing.Pen $row.Color, $strokeW
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $y = $s * $row.Y
        $g.DrawLines($pen, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([float]($cx - $half)), ([float]($y + $rise / 2))),
            (New-Object System.Drawing.PointF ([float]$cx), ([float]($y - $rise / 2))),
            (New-Object System.Drawing.PointF ([float]($cx + $half)), ([float]($y + $rise / 2)))
        ))
        $pen.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# Кадр ICO у форматі DIB (32 bpp BGRA + маска) — для розмірів менше 256
function ConvertTo-IcoDib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    # кома не дає PowerShell розгорнути масив байтів у список об'єктів
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    if ($size -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save((Join-Path $assets "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , $ms.ToArray()
    }
    else {
        $pngs += , (ConvertTo-IcoDib $bmp)
    }
    $bmp.Dispose()
}

# ICO: заголовок, каталог, PNG-дані
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "icon.ico"), $out.ToArray())
Write-Host "Готово: $(Join-Path $assets 'icon.ico')"
