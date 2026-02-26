# deploy.ps1 — Build, publish, and deploy to OrangePi (Nginx architecture)
# Usage:
#   .\deploy.ps1          — build + upload + restart
#   .\deploy.ps1 -Setup   — also install Nginx + native deps (first time only)
#
# One-time SSH key setup (run once, then no more password prompts):
#   ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\id_orangepi" -N ""
#   type "$env:USERPROFILE\.ssh\id_orangepi.pub" | ssh root@192.168.254.53 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"

param(
    [switch]$Setup
)

$OPI_HOST   = "192.168.254.53"
$OPI_USER   = "root"
$OPI_KEY    = "$env:USERPROFILE\.ssh\id_orangepi"
$API_PATH   = "/opt/accesscontroll"
$WEB_ROOT   = "/var/www/accesscontroll"
$SERVICE    = "accesscontroll"
$PUB_API    = "$PSScriptRoot\src\AccessControll.API\bin\Release\net10.0\linux-arm64\publish"
$PUB_BLAZOR = "$PSScriptRoot\publish\blazor"

# SSH/SCP wrappers using key auth
function Invoke-SSH { param([string]$Cmd) ssh -i "$OPI_KEY" -o StrictHostKeyChecking=no "${OPI_USER}@${OPI_HOST}" $Cmd }
function Invoke-SCP { param([string[]]$ScpArgs) scp -i "$OPI_KEY" -o StrictHostKeyChecking=no @ScpArgs }

# ── 1. Publish API (linux-arm64, self-contained, always fresh) ───────────
# Delete linux-arm64 obj cache so dotnet always recompiles with latest source
$armObj = "$PSScriptRoot\src\AccessControll.API\obj\Release\net10.0\linux-arm64"
if (Test-Path $armObj) { Remove-Item -Recurse -Force $armObj }

Write-Host "==> Publishing API (linux-arm64, Release)..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot\src\AccessControll.API\AccessControll.API.csproj" `
    /p:PublishProfile=OrangePi `
    -c Release

if ($LASTEXITCODE -ne 0) { Write-Host "API build failed." -ForegroundColor Red; exit 1 }

# ── 2. Publish Blazor WASM ────────────────────────────────────────────────
Write-Host "==> Publishing Blazor WASM..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot\src\AccessControll.Blazor\AccessControll.Blazor.csproj" `
    -c Release -o "$PUB_BLAZOR"

if ($LASTEXITCODE -ne 0) { Write-Host "Blazor build failed." -ForegroundColor Red; exit 1 }

# Production appsettings: remove ApiBaseUrl so Blazor uses HostEnvironment.BaseAddress (same origin via Nginx)
'{}' | Set-Content -Path "$PUB_BLAZOR\wwwroot\appsettings.json" -Encoding UTF8

# ── 3. First-time setup on OrangePi ──────────────────────────────────────
if ($Setup) {
    Write-Host "==> Setting up OrangePi (first time)..." -ForegroundColor Yellow

    # Install dependencies
    Invoke-SSH "apt-get update -qq && apt-get install -y nginx libgpiod2 libssl3 curl"

    # Install arduino-cli (for ESP8266 firmware flashing)
    Invoke-SSH "curl -fsSL https://raw.githubusercontent.com/arduino/arduino-cli/master/install.sh | BINDIR=/usr/local/bin sh && arduino-cli config init && arduino-cli config add board_manager.additional_urls https://arduino.esp8266.com/stable/package_esp8266com_index.json && arduino-cli core update-index && arduino-cli core install esp8266:esp8266"

    # Create directories
    Invoke-SSH "mkdir -p $API_PATH $WEB_ROOT"

    # Write systemd service file with LF line endings (avoids CRLF bash errors)
    $serviceContent = "[Unit]`nDescription=AccessControll API`nAfter=network.target`n`n[Service]`nWorkingDirectory=$API_PATH`nExecStart=$API_PATH/AccessControll.API`nRestart=always`nUser=root`nEnvironment=ASPNETCORE_ENVIRONMENT=Production`nEnvironment=ASPNETCORE_URLS=http://127.0.0.1:5000`n`n[Install]`nWantedBy=multi-user.target`n"
    $tempService = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tempService, $serviceContent, [System.Text.Encoding]::UTF8)
    Invoke-SCP $tempService,"${OPI_USER}@${OPI_HOST}:/etc/systemd/system/$SERVICE.service"
    Remove-Item $tempService

    Invoke-SSH "systemctl daemon-reload && systemctl enable $SERVICE"
}

# ── 4. Stop service, pack as tar, upload, extract ────────────────────────
Write-Host "==> Stopping API service..." -ForegroundColor Cyan
Invoke-SSH "systemctl stop $SERVICE 2>/dev/null; sleep 1; true"

Write-Host "==> Packing API as tar (excluding local DB)..." -ForegroundColor Cyan
$tarFile = "$env:TEMP\accesscontroll-api.tar"
Push-Location $PUB_API
tar -cf $tarFile --exclude="./app.db" --exclude="./app.db-shm" --exclude="./app.db-wal" .
Pop-Location
if ($LASTEXITCODE -ne 0) { Write-Host "tar failed." -ForegroundColor Red; exit 1 }

Write-Host "==> Uploading API tar to OrangePi..." -ForegroundColor Cyan
Invoke-SCP $tarFile,"${OPI_USER}@${OPI_HOST}:/tmp/api.tar"
if ($LASTEXITCODE -ne 0) { Write-Host "API upload failed." -ForegroundColor Red; exit 1 }
Remove-Item $tarFile

Write-Host "==> Extracting API on OrangePi..." -ForegroundColor Cyan
Invoke-SSH "mkdir -p $API_PATH && tar -xf /tmp/api.tar -C $API_PATH && rm /tmp/api.tar"
if ($LASTEXITCODE -ne 0) { Write-Host "API extract failed." -ForegroundColor Red; exit 1 }

# ── 5. Install arduino-cli on OrangePi (skip if already installed) ───────
Write-Host "==> Checking arduino-cli on OrangePi..." -ForegroundColor Cyan
Invoke-SSH "command -v arduino-cli || (curl -L https://downloads.arduino.cc/arduino-cli/arduino-cli_latest_Linux_ARM64.tar.gz | tar -xz -C /usr/local/bin arduino-cli && chmod +x /usr/local/bin/arduino-cli && arduino-cli config init && arduino-cli config add board_manager.additional_urls https://arduino.esp8266.com/stable/package_esp8266com_index.json && arduino-cli core update-index && arduino-cli core install esp8266:esp8266)"
Invoke-SSH "arduino-cli lib install WebSockets ArduinoJson micro-ecc 'Adafruit GFX Library' 'Adafruit SSD1306'"

# ── 6. Upload Arduino sketch ──────────────────────────────────────────────
Write-Host "==> Uploading Arduino sketch to OrangePi..." -ForegroundColor Cyan
Invoke-SSH "mkdir -p $API_PATH/sketch"
Invoke-SCP "-r","$PSScriptRoot\src\AccessControll.Station\sketch_feb19b","${OPI_USER}@${OPI_HOST}:${API_PATH}/sketch/"
if ($LASTEXITCODE -ne 0) { Write-Host "Sketch upload failed." -ForegroundColor Red; exit 1 }

# ── 6. Upload Blazor static files ─────────────────────────────────────────
Write-Host "==> Uploading Blazor to Nginx web root..." -ForegroundColor Cyan
Invoke-SCP "-r","$PUB_BLAZOR\wwwroot\*","${OPI_USER}@${OPI_HOST}:${WEB_ROOT}/"
if ($LASTEXITCODE -ne 0) { Write-Host "Blazor upload failed." -ForegroundColor Red; exit 1 }

# ── 7. Deploy Nginx config ────────────────────────────────────────────────
Write-Host "==> Deploying Nginx config..." -ForegroundColor Cyan
Invoke-SCP "$PSScriptRoot\nginx.conf","${OPI_USER}@${OPI_HOST}:/etc/nginx/sites-available/accesscontroll"
Invoke-SSH "ln -sf /etc/nginx/sites-available/accesscontroll /etc/nginx/sites-enabled/accesscontroll && rm -f /etc/nginx/sites-enabled/default && nginx -t && systemctl reload nginx"

# ── 8. Restart API service ────────────────────────────────────────────────
Write-Host "==> Restarting API service..." -ForegroundColor Cyan
Invoke-SSH "chmod +x $API_PATH/AccessControll.API && systemctl restart $SERVICE && systemctl status $SERVICE --no-pager"

Write-Host "==> Done. Open http://${OPI_HOST}" -ForegroundColor Green
