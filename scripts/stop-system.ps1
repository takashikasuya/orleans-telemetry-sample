param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stateFile = Join-Path $Root "scripts/.system-state"

$tempDir = ""
$overrideFile = ""

if (Test-Path $stateFile) {
  foreach ($line in Get-Content $stateFile) {
    if ($line -match "^(.*?)=(.*)$") {
      switch ($matches[1]) {
        "TEMP_DIR" { $tempDir = $matches[2] }
        "OVERRIDE_FILE" { $overrideFile = $matches[2] }
      }
    }
  }
}

if ($overrideFile -and (Test-Path $overrideFile)) {
  Write-Host "Stopping system with override: $overrideFile"
  & docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile down --remove-orphans
} else {
  Write-Host "Stopping system with base compose (override not found)"
  & docker compose -f (Join-Path $Root "docker-compose.yml") down --remove-orphans
}

if ($tempDir -and (Test-Path $tempDir)) {
  Remove-Item -Recurse -Force $tempDir
}

if (Test-Path $stateFile) {
  Remove-Item -Force $stateFile
}

Write-Host "System stopped."
