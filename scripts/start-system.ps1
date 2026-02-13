param(
  [switch]$Simulator,
  [switch]$RabbitMq,
  [switch]$Help
)

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
$seedFile = Join-Path $Root "data/seed-complex.ttl"
$seedFilePosix = $seedFile -replace "\\", "/"

if ($Help) {
  @"
Usage: .\scripts\start-system.ps1 [-Simulator] [-RabbitMq]

  -Simulator   Enable Simulator ingest connector.
  -RabbitMq    Enable RabbitMq ingest connector (also starts publisher).

If no options are provided, no ingest connectors are enabled.
"@ | Write-Host
  exit 0
}

New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
New-Item -ItemType Directory -Force -Path $storageDir | Out-Null

if (Test-Path $stateFile) {
  Remove-Item -Force $stateFile
}

$tempDir = Join-Path ([IO.Path]::GetTempPath()) ("telemetry-system-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$overrideFile = Join-Path $tempDir "docker-compose.override.yml"

$ingestEnabledLines = New-Object System.Collections.Generic.List[string]
$simulatorLines = ""
$rabbitMqLines = ""
$ingestSinkLine = ""
$mqBlock = ""
$publisherBlock = ""
$publisherService = ""

if ($Simulator) {
  $ingestEnabledLines.Add("      TelemetryIngest__Enabled__{0}: Simulator" -f $ingestEnabledLines.Count)
  $simulatorLines = @"
      TelemetryIngest__Simulator__TenantId: t1
      TelemetryIngest__Simulator__BuildingName: building
      TelemetryIngest__Simulator__SpaceId: space
      TelemetryIngest__Simulator__DeviceIdPrefix: device
      TelemetryIngest__Simulator__DeviceCount: "1"
      TelemetryIngest__Simulator__PointsPerDevice: "1"
      TelemetryIngest__Simulator__IntervalMilliseconds: "500"
"@
}

if ($RabbitMq) {
  $ingestEnabledLines.Add("      TelemetryIngest__Enabled__{0}: RabbitMq" -f $ingestEnabledLines.Count)
  $rabbitMqLines = @"
      TelemetryIngest__RabbitMq__HostName: mq
      TelemetryIngest__RabbitMq__Port: "5672"
      TelemetryIngest__RabbitMq__UserName: user
      TelemetryIngest__RabbitMq__Password: password
      TelemetryIngest__RabbitMq__QueueName: telemetry
      TelemetryIngest__RabbitMq__PrefetchCount: "100"
"@
  $mqBlock = @"
  mq:
    environment:
      RABBITMQ_DEFAULT_USER: user
      RABBITMQ_DEFAULT_PASS: password
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20
"@
  $publisherBlock = @"
  publisher:
    build:
      context: $RootPosix
      dockerfile: $dockerfilePosix
      args:
        PROJECT: src/Publisher
    depends_on:
      mq:
        condition: service_healthy
    restart: on-failure
    environment:
      RABBITMQ_HOST: mq
      RABBITMQ_USER: user
      RABBITMQ_PASS: password
      TENANT: t1
      RDF_SEED_PATH: /seed/seed.ttl
    volumes:
      - "${seedFilePosix}:/seed/seed.ttl:ro"
"@
  $publisherService = " publisher"
}

if ($Simulator -or $RabbitMq) {
  $ingestSinkLine = "      TelemetryIngest__EventSinks__Enabled__0: ParquetStorage"
}

$ingestEnabledText = if ($ingestEnabledLines.Count -gt 0) { ($ingestEnabledLines -join "`n") + "`n" } else { "" }
$simulatorText = if ($simulatorLines) { "$simulatorLines`n" } else { "" }
$rabbitMqText = if ($rabbitMqLines) { "$rabbitMqLines`n" } else { "" }
$ingestSinkText = if ($ingestSinkLine) { "$ingestSinkLine`n" } else { "" }

@"
services:
$mqBlock
  silo:
    depends_on:
      mq:
        condition: service_healthy
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
$ingestEnabledText$ingestSinkText$rabbitMqText$simulatorText
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
      OIDC_AUTHORITY: http://mock-oidc:8080/default
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
      OIDC_AUTHORITY: http://mock-oidc:8080/default
      OIDC_AUDIENCE: default
    extra_hosts:
      - "localhost:host-gateway"
$publisherBlock
"@ | Set-Content -Path $overrideFile -Encoding UTF8

Write-Host "Starting system..."
$MaxRetries = 3
$RetryCount = 0
$Success = $false

while ($RetryCount -lt $MaxRetries) {
  try {
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile down --remove-orphans 2>$null
    
    Write-Host "Building services (silo, api, admin$publisherService)..."
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile build silo api admin$publisherService

    Write-Host "Starting base services (mq, silo, mock-oidc)..."
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile up -d mq silo mock-oidc

    Write-Host "Waiting for Orleans gateway (localhost:30000)..."
    $GatewayRetries = 24
    $GatewayDelaySeconds = 2
    $GatewayReady = $false

    for ($i = 1; $i -le $GatewayRetries; $i++) {
      try {
        $tcpClient = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $tcpClient.ConnectAsync("localhost", 30000)
        if ($connectTask.Wait(1000) -and $tcpClient.Connected) {
          $GatewayReady = $true
          $tcpClient.Dispose()
          Write-Host "Orleans gateway is ready."
          break
        }
        $tcpClient.Dispose()
      }
      catch {
        # retry
      }

      Write-Host "Gateway not ready (attempt $i/$GatewayRetries). Retrying in $GatewayDelaySeconds seconds..."
      Start-Sleep -Seconds $GatewayDelaySeconds
    }

    if (-not $GatewayReady) {
      throw "Orleans gateway did not become ready after $GatewayRetries attempts."
    }

    Write-Host "Starting API/Admin/Publisher..."
    & docker compose -f (Join-Path $Root "docker-compose.yml") -f $overrideFile up -d api admin$publisherService
    
    $Success = $true
    break
  }
  catch {
    $RetryCount++
    Write-Warning "Build failed (attempt $RetryCount/$MaxRetries): $_"
    if ($RetryCount -lt $MaxRetries) {
      Write-Host "Retrying in 5 seconds..."
      Start-Sleep -Seconds 5
    }
  }
}

if (-not $Success) {
  Write-Error "Failed to start system after $MaxRetries attempts"
  exit 1
}

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
