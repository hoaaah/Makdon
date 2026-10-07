<#
  Membuat Assets\app.ico (ikon MdViewer) dengan System.Drawing. Tidak dijalankan saat build;
  jalankan manual bila ikon ingin diubah:  powershell -File scripts\generate-icon.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\MdViewer\Assets\app.ico')
)

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Kotak membulat dengan gradasi biru.
    $m = [single]($size * 0.04)
    $w = [single]($size - 2 * $m)
    $r = [single]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 59, 130, 246)),
        ([System.Drawing.Color]::FromArgb(255, 29, 78, 216)), 90
    $g.FillPath($brush, $path)

    # Huruf "M" putih dan panah ke bawah (lambang Markdown).
    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.42)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = [System.Drawing.Brushes]::White
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF ([single]($size * 0.06)), 0, ([single]($size * 0.62)), $size
    $g.DrawString('M', $font, $white, $textRect, $fmt)

    $ax = $size * 0.78; $top = $size * 0.34; $bot = $size * 0.66; $half = $size * 0.11; $shaft = $size * 0.045
    $pts = @(
        (New-Object System.Drawing.PointF ([single]($ax - $shaft)), ([single]$top)),
        (New-Object System.Drawing.PointF ([single]($ax + $shaft)), ([single]$top)),
        (New-Object System.Drawing.PointF ([single]($ax + $shaft)), ([single]($bot - $half))),
        (New-Object System.Drawing.PointF ([single]($ax + $half)), ([single]($bot - $half))),
        (New-Object System.Drawing.PointF ([single]$ax), ([single]$bot)),
        (New-Object System.Drawing.PointF ([single]($ax - $half)), ([single]($bot - $half))),
        (New-Object System.Drawing.PointF ([single]($ax - $shaft)), ([single]($bot - $half)))
    )
    $g.FillPolygon($white, [System.Drawing.PointF[]]$pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (New-IconPng $_) }

$dir = Split-Path -Parent ([System.IO.Path]::GetFullPath($Output))
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Output), $out.ToArray())
Write-Host "Ikon dibuat: $([System.IO.Path]::GetFullPath($Output))"
