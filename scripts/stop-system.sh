#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE="docker compose"
STATE_FILE="$ROOT/scripts/.system-state"

TEMP_DIR=""
OVERRIDE_FILE=""

if [[ -f "$STATE_FILE" ]]; then
  # shellcheck disable=SC1090
  source "$STATE_FILE"
fi

if [[ -n "${OVERRIDE_FILE:-}" && -f "$OVERRIDE_FILE" ]]; then
  echo "Stopping system with override: $OVERRIDE_FILE"
  $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" down --remove-orphans
else
  echo "Stopping system with base compose (override not found)"
  $COMPOSE -f "$ROOT/docker-compose.yml" down --remove-orphans
fi

if [[ -n "${TEMP_DIR:-}" && -d "$TEMP_DIR" ]]; then
  rm -rf "$TEMP_DIR"
fi
rm -f "$STATE_FILE"

echo "System stopped."
