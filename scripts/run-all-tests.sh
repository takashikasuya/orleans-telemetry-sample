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
  $0

  # Quick run (skip memory test)
  $0 --quick --skip-memory

  # Only load and memory tests
  $0 --skip-unit --skip-e2e
EOF
}

# Parse arguments
SKIP_UNIT=0
SKIP_E2E=0
SKIP_LOAD=0
SKIP_MEMORY=0
QUICK=0
OUTPUT_DIR="reports"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --help)
      print_usage
      exit 0
      ;;
    --skip-unit)
      SKIP_UNIT=1
      shift
      ;;
    --skip-e2e)
      SKIP_E2E=1
      shift
      ;;
    --skip-load)
      SKIP_LOAD=1
      shift
      ;;
    --skip-memory)
      SKIP_MEMORY=1
      shift
      ;;
    --quick)
      QUICK=1
      shift
      ;;
    --output-dir)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      print_usage
      exit 1
      ;;
  esac
done

FAILED_SUITES=()

# Unit tests
if [[ $SKIP_UNIT -eq 0 ]]; then
  log "===== Running unit tests ====="
  if dotnet test; then
    log "Unit tests PASSED"
  else
    log "Unit tests FAILED"
    FAILED_SUITES+=("unit")
  fi
else
  log "Skipping unit tests"
fi

# E2E tests
if [[ $SKIP_E2E -eq 0 ]]; then
  log "===== Running E2E tests ====="
  E2E_ARGS=()
  [[ $QUICK -eq 1 ]] && E2E_ARGS+=(--mode inproc)
  
  if "$ROOT/scripts/run-e2e.sh" "${E2E_ARGS[@]}"; then
    log "E2E tests PASSED"
  else
    log "E2E tests FAILED"
    FAILED_SUITES+=("e2e")
  fi
else
  log "Skipping E2E tests"
fi

# Load test
if [[ $SKIP_LOAD -eq 0 ]]; then
  log "===== Running ingest load test ====="
  LOAD_ARGS=(--ensure-rabbitmq --output-dir "$OUTPUT_DIR")
  [[ $QUICK -eq 1 ]] && LOAD_ARGS+=(--quick)
  
  if "$ROOT/scripts/run-loadtest.sh" "${LOAD_ARGS[@]}"; then
    log "Load test PASSED"
  else
    log "Load test FAILED"
    FAILED_SUITES+=("load")
  fi
else
  log "Skipping load test"
fi

# Memory test
if [[ $SKIP_MEMORY -eq 0 ]]; then
  log "===== Running memory load test ====="
  MEMORY_ARGS=(--ensure-cluster --output-dir "$OUTPUT_DIR")
  
  if [[ -f "$ROOT/scripts/run-memorytest.sh" ]]; then
    if "$ROOT/scripts/run-memorytest.sh" "${MEMORY_ARGS[@]}"; then
      log "Memory test PASSED"
    else
      log "Memory test FAILED"
      FAILED_SUITES+=("memory")
    fi
  else
    log "WARNING: run-memorytest.sh not found, skipping memory test"
  fi
else
  log "Skipping memory test"
fi

# Summary
log "===== Test Summary ====="
if [[ ${#FAILED_SUITES[@]} -eq 0 ]]; then
  log "All enabled test suites PASSED"
  exit 0
else
  log "FAILED test suites: ${FAILED_SUITES[*]}"
  exit 1
fi
