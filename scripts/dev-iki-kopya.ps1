# Geliştirme testi: aynı makinede iki LanBeam kopyası açar.
# Kopyalar farklı veri klasörü ve farklı TCP portu kullanır; birbirlerini keşfedip
# aralarında transfer yapabilirler (localhost üzerinden).
#
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\dev-iki-kopya.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\LanBeam.App\bin\Debug\net8.0-windows\LanBeam.App.exe"

if (-not (Test-Path $exe)) {
    Write-Host "Önce derleyin: dotnet build src/LanBeam.App" -ForegroundColor Yellow
    exit 1
}

$dirA = Join-Path $env:TEMP "LanBeam-DevA"
$dirB = Join-Path $env:TEMP "LanBeam-DevB"
New-Item -ItemType Directory -Force $dirA | Out-Null
New-Item -ItemType Directory -Force $dirB | Out-Null

# Farklı TCP portları ve ayırt edici adlar (ilk açılıştan önce ayarı yaz)
$settingsA = Join-Path $dirA "settings.json"
$settingsB = Join-Path $dirB "settings.json"
if (-not (Test-Path $settingsA)) {
    @{ DeviceId = [guid]::NewGuid().ToString("N"); DeviceName = "Test-A"; TcpPort = 45655 } |
        ConvertTo-Json | Out-File $settingsA -Encoding utf8
}
if (-not (Test-Path $settingsB)) {
    @{ DeviceId = [guid]::NewGuid().ToString("N"); DeviceName = "Test-B"; TcpPort = 45656 } |
        ConvertTo-Json | Out-File $settingsB -Encoding utf8
}

Start-Process $exe -ArgumentList "--datadir `"$dirA`""
Start-Sleep -Seconds 1
Start-Process $exe -ArgumentList "--datadir `"$dirB`""

Write-Host "İki kopya açıldı (Test-A ve Test-B). Birinden diğerine dosya göndererek deneyin." -ForegroundColor Green
