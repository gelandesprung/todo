Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$assetDir = Join-Path $PSScriptRoot "..\src\DayTodo\Assets"
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$icoPath = Join-Path $assetDir "app.ico"
$pngPath = Join-Path $assetDir "tray.png"

function New-RoundedPath($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90)
    $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90)
    $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-MarkBitmap($size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.05))
    $w = $size - 2 * $pad
    $r = [int]($size * 0.24)
    $path = New-RoundedPath $pad $pad $w $w $r
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(56, 138, 221))
    $g.FillPath($brush, $path)

    $penW = [Math]::Max(1.6, $size * 0.105)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]$penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLine($pen, [single]($size * 0.26), [single]($size * 0.51), [single]($size * 0.43), [single]($size * 0.68))
    $g.DrawLine($pen, [single]($size * 0.43), [single]($size * 0.68), [single]($size * 0.75), [single]($size * 0.33))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return $bytes
}

$sizes = @(16, 32, 48, 256)
$pngs = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $pngs.Add((New-MarkBitmap $s)) }

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = [byte]$(if ($s -ge 256) { 0 } else { $s })
    $bw.Write($b); $bw.Write($b)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
$bw.Dispose()

[System.IO.File]::WriteAllBytes($pngPath, (New-MarkBitmap 48))
Write-Host "Generated: $icoPath"
Write-Host "Generated: $pngPath"
