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
  $0 --quick

  # Full test with RabbitMQ scenarios (ensure RabbitMQ is running)
  $0 --soak --spike

  # Batch sweep with automatic RabbitMQ startup
  $0 --batch-sweep --ensure-rabbitmq
EOF
}

# Parse arguments
ARGS=()
OUTPUT_DIR=""
CONFIG_PATH=""
ENSURE_RABBITMQ=0

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
    --ensure-rabbitmq)
      ENSURE_RABBITMQ=1
      shift
      ;;
    *)
      ARGS+=("$1")
      shift
      ;;
  esac
done

# Ensure RabbitMQ if requested
if [[ $ENSURE_RABBITMQ -eq 1 ]]; then
  log "Ensuring RabbitMQ is running..."
  if ! docker compose ps mq | grep -q "Up"; then
    log "Starting RabbitMQ..."
    docker compose up -d mq
    log "Waiting for RabbitMQ to be ready..."
    sleep 5
  else
    log "RabbitMQ already running"
  fi
fi

# Build command
CMD=(dotnet run --project src/Telemetry.Ingest.LoadTest --)

if [[ -n "$OUTPUT_DIR" ]]; then
  CMD+=(--output-dir "$OUTPUT_DIR")
fi

if [[ -n "$CONFIG_PATH" ]]; then
  CMD+=(--config "$CONFIG_PATH")
fi

CMD+=("${ARGS[@]}")

log "Running load test: ${CMD[*]}"
"${CMD[@]}"

log "Load test completed. Check reports directory for results."
