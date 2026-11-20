<#
.SYNOPSIS
  Stops Kindred however it was started: the containers, and the services and frontend that
  scripts/start-dev.ps1 runs on the host.

.EXAMPLE
  ./scripts/stop-dev.ps1          # stop everything, keep the data
  ./scripts/stop-dev.ps1 -Wipe    # also delete the databases, queues and the knowledge graph
#>
param(
    [switch]$Wipe
)

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Containers first, so the only listeners left on Kindred's ports are host processes.
$down = @('compose', '-f', 'docker-compose.dev.yml', '--profile', 'kg', '--profile', 'app', 'down')
if ($Wipe) { $down += '-v' }
& docker @down

# Host mode: gateway, student-service, matching-service, frontend. Only ever dotnet or node -
# never whatever else holds the port (Docker publishes container ports from its own process).
foreach ($port in 11000, 11101, 11102, 11200) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } |
        Where-Object { $_.ProcessName -match '^(dotnet|node|Kindred\.)' } |
        ForEach-Object {
            Stop-Process -Id $_.Id -Force -Confirm:$false -ErrorAction SilentlyContinue
            Write-Host "  stopped the process on :$port"
        }
}

$suffix = if ($Wipe) { ' All data wiped.' } else { '' }
Write-Host "Kindred stopped.$suffix" -ForegroundColor Green
