# FoxFix'i paketler: dist\FoxFix-<surum>-tasinabilir.zip ve (Inno Setup kuruluysa) dist\FoxFixSetup.exe.
#
# Kullanim:  powershell -ExecutionPolicy Bypass -File tools\paketle.ps1 [-Surum 0.7.0]
#
# ZIP "tasinabilir" calisir: FoxFix.portable dosyasi yuzunden butun veriler exe'nin yanindaki Veri klasorune yazilir.

param([string]$Surum = "")

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Surum) {
    $Surum = ([xml](Get-Content src\FoxFix.App\FoxFix.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$stage = Join-Path $root "dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

& powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\yayinla.ps1" -Out $stage
if ($LASTEXITCODE -ne 0) { throw "Yayinlama basarisiz." }

# ZIP
$zipDir = Join-Path $root "dist\zip"
Remove-Item $zipDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$zipDir\FoxFix" | Out-Null
Copy-Item "$stage\FoxFix.exe", "$stage\FoxDPI.exe" "$zipDir\FoxFix"
Copy-Item "$PSScriptRoot\installer\FoxFix.portable" "$zipDir\FoxFix"
Copy-Item "$root\README.md" "$zipDir\FoxFix"
New-Item -ItemType Directory -Force "$zipDir\FoxFix\Licenses" | Out-Null
Copy-Item "$root\src\FoxFix.App\Assets\GoodbyeDpi\LICENSE-*.txt", "$root\src\FoxFix.App\Assets\Zapret\LICENSE-*.txt" "$zipDir\FoxFix\Licenses"
$zip = Join-Path $root "dist\FoxFix-$Surum-tasinabilir.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$zipDir\FoxFix" -DestinationPath $zip
"ZIP: $zip"

# Kurulum (Inno Setup)
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc "/DAppVersion=$Surum" "/DSourceDir=$stage" "$PSScriptRoot\installer\FoxFix.iss"
    if ($LASTEXITCODE -ne 0) { throw "Kurulum paketi derlenemedi." }
    "Kurulum: $(Join-Path $root 'dist\FoxFixSetup.exe')"
} else {
    Write-Warning "Inno Setup 6 kurulu degil: FoxFixSetup.exe uretilmedi (https://jrsoftware.org/isinfo.php)."
}