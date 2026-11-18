<#
.SYNOPSIS
  Stops the Kindred services, the frontend and the Docker infrastructure.

.EXAMPLE
  ./scripts/stop-dev.ps1          # stop everything, keep the data
  ./scripts/stop-dev.ps1 -Wipe    # also delete the databases, queues and the knowledge graph
#>
param(
    [switch]$Wipe
)

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Whatever listens on Kindred's ports: gateway, student-service, matching-service, frontend.
foreach ($port in 5000, 5101, 5102, 4200) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object {
            Stop-Process -Id $_ -Force -Confirm:$false -ErrorAction SilentlyContinue
            Write-Host "  stopped the process on :$port"
        }
}

$down = @('compose', '-f', 'docker-compose.dev.yml', '--profile', 'kg', 'down')
if ($Wipe) { $down += '-v' }
& docker @down

$suffix = if ($Wipe) { ' All data wiped.' } else { '' }
Write-Host "Kindred stopped.$suffix" -ForegroundColor Green
