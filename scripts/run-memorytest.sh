#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

log() {
  echo "[$(date -u +%H:%M:%S)] $*"
}

print_usage() {
  cat <<EOF
Usage: $0 [OPTIONS]

Run Orleans memory load test and generate reports.

Options:
  --output-dir <path>     Override report output directory (default: reports)
  --config <path>         Configuration file path (default: src/Telemetry.Orleans.MemoryLoadTest/appsettings.memoryloadtest.json)
  --ensure-cluster        Start Orleans cluster via docker compose if needed
  --gateway-host <host>   Orleans gateway host (default: from config or localhost)
  --gateway-port <port>   Orleans gateway port (default: from config or 30000)
  --help                  Show this help

Note:
  When running against Docker-hosted silo, UseLocalhostClustering may fail.
  This test is best run with locally-hosted silo outside of Docker.
  If you experience connection issues, try stopping Docker silo and running
  SiloHost locally: dotnet run --project src/SiloHost

Examples:
  # Run with default configuration (local silo expected)
  $0

  # Run against local cluster with custom config
  $0 --config my-memorytest-config.json

  # Start cluster automatically and run test (may fail with Docker)
  $0 --ensure-cluster

  # Run against remote gateway
  $0 --gateway-host silo --gateway-port 30000
EOF
}

# Parse arguments
OUTPUT_DIR=""
CONFIG_PATH=""
ENSURE_CLUSTER=0
GATEWAY_HOST=""
GATEWAY_PORT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --help)
      print_usage
      exit 0
      ;;
    --output-dir)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --config)
      CONFIG_PATH="$2"
      shift 2
      ;;
    --ensure-cluster)
      ENSURE_CLUSTER=1
      shift
      ;;
    --gateway-host)
      GATEWAY_HOST="$2"
      shift 2
      ;;
    --gateway-port)
      GATEWAY_PORT="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      print_usage
      exit 1
      ;;
  esac
done

# Ensure cluster if requested
if [[ $ENSURE_CLUSTER -eq 1 ]]; then
  log "Ensuring Orleans cluster is running..."
  if ! docker compose ps silo | grep -q "Up"; then
    log "Starting Orleans cluster..."
    docker compose up -d silo
    log "Waiting for cluster to be ready..."
    sleep 20
    log "Checking cluster health..."
    for i in {1..5}; do
      if docker compose logs silo 2>&1 | grep -q "Started silo"; then
        log "Cluster is ready"
        break
      fi
      log "Waiting for cluster... (attempt $i/5)"
      sleep 5
    done
  else
    log "Orleans cluster already running"
  fi
fi

# Build command
CMD=(dotnet run --project src/Telemetry.Orleans.MemoryLoadTest --)

if [[ -n "$OUTPUT_DIR" ]]; then
  CMD+=(--output-dir "$OUTPUT_DIR")
fi

if [[ -n "$CONFIG_PATH" ]]; then
  CMD+=(--config "$CONFIG_PATH")
fi

# Set environment variables for gateway override
if [[ -n "$GATEWAY_HOST" ]]; then
  export Orleans__GatewayHost="$GATEWAY_HOST"
fi

if [[ -n "$GATEWAY_PORT" ]]; then
  export Orleans__GatewayPort="$GATEWAY_PORT"
fi

log "Running memory load test: ${CMD[*]}"
"${CMD[@]}"

EXIT_CODE=$?

if [[ $EXIT_CODE -eq 0 ]]; then
  log "Memory load test PASSED. Check reports directory for results."
else
  log "Memory load test FAILED (exit code: $EXIT_CODE)"
fi

exit $EXIT_CODE
