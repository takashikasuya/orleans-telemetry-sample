param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$RootPosix = $Root -replace "\\", "/"
$reportDir = if ($env:TELEMETRY_E2E_REPORT_DIR) { $env:TELEMETRY_E2E_REPORT_DIR } else { Join-Path $Root "reports" }
$storageDir = Join-Path $Root "storage"
$storageDirPosix = $storageDir -replace "\\", "/"
$stateFile = Join-Path $Root "scripts/.system-state"
$dockerfile = Join-Path $Root "Dockerfile.dotnet"
$dockerfilePosix = $dockerfile -replace "\\", "/"
$seedFile = Join-Path $Root "src/Telemetry.E2E.Tests/seed-complex.ttl"
$seedFilePosix = $seedFile -replace "\\", "/"

New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
New-Item -ItemType Directory -Force -Path $storageDir | Out-Null

if (Test-Path $stateFile) {
  Remove-Item -Force $stateFile
}

$tempDir = Join-Path ([IO.Path]::GetTempPath()) ("telemetry-system-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$overrideFile = Join-Path $tempDir "docker-compose.override.yml"

@"
services:
  silo:
    build:
      context: $RootPosix
      dockerfile: $dockerfilePosix
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
      - "${storageDirPosix}:/storage"
      - "${seedFilePosix}:/seed/seed.ttl:ro"
  api:
    build:
      context: $RootPosix
      dockerfile: $dockerfilePosix
      args:
        PROJECT: src/ApiGateway
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      OIDC_AUTHORITY: http://localhost:8081/default
      OIDC_AUDIENCE: default
      TelemetryStorage__StagePath: /storage/stage
      TelemetryStorage__ParquetPath: /storage/parquet
      TelemetryStorage__IndexPath: /storage/index
      Orleans__GatewayHost: silo
      Orleans__GatewayPort: "30000"
      ASPNETCORE_URLS: http://+:80
    volumes:
      - "${storageDirPosix}:/storage"
    extra_hosts:
      - "localhost:host-gateway"
  admin:
    build:
      context: $RootPosix
      dockerfile: $dockerfilePosix
      args:
        PROJECT: src/AdminGateway
    environment:
      OIDC_AUTHORITY: http://localhost:8081/default
      OIDC_AUDIENCE: default
    extra_hosts:
      - "localhost:host-gateway"
"@ | Set-Content -Path $overrideFile -Encoding UTF8

Write-Host "Starting system..."
& docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile down --remove-orphans
& docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile up --build -d mq silo api admin mock-oidc

@"
TEMP_DIR=$tempDir
OVERRIDE_FILE=$overrideFile
"@ | Set-Content -Path $stateFile -Encoding UTF8

Write-Host "System started."
Write-Host "Swagger: http://localhost:8080/swagger"
Write-Host "Admin UI: http://localhost:8082/"
Write-Host "Mock OIDC: http://localhost:8081/default"
Write-Host "Storage dir: $storageDir"
Write-Host "Reports dir: $reportDir"
Write-Host "Override file: $overrideFile"
Write-Host "State file: $stateFile"
