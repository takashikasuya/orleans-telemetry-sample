param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $Root

function Get-UtcNow {
  return (Get-Date).ToUniversalTime()
}

function Log([string]$Message) {
  $timestamp = (Get-UtcNow).ToString("HH:mm:ss")
  Write-Host "[$timestamp] $Message"
}

function Print-Usage {
  @"
Usage: $($MyInvocation.MyCommand.Name) [OPTIONS]

Run telemetry ingest load test and generate reports.

Options:
  --quick                 Run shortened stages
  --batch-sweep           Add batch size sweep stages
  --abnormal              Add failure scenario stages
  --soak                  Add RabbitMQ soak test
  --spike                 Add RabbitMQ spike test
  --multi-connector       Add multi-connector stages
  --output-dir <path>     Override report output directory (default: reports)
  --config <path>         Configuration file path (default: src/Telemetry.Ingest.LoadTest/appsettings.loadtest.json)
  --ensure-rabbitmq       Start RabbitMQ via docker compose if needed
  --help                  Show this help

Examples:
  # Quick baseline test
  $($MyInvocation.MyCommand.Name) --quick

  # Full test with RabbitMQ scenarios (ensure RabbitMQ is running)
  $($MyInvocation.MyCommand.Name) --soak --spike

  # Batch sweep with automatic RabbitMQ startup
  $($MyInvocation.MyCommand.Name) --batch-sweep --ensure-rabbitmq
"@ | Write-Host
}

$extraArgs = New-Object System.Collections.Generic.List[string]
$outputDir = ""
$configPath = ""
$ensureRabbitMq = $false

for ($i = 0; $i -lt $args.Count; $i++) {
  switch ($args[$i]) {
    "--help" {
      Print-Usage
      exit 0
    }
    "--output-dir" {
      if ($i + 1 -ge $args.Count) {
        throw "Missing value for --output-dir"
      }
      $outputDir = $args[$i + 1]
      $i++
    }
    "--config" {
      if ($i + 1 -ge $args.Count) {
        throw "Missing value for --config"
      }
      $configPath = $args[$i + 1]
      $i++
    }
    "--ensure-rabbitmq" { $ensureRabbitMq = $true }
    Default { $extraArgs.Add($args[$i]) }
  }
}

if ($ensureRabbitMq) {
  Log "Ensuring RabbitMQ is running..."
  $status = & docker compose ps mq 2>$null
  if ($status -notmatch "Up") {
    Log "Starting RabbitMQ..."
    & docker compose up -d mq
    Log "Waiting for RabbitMQ to be ready..."
    Start-Sleep -Seconds 5
  } else {
    Log "RabbitMQ already running"
  }
}

$cmd = New-Object System.Collections.Generic.List[string]
$cmd.Add("run")
$cmd.Add("--project")
$cmd.Add("src/Telemetry.Ingest.LoadTest")
$cmd.Add("--")

if ($outputDir) {
  $cmd.Add("--output-dir")
  $cmd.Add($outputDir)
}

if ($configPath) {
  $cmd.Add("--config")
  $cmd.Add($configPath)
}

foreach ($arg in $extraArgs) {
  $cmd.Add($arg)
}

Log ("Running load test: dotnet " + ($cmd -join " "))
& dotnet @cmd
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Log "Load test completed. Check reports directory for results."
