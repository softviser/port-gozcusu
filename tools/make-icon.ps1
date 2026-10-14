# Uygulama simgesini (src/PortGozcusu/Assets/app.ico) yeniden üretir.
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\PortGozcusu\Assets\app.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 64.0
    # Arka plan: koyu lacivert yuvarlak
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 15, 42, 74))
    $g.FillEllipse($bg, 1 * $s, 1 * $s, 62 * $s, 62 * $s)

    # Radar halkaları
    $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 56, 189, 248)), ([Math]::Max(1, 2.5 * $s))
    $g.DrawEllipse($ringPen, 12 * $s, 12 * $s, 40 * $s, 40 * $s)
    $g.DrawEllipse($ringPen, 22 * $s, 22 * $s, 20 * $s, 20 * $s)

    # Tarama ışını (pasta dilimi)
    $sweep = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 56, 189, 248))
    $g.FillPie($sweep, 6 * $s, 6 * $s, 52 * $s, 52 * $s, -90, 70)

    # Merkez ve "açık port" noktası
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillEllipse($white, 28.5 * $s, 28.5 * $s, 7 * $s, 7 * $s)
    $hit = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 250, 204, 21))
    $g.FillEllipse($hit, 40 * $s, 14 * $s, 9 * $s, 9 * $s)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-IconPng $sz) }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write([byte[]]$data) }
$bw.Close()
Write-Host "Simge oluşturuldu: $out"
