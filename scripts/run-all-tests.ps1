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

Run all test suites: unit tests, E2E, load test, and memory test.

Options:
  --skip-unit             Skip unit tests
  --skip-e2e              Skip E2E tests
  --skip-load             Skip ingest load test
  --skip-memory           Skip memory load test
  --quick                 Run quick variants where applicable
  --output-dir <path>     Override report output directory (default: reports)
  --help                  Show this help

Examples:
  # Run all tests (full suite)
  $($MyInvocation.MyCommand.Name)

  # Quick run (skip memory test)
  $($MyInvocation.MyCommand.Name) --quick --skip-memory

  # Only load and memory tests
  $($MyInvocation.MyCommand.Name) --skip-unit --skip-e2e
"@ | Write-Host
}

$skipUnit = $false
$skipE2e = $false
$skipLoad = $false
$skipMemory = $false
$quick = $false
$outputDir = "reports"

for ($i = 0; $i -lt $args.Count; $i++) {
  switch ($args[$i]) {
    "--help" {
      Print-Usage
      exit 0
    }
    "--skip-unit" { $skipUnit = $true }
    "--skip-e2e" { $skipE2e = $true }
    "--skip-load" { $skipLoad = $true }
    "--skip-memory" { $skipMemory = $true }
    "--quick" { $quick = $true }
    "--output-dir" {
      if ($i + 1 -ge $args.Count) {
        throw "Missing value for --output-dir"
      }
      $outputDir = $args[$i + 1]
      $i++
    }
    Default {
      Write-Error "Unknown option: $($args[$i])"
      Print-Usage
      exit 1
    }
  }
}

$failedSuites = New-Object System.Collections.Generic.List[string]

if (-not $skipUnit) {
  Log "===== Running unit tests ====="
  & dotnet test
  if ($LASTEXITCODE -eq 0) {
    Log "Unit tests PASSED"
  } else {
    Log "Unit tests FAILED"
    $failedSuites.Add("unit")
  }
} else {
  Log "Skipping unit tests"
}

if (-not $skipE2e) {
  Log "===== Running E2E tests ====="
  $e2eArgs = @()
  if ($quick) {
    $e2eArgs += "--mode"
    $e2eArgs += "inproc"
  }

  & (Join-Path $Root "scripts/run-e2e.ps1") @e2eArgs
  if ($LASTEXITCODE -eq 0) {
    Log "E2E tests PASSED"
  } else {
    Log "E2E tests FAILED"
    $failedSuites.Add("e2e")
  }
} else {
  Log "Skipping E2E tests"
}

if (-not $skipLoad) {
  Log "===== Running ingest load test ====="
  $loadArgs = @("--ensure-rabbitmq", "--output-dir", $outputDir)
  if ($quick) {
    $loadArgs += "--quick"
  }

  & (Join-Path $Root "scripts/run-loadtest.ps1") @loadArgs
  if ($LASTEXITCODE -eq 0) {
    Log "Load test PASSED"
  } else {
    Log "Load test FAILED"
    $failedSuites.Add("load")
  }
} else {
  Log "Skipping load test"
}

if (-not $skipMemory) {
  Log "===== Running memory load test ====="
  $memoryArgs = @("--ensure-cluster", "--output-dir", $outputDir)
  $memoryScript = Join-Path $Root "scripts/run-memorytest.ps1"
  if (Test-Path $memoryScript) {
    & $memoryScript @memoryArgs
    if ($LASTEXITCODE -eq 0) {
      Log "Memory test PASSED"
    } else {
      Log "Memory test FAILED"
      $failedSuites.Add("memory")
    }
  } else {
    Log "WARNING: run-memorytest.ps1 not found, skipping memory test"
  }
} else {
  Log "Skipping memory test"
}

Log "===== Test Summary ====="
if ($failedSuites.Count -eq 0) {
  Log "All enabled test suites PASSED"
  exit 0
}

Log ("FAILED test suites: " + ($failedSuites -join " "))
exit 1
