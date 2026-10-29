# FoxFix'i yayin klasorune cikarir: FoxFix.exe (tek dosya) ve yanina FoxDPI.exe.
#
# Kullanim:  powershell -ExecutionPolicy Bypass -File tools\yayinla.ps1 [-Out publish]
#
# FoxDPI.exe, FoxFix.exe'yi "--sadece-foxdpi" ile baslatan kucuk bir programdir. .NET Framework ile derlenir
# (Windows'ta hazir gelir, ek kurulum gerekmez) ve acilirken UAC sorar.

param([string]$Out = "publish")

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet publish src\FoxFix.App -c Release -p:PublishSingleFile=true -o $Out
if ($LASTEXITCODE -ne 0) { throw "FoxFix yayinlanamadi." }

$csc = @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw ".NET Framework derleyicisi (csc.exe) bulunamadi." }

$launcher = Join-Path $root "tools\FoxDpiLauncher"
& $csc /nologo /target:winexe /optimize+ `
    "/out:$Out\FoxDPI.exe" `
    "/win32manifest:$launcher\app.manifest" `
    "/win32icon:$root\src\FoxFix.App\Assets\FoxDpi.ico" `
    /reference:System.Windows.Forms.dll `
    "$launcher\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "FoxDPI.exe derlenemedi." }

Get-Item "$Out\FoxFix.exe", "$Out\FoxDPI.exe" | ForEach-Object { "{0}: {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
