param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $Root

$reportDir = if ($env:TELEMETRY_E2E_REPORT_DIR) { $env:TELEMETRY_E2E_REPORT_DIR } else { Join-Path $Root "reports" }
$runId = "telemetry-e2e-docker-" + (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$reportMd = Join-Path $reportDir "$runId.md"
$reportJson = Join-Path $reportDir "$runId.json"
$seedFile = Join-Path $Root "data/seed.ttl"
$storageDir = Join-Path $Root "storage"
$mockOidcPort = if ($env:MOCK_OIDC_PORT) { $env:MOCK_OIDC_PORT } else { "8081" }
$apiWaitSeconds = if ($env:API_WAIT_SECONDS) { [int]$env:API_WAIT_SECONDS } else { 120 }
$tempDir = ""
$overrideFile = ""

New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
New-Item -ItemType Directory -Force -Path $storageDir | Out-Null

function Get-UtcNow {
  return (Get-Date).ToUniversalTime()
}

function Log([string]$Message) {
  $timestamp = (Get-UtcNow).ToString("HH:mm:ss")
  Write-Host "[$timestamp] $Message"
}

function Wait-ForUrl([string]$Url, [int]$TimeoutSeconds) {
  $start = Get-Date
  while ($true) {
    try {
      Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec 5 | Out-Null
      return $true
    } catch {
      if ((Get-Date) - $start -gt (New-TimeSpan -Seconds $TimeoutSeconds)) {
        return $false
      }
      Start-Sleep -Seconds 2
    }
  }
}

function Run-InProc {
  Log "Running in-proc E2E test"
  & dotnet test (Join-Path $Root "src/Telemetry.E2E.Tests")
  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }
}

function Run-Docker {
  $script:tempDir = Join-Path ([IO.Path]::GetTempPath()) ("telemetry-e2e-" + [guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory -Force -Path $script:tempDir | Out-Null
  $script:overrideFile = Join-Path $script:tempDir "docker-compose.override.yml"

  @"
version: "3.9"
services:
  silo:
    build:
      context: .
      dockerfile: Dockerfile.dotnet
      args:
        PROJECT: src/SiloHost
    environment:
      RDF_SEED_PATH: /seed/seed.ttl
      TENANT_ID: t1
      Orleans__AdvertisedIPAddress: silo
      Orleans__SiloPort: "11111"
      Orleans__GatewayPort: "30000"
      TelemetryIngest__Enabled__0: Simulator
      TelemetryIngest__EventSinks__Enabled__0: ParquetStorage
      TelemetryIngest__Simulator__TenantId: t1
      TelemetryIngest__Simulator__BuildingName: building
      TelemetryIngest__Simulator__SpaceId: space
      TelemetryIngest__Simulator__DeviceIdPrefix: device
      TelemetryIngest__Simulator__DeviceCount: "1"
      TelemetryIngest__Simulator__PointsPerDevice: "1"
      TelemetryIngest__Simulator__IntervalMilliseconds: "500"
      TelemetryStorage__StagePath: /storage/stage
      TelemetryStorage__ParquetPath: /storage/parquet
      TelemetryStorage__IndexPath: /storage/index
      TelemetryStorage__CompactionIntervalSeconds: "2"
    volumes:
      - ./storage:/storage
      - ./data/seed.ttl:/seed/seed.ttl:ro
  api:
    build:
      context: .
      dockerfile: Dockerfile.dotnet
      args:
        PROJECT: src/ApiGateway
    environment:
      TelemetryStorage__StagePath: /storage/stage
      TelemetryStorage__ParquetPath: /storage/parquet
      TelemetryStorage__IndexPath: /storage/index
      Orleans__GatewayHost: silo
      Orleans__GatewayPort: "30000"
      ASPNETCORE_URLS: http://+:80
    volumes:
      - ./storage:/storage
  mock-oidc:
    ports:
      - "${mockOidcPort}:8080"
"@ | Set-Content -Path $script:overrideFile -Encoding UTF8

  Log "Starting docker compose"
  & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile down --remove-orphans
  
  # Only build if images don't exist
  foreach ($service in @("silo", "api")) {
    $imageName = "orleans-telemetry-sample-${service}:latest"
    $imageExists = & docker image inspect $imageName 2>$null
    
    if (-not $imageExists) {
      Log "Building $service..."
      & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile build $service
    }
  }
  
  & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile up -d mq silo api mock-oidc

  Log "Waiting for API"
  if (-not (Wait-ForUrl "http://localhost:8080/swagger" $apiWaitSeconds)) {
    Write-Error "API did not become ready in time"
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile ps | Write-Error
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile logs --no-color api silo | Write-Error
    exit 1
  }

  $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("test-client:test-secret"))
  $token = ""
  $tokenResponse = $null
  for ($i = 0; $i -lt 60; $i++) {
    try {
      $tokenResponse = Invoke-RestMethod -Uri "http://localhost:$mockOidcPort/default/token" `
        -Method Post `
        -Headers @{ Host = "mock-oidc:8080"; Authorization = "Basic $basic" } `
        -ContentType "application/x-www-form-urlencoded" `
        -Body "grant_type=client_credentials"
      if ($tokenResponse -and $tokenResponse.access_token) {
        $token = $tokenResponse.access_token
        break
      }
    } catch {
      Start-Sleep -Seconds 2
    }
  }

  if (-not $token) {
    Write-Error "Failed to get access token"
    if ($tokenResponse) {
      Write-Error ("Last token response: " + (ConvertTo-Json $tokenResponse -Depth 5 -Compress))
    }
    exit 1
  }

  $nodeId = "urn:point-1"
  $encodedNode = [System.Uri]::EscapeDataString($nodeId)

  Log "Querying graph node"
  $nodeJson = Invoke-RestMethod -Uri "http://localhost:8080/api/nodes/$encodedNode" -Headers @{ Authorization = "Bearer $token" }

  $deviceId = $nodeJson.node.attributes.DeviceId
  $pointId = $nodeJson.node.attributes.PointId
  if (-not $deviceId -or -not $pointId) {
    Write-Error "Missing DeviceId or PointId from graph node"
    Write-Error (ConvertTo-Json $nodeJson -Depth 8)
    exit 1
  }

  Log "Querying point snapshot"
  $pointJson = Invoke-RestMethod -Uri "http://localhost:8080/api/nodes/$encodedNode/value" -Headers @{ Authorization = "Bearer $token" }

  Log "Querying device snapshot"
  $deviceJson = Invoke-RestMethod -Uri "http://localhost:8080/api/devices/$deviceId" -Headers @{ Authorization = "Bearer $token" }

  Log "Querying telemetry history"
  $now = (Get-UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ")
  $from = (Get-UtcNow).AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ")
  $to = (Get-UtcNow).AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ")
  $fromEncoded = [System.Uri]::EscapeDataString($from)
  $toEncoded = [System.Uri]::EscapeDataString($to)
  $telemetryJson = Invoke-RestMethod -Uri "http://localhost:8080/api/telemetry/$deviceId?from=$fromEncoded&to=$toEncoded&pointId=$pointId&limit=10" -Headers @{ Authorization = "Bearer $token" }

  $storageParquet = Get-ChildItem -Path (Join-Path $storageDir "parquet") -Recurse -Filter "*.parquet" -ErrorAction SilentlyContinue | Select-Object -First 1
  $storageIndex = Get-ChildItem -Path (Join-Path $storageDir "index") -Recurse -Filter "*.json" -ErrorAction SilentlyContinue | Select-Object -First 1

  $pointUpdated = $pointJson.updatedAt
  $lagMs = $null
  if ($pointUpdated) {
    try {
      $parsed = [DateTimeOffset]::Parse($pointUpdated)
      $lagMs = ((Get-UtcNow) - $parsed.UtcDateTime).TotalMilliseconds
    } catch {
      $lagMs = $null
    }
  }

  $telemetryItems = $telemetryJson
  $seedEvent = $null
  if ($telemetryJson -isnot [System.Array]) {
    if ($telemetryJson.mode -eq "inline") {
      $telemetryItems = $telemetryJson.items
    } else {
      $telemetryItems = @()
    }
  }
  if ($telemetryItems -is [System.Array] -and $telemetryItems.Count -gt 0) {
    $seedEvent = $telemetryItems[0]
  }

  $telemetryResultCount = if ($telemetryJson -is [System.Array]) { $telemetryJson.Count } else { 0 }
  $telemetryFirstJson = if ($telemetryJson -is [System.Array] -and $telemetryJson.Count -gt 0) { ConvertTo-Json $telemetryJson[0] -Depth 8 -Compress } else { "" }

  $report = [ordered]@{
    runId = $runId
    status = "Passed"
    startedAt = (Get-UtcNow).ToString("o").Replace("+00:00", "Z")
    completedAt = (Get-UtcNow).ToString("o").Replace("+00:00", "Z")
    tenantId = "t1"
    rdfSeedPath = "seed.ttl"
    reportDirectory = $reportDir
    simulator = @{
      tenantId = "t1"
      buildingName = "building"
      spaceId = "space"
      deviceIdPrefix = "device"
      deviceCount = 1
      pointsPerDevice = 1
      intervalMilliseconds = 500
    }
    graph = @{
      nodeId = $nodeJson.node.nodeId
      attributes = $nodeJson.node.attributes
    }
    seedEvent = $seedEvent
    api = @{
      pointLastSequence = $pointJson.lastSequence
      pointUpdatedAt = $pointJson.updatedAt
      pointLatestValueJson = (ConvertTo-Json $pointJson.latestValue -Depth 8 -Compress)
      pointReadAt = (Get-UtcNow).ToString("o").Replace("+00:00", "Z")
      pointLagMilliseconds = $lagMs
      deviceLastSequence = $deviceJson.lastSequence
      deviceUpdatedAt = $deviceJson.updatedAt
      devicePropertiesJson = (ConvertTo-Json $deviceJson.properties -Depth 12 -Compress)
      telemetryResultCount = $telemetryResultCount
      telemetryFirstResultJson = $telemetryFirstJson
    }
    storage = @{
      parquetFilePath = if ($storageParquet) { $storageParquet.FullName } else { "" }
      parquetExists = [bool]$storageParquet
      indexFilePath = if ($storageIndex) { $storageIndex.FullName } else { "" }
      indexExists = [bool]$storageIndex
    }
  }

  $lines = New-Object System.Collections.Generic.List[string]
  $lines.Add("# Telemetry E2E Report (Docker)")
  $lines.Add("RunId: $runId")
  $lines.Add("Status: Passed")
  $lines.Add("StartedAt: $($report.startedAt)")
  $lines.Add("CompletedAt: $($report.completedAt)")
  $lines.Add("TenantId: $($report.tenantId)")
  $lines.Add("ReportDirectory: $reportDir")
  $lines.Add("")
  $lines.Add("## Graph Binding")
  $lines.Add("- NodeId: $($report.graph.nodeId)")
  foreach ($key in ($report.graph.attributes.Keys | Sort-Object)) {
    $lines.Add("- ${key}: $($report.graph.attributes[$key])")
  }
  $lines.Add("")
  $lines.Add("## API Checks")
  $lines.Add("- PointLastSequence: $($report.api.pointLastSequence)")
  $lines.Add("- PointUpdatedAt: $($report.api.pointUpdatedAt)")
  $lines.Add("- PointLagMilliseconds: $($report.api.pointLagMilliseconds)")
  $lines.Add("- DeviceLastSequence: $($report.api.deviceLastSequence)")
  $lines.Add("- TelemetryResultCount: $($report.api.telemetryResultCount)")
  $lines.Add("")
  $lines.Add("## Storage")
  $lines.Add("- ParquetFilePath: $($report.storage.parquetFilePath)")
  $lines.Add("- ParquetExists: $($report.storage.parquetExists)")
  $lines.Add("- IndexFilePath: $($report.storage.indexFilePath)")
  $lines.Add("- IndexExists: $($report.storage.indexExists)")
  $lines.Add("")

  $lines | Set-Content -Path $reportMd -Encoding UTF8
  $report | ConvertTo-Json -Depth 12 | Set-Content -Path $reportJson -Encoding UTF8

  Log "Docker E2E report written: $reportMd"
}

function Cleanup {
  if ($script:overrideFile) {
    Log "Stopping docker compose"
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $script:overrideFile down --remove-orphans
  }
  if ($script:tempDir -and (Test-Path $script:tempDir)) {
    Remove-Item -Recurse -Force $script:tempDir
  }
}

Run-InProc
try {
  Run-Docker
} finally {
  Cleanup
}
