# FoxFix gercek sistem testini yonetici olarak baslatir.
# Normal bir terminalden calistir, Windows bir UAC penceresi acar, "Evet" de.
#
#   powershell -ExecutionPolicy Bypass -File tools\gercek-sistem-testi-yonetici.ps1
#
# Yonetici degilse kendini UAC ile yeniden baslatir. Sonuc penceresi acik kalir,
# sonuc ayrica %TEMP%\foxfix-gercek-test.txt dosyasina yazilir.

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Bir UAC penceresi acilacak. 'Evet' de, test yeni bir yonetici penceresinde calisacak."
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`""
    )
    exit
}

$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$machinePath;$userPath"

$log = Join-Path $env:TEMP "foxfix-gercek-test.txt"
Write-Host "FoxFix gercek sistem testi basliyor. Gerekirse derleme birkac saniye surer."
Write-Host ""

dotnet run (Join-Path $root "tools\gercek-sistem-testi.cs") -- $log

Write-Host ""
Write-Host "Sonuc dosyasi: $log"
Read-Host "Pencereyi kapatmak icin Enter'a bas"
