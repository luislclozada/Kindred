<#
.SYNOPSIS
  Starts Kindred for development: infrastructure in Docker, then each service in its own window.

.DESCRIPTION
  Order matters: matching-service starts BEFORE student-service, because the consumer declares
  its queue and student-service publishes enrollment events while it seeds. Publishers only
  declare the exchange, so events published before the queue exists are lost.

.EXAMPLE
  ./scripts/start-dev.ps1            # postgres + rabbitmq + services + frontend
  ./scripts/start-dev.ps1 -Kg        # ... plus Neo4j for the knowledge graph
  ./scripts/start-dev.ps1 -NoFrontend
#>
param(
    [switch]$Kg,
    [switch]$NoFrontend
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Test-Port([int]$port) {
    [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Wait-Port([int]$port, [string]$name, [int]$seconds = 60) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while (-not (Test-Port $port)) {
        if ((Get-Date) -gt $deadline) { throw "$name did not start listening on port $port within $seconds s." }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "  $name is up on :$port" -ForegroundColor Green
}

function Start-Window([string]$title, [string]$command) {
    $script = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location '$root'; $command"
    Start-Process powershell -ArgumentList '-NoExit', '-NoProfile', '-Command', $script | Out-Null
}

# ── 1. Pre-flight: every port Kindred needs must be free (or already held by Kindred) ──────────
$infraPorts = @(5432, 5672, 15672)
if ($Kg) { $infraPorts += 7474, 7687 }
$conflicts = @()
foreach ($port in $infraPorts) {
    if (-not (Test-Port $port)) { continue }
    $holders = @(docker ps --filter "publish=$port" --format '{{.Names}}')
    if ($holders | Where-Object { $_ -like 'kindred-*' }) { continue }
    $who = if ($holders) { "container $($holders -join ', ')" } else { 'another process' }
    $conflicts += "  port $port is in use by $who"
}
foreach ($port in 5000, 5101, 5102) {
    if (Test-Port $port) { $conflicts += "  port $port is in use (a Kindred service still running? ./scripts/stop-dev.ps1)" }
}
if ($conflicts) {
    Write-Host "Cannot start - ports are taken:" -ForegroundColor Red
    $conflicts | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

# ── 2. Infrastructure ──────────────────────────────────────────────────────────────────────────
Write-Host "Starting infrastructure..." -ForegroundColor Cyan
$compose = @('compose', '-f', 'docker-compose.dev.yml')
if ($Kg) { $compose += '--profile', 'kg' }
& docker @compose up -d --wait --quiet-pull
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed.' }

# ── 3. Build once, then run each service without rebuilding ────────────────────────────────────
Write-Host "Building..." -ForegroundColor Cyan
dotnet build Kindred.slnx -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host "Starting services..." -ForegroundColor Cyan
Start-Window 'kindred: matching-service' 'dotnet run --no-build --project src/Services/MatchingService/Kindred.MatchingService.Api'
Wait-Port 5102 'matching-service'
Start-Sleep -Seconds 2   # let the consumer declare and bind its queue before anything publishes

Start-Window 'kindred: student-service' 'dotnet run --no-build --project src/Services/StudentService/Kindred.StudentService.Api'
Wait-Port 5101 'student-service'

Start-Window 'kindred: gateway' 'dotnet run --no-build --project src/Gateway/Kindred.Gateway'
Wait-Port 5000 'gateway'

# ── 4. Frontend ────────────────────────────────────────────────────────────────────────────────
if (-not $NoFrontend -and (Test-Path 'apps/frontend/package.json')) {
    if (Test-Port 4200) {
        Write-Host "  frontend already running on :4200" -ForegroundColor Yellow
    } else {
        Start-Window 'kindred: frontend' "Set-Location 'apps/frontend'; npm start"
        Wait-Port 4200 'frontend' 120
    }
}

Write-Host ""
Write-Host "Kindred is running." -ForegroundColor Green
Write-Host "  App        http://localhost:4200"
Write-Host "  Gateway    http://localhost:5000/api/v1/courses"
Write-Host "  RabbitMQ   http://localhost:15672   (kindred / kindred_dev)"
if ($Kg) { Write-Host "  Neo4j      http://localhost:7474    (neo4j / kindred_kg_dev)" }
Write-Host "Stop with ./scripts/stop-dev.ps1 (add -Wipe to also delete all data)."
