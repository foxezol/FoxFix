# Kurulum sihirbazinin yan ve kucuk gorsellerini uygulamanin koyu temasiyla uretir (tools\installer\*.bmp).
# Calistirma: powershell -File tools\installer\make-images.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logo = [System.Drawing.Image]::FromFile("$root\src\FoxFix.App\Assets\FoxLogo.png")
$back = [System.Drawing.Color]::FromArgb(0x10, 0x13, 0x1A)
$ink = [System.Drawing.Color]::FromArgb(0xE9, 0xED, 0xF4)
$muted = [System.Drawing.Color]::FromArgb(0x9A, 0xA3, 0xB5)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear($back)
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Save-Side([int]$scale, [string]$name) {
    $w = 164 * $scale; $h = 314 * $scale
    $c = New-Canvas $w $h
    $lh = 150 * $scale; $lw = [int]($lh * $logo.Width / $logo.Height)
    $c.Graphics.DrawImage($logo, [int](($w - $lw) / 2), 70 * $scale, $lw, $lh)
    $font = New-Object System.Drawing.Font('Segoe UI Semibold', (17 * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $center = New-Object System.Drawing.StringFormat; $center.Alignment = 'Center'
    $c.Graphics.DrawString('FoxFix', $font, (New-Object System.Drawing.SolidBrush $ink), (New-Object System.Drawing.RectangleF 0, (236 * $scale), $w, (28 * $scale)), $center)
    $c.Bitmap.Save("$PSScriptRoot\$name", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

function Save-Small([int]$scale, [string]$name) {
    $s = 55 * $scale
    $c = New-Canvas $s $s
    $lh = 42 * $scale; $lw = [int]($lh * $logo.Width / $logo.Height)
    $c.Graphics.DrawImage($logo, [int](($s - $lw) / 2), [int](($s - $lh) / 2), $lw, $lh)
    $c.Bitmap.Save("$PSScriptRoot\$name", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

Save-Side 1 'wizard-side.bmp'
Save-Side 2 'wizard-side-2x.bmp'
Save-Small 1 'wizard-small.bmp'
Save-Small 2 'wizard-small-2x.bmp'
$logo.Dispose()
Get-ChildItem $PSScriptRoot -Filter *.bmp | ForEach-Object { "$($_.Name) $([int]($_.Length/1KB)) KB" }
