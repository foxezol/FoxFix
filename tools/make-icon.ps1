# FoxFix ve FoxDPI logolarini, simgelerini kaynak cizimlerden uretir.
#
# Kullanim:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Girdi:     src\FoxFix.App\Assets\source\fox-gear.png    (FoxFix: tilki + disli, siyah cizgi, saydam zemin)
#            src\FoxFix.App\Assets\source\fox-globe.png   (FoxDPI: tilki + kure, siyah cizgi, saydam zemin)
# Cikti:     Assets\FoxLogo.png      + Assets\FoxFix.ico   (FoxFix)
#            Assets\FoxDpiLogo.png   + Assets\FoxDpi.ico   (FoxDPI)
#
# Siyah cizgiler tema rengine boyanir; beyaz ya da saydam alan saydam kalir. Simgeler tema renginde yuvarlak karo
# uzerinde koyu tilkidir. Kucuk simgelerde ince cizgiler kaybolmasin diye cizim boyuta gore hafifce kalinlastirilir.

param(
    [string]$Color = "9E20E6",
    [string]$Ink = "16181D"
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class FoxArt
{
    // Alpha = kaynak alpha * (1 - parlaklik). Siyah opak => opak, beyaz opak => saydam, saydam => saydam.
    public static Bitmap Tint(Bitmap src, int r, int g, int b)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var s = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var d = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[s.Stride * s.Height];
        Marshal.Copy(s.Scan0, buf, 0, buf.Length);
        for (int i = 0; i < buf.Length; i += 4)
        {
            double lum = (0.2126 * buf[i + 2] + 0.7152 * buf[i + 1] + 0.0722 * buf[i]) / 255.0;
            double a = buf[i + 3] * (1.0 - lum);
            buf[i] = (byte)b; buf[i + 1] = (byte)g; buf[i + 2] = (byte)r; buf[i + 3] = (byte)Math.Round(a);
        }
        Marshal.Copy(buf, 0, d.Scan0, buf.Length);
        src.UnlockBits(s);
        dst.UnlockBits(d);
        return dst;
    }

    public static Rectangle Bounds(Bitmap bmp, int alphaThreshold)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(data);
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (buf[y * data.Stride + x * 4 + 3] > alphaThreshold)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
"@ -ReferencedAssemblies System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "src\FoxFix.App\Assets"

function Parse-Hex([string]$hex) {
    return @([Convert]::ToInt32($hex.Substring(0, 2), 16), [Convert]::ToInt32($hex.Substring(2, 2), 16), [Convert]::ToInt32($hex.Substring(4, 2), 16))
}
$accent = Parse-Hex $Color
$inkRgb = Parse-Hex $Ink

function Resize-Bitmap($bmp, [int]$w, [int]$h) {
    # Buyuk kaynaktan tek seferde kucultmek tirtikli olur; yari yariya adim adim kucultulur.
    $current = $bmp
    $own = $false
    while ($current.Width / 2 -gt $w -and $current.Height / 2 -gt $h) {
        $step = New-Object System.Drawing.Bitmap ([int]($current.Width / 2)), ([int]($current.Height / 2)), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $gx = [System.Drawing.Graphics]::FromImage($step)
        $gx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $gx.Clear([System.Drawing.Color]::Transparent)
        $gx.DrawImage($current, 0, 0, $step.Width, $step.Height)
        $gx.Dispose()
        if ($own) { $current.Dispose() }
        $current = $step
        $own = $true
    }
    $out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($out)
    $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gfx.Clear([System.Drawing.Color]::Transparent)
    $gfx.DrawImage($current, 0, 0, $w, $h)
    $gfx.Dispose()
    if ($own) { $current.Dispose() }
    return $out
}

# Cizgileri kaynakta biraz kalinlastirir: ayni resmi 8 yone kaydirip ust uste basar.
function Get-Bold($bmp, [double]$fraction) {
    $r = [math]::Max(1, [int][math]::Round($bmp.Width * $fraction))
    $out = New-Object System.Drawing.Bitmap $bmp.Width, $bmp.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($out)
    $gfx.Clear([System.Drawing.Color]::Transparent)
    foreach ($o in @(@(0, 0), @($r, 0), @(-$r, 0), @(0, $r), @(0, -$r), @($r, $r), @(-$r, -$r), @($r, -$r), @(-$r, $r))) {
        $gfx.DrawImage($bmp, $o[0], $o[1], $bmp.Width, $bmp.Height)
    }
    $gfx.Dispose()
    return $out
}

function New-IconPng($croppedInk, [int]$size) {
    $boldFraction = if ($size -le 20) { 0.011 } elseif ($size -le 32) { 0.008 } elseif ($size -le 48) { 0.004 } else { 0 }
    $art = if ($boldFraction -gt 0) { Get-Bold $croppedInk $boldFraction } else { $croppedInk }

    $fill = if ($size -le 32) { 0.84 } else { 0.76 }
    $inner = $size * $fill
    $scale = [math]::Min($inner / $art.Width, $inner / $art.Height)
    $w = [int][math]::Max(1, [math]::Round($art.Width * $scale))
    $h = [int][math]::Max(1, [math]::Round($art.Height * $scale))
    $small = Resize-Bitmap $art $w $h
    if ($boldFraction -gt 0) { $art.Dispose() }

    $canvas = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($canvas)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.Clear([System.Drawing.Color]::Transparent)

    $radius = [single]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $accent[0], $accent[1], $accent[2]))
    $gfx.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()

    $gfx.DrawImage($small, [int][math]::Floor(($size - $w) / 2), [int][math]::Floor(($size - $h) / 2), $w, $h)
    $gfx.Dispose()
    $small.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $canvas.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    return , $ms.ToArray()
}

function Build-Set([string]$sourceName, [string]$logoName, [string]$icoName) {
    $source = Join-Path $assets "source\$sourceName"
    if (-not (Test-Path $source)) { throw "Kaynak gorsel bulunamadi: $source" }

    $srcBmp = [System.Drawing.Bitmap]::FromFile($source)
    $tinted = [FoxArt]::Tint($srcBmp, $accent[0], $accent[1], $accent[2])
    $inked = [FoxArt]::Tint($srcBmp, $inkRgb[0], $inkRgb[1], $inkRgb[2])
    $srcBmp.Dispose()

    $box = [FoxArt]::Bounds($tinted, 16)
    if ($box.IsEmpty) { throw "Gorselde cizgi bulunamadi: $sourceName" }
    $cropped = $tinted.Clone($box, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $croppedInk = $inked.Clone($box, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $tinted.Dispose(); $inked.Dispose()

    # Uygulama ici logo: kirpilmis, 512 px yukseklik
    $logoH = 512
    $logoW = [int][math]::Round($cropped.Width * $logoH / $cropped.Height)
    $logo = Resize-Bitmap $cropped $logoW $logoH
    $logoFile = Join-Path $assets $logoName
    $logo.Save($logoFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $logo.Dispose()
    "Yazildi: $logoFile ($logoW x $logoH)"

    # Simge: tema renginde karo uzerinde koyu tilki
    $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
    $images = foreach ($size in $sizes) { , (New-IconPng $croppedInk $size) }

    # ICO: 6 byte baslik + 16 byte/girdi + PNG verileri (Vista ve sonrasi PNG'li simgeleri destekler)
    $stream = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $stream
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($png in $images) { $w.Write($png) }
    $w.Flush()

    $icoFile = Join-Path $assets $icoName
    [System.IO.File]::WriteAllBytes($icoFile, $stream.ToArray())
    "Yazildi: $icoFile ($([math]::Round((Get-Item $icoFile).Length / 1KB, 1)) KB, $($sizes.Count) boyut)"

    $cropped.Dispose(); $croppedInk.Dispose()
}

Build-Set "fox-gear.png" "FoxLogo.png" "FoxFix.ico"
Build-Set "fox-globe.png" "FoxDpiLogo.png" "FoxDpi.ico"
