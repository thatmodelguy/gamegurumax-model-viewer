$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\GameGuruMax.ModelViewer\Assets"
$src = Join-Path $assets "app-icon-source.jpg"
$icoOut = Join-Path $assets "app.ico"
$pngOut = Join-Path $assets "app-icon-256.png"

$img = [System.Drawing.Image]::FromFile($src)
$bmp256 = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp256)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::FromArgb(255, 10, 12, 16))
$g.DrawImage($img, 0, 0, 256, 256)
$g.Dispose()
$img.Dispose()
$bmp256.Save($pngOut, [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 32, 48, 64, 128, 256)
$imageDatas = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $frame = New-Object System.Drawing.Bitmap $s, $s
    $gf = [System.Drawing.Graphics]::FromImage($frame)
    $gf.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gf.Clear([System.Drawing.Color]::FromArgb(255, 10, 12, 16))
    $gf.DrawImage($bmp256, 0, 0, $s, $s)
    $gf.Dispose()
    $pms = New-Object System.IO.MemoryStream
    $frame.Save($pms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frame.Dispose()
    $imageDatas.Add($pms.ToArray())
    $pms.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $imageDatas[$i]
    $dim = 0
    if ($s -lt 256) { $dim = $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $imageDatas) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoOut, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$bmp256.Dispose()
Write-Host "Wrote $icoOut ($((Get-Item $icoOut).Length) bytes)"
