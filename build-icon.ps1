param()

<#
.SYNOPSIS
    Gera o ícone multi-tamanho do aplicativo (src\assets\claude-chat-backup.ico)
    a partir de src\assets\512-claude-chat-backup.png.
#>

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$src = Join-Path $root 'src\assets'
$tmp = Join-Path $env:LOCALAPPDATA 'Temp\claude-chat-backup\ico-build'

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Path $tmp | Out-Null

$source = Join-Path $src '512-claude-chat-backup.png'
$big = [System.Drawing.Image]::FromFile($source)

$sizes = 16, 24, 32, 48, 64, 256
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($big, 0, 0, $s, $s)
    $g.Dispose()
    $bmp.Save((Join-Path $tmp "$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$big.Dispose()

$out = Join-Path $src 'claude-chat-backup.ico'
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter($fs)
try {
    $w.Write([UInt16]0)
    $w.Write([UInt16]1)
    $w.Write([UInt16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    foreach ($s in $sizes) {
        $data = [System.IO.File]::ReadAllBytes((Join-Path $tmp "$s.png"))
        $dim = if ($s -eq 256) { 0 } else { $s }
        $w.Write([Byte]$dim)
        $w.Write([Byte]$dim)
        $w.Write([Byte]0)
        $w.Write([Byte]0)
        $w.Write([UInt16]1)
        $w.Write([UInt16]32)
        $w.Write([UInt32]$data.Length)
        $w.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($s in $sizes) {
        $data = [System.IO.File]::ReadAllBytes((Join-Path $tmp "$s.png"))
        $w.Write($data)
    }
}
finally {
    $w.Close()
    $fs.Close()
}

Remove-Item $tmp -Recurse -Force
Write-Host "Ícone gerado: $out ($([math]::Round((Get-Item $out).Length / 1KB, 1)) KB)"
