#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPORT_DIR="${TELEMETRY_E2E_REPORT_DIR:-$ROOT/reports}"
STORAGE_DIR="$ROOT/storage"
COMPOSE="docker compose"
STATE_FILE="$ROOT/scripts/.system-state"
DOCKERFILE="$ROOT/Dockerfile.dotnet"
SEED_FILE="$ROOT/data/seed-complex.ttl"

mkdir -p "$REPORT_DIR" "$STORAGE_DIR"

if [[ -f "$STATE_FILE" ]]; then
  rm -f "$STATE_FILE"
fi

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/telemetry-system-XXXXXX")"
OVERRIDE_FILE="$TEMP_DIR/docker-compose.override.yml"

cat <<YML > "$OVERRIDE_FILE"
services:
  silo:
    build:
      context: $ROOT
      dockerfile: $DOCKERFILE
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
      - $STORAGE_DIR:/storage
      - $SEED_FILE:/seed/seed.ttl:ro
  api:
    build:
      context: $ROOT
      dockerfile: $DOCKERFILE
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
      - $STORAGE_DIR:/storage
    extra_hosts:
      - "localhost:host-gateway"
  admin:
    build:
      context: $ROOT
      dockerfile: $DOCKERFILE
      args:
        PROJECT: src/AdminGateway
    environment:
      OIDC_AUTHORITY: http://localhost:8081/default
      OIDC_AUDIENCE: default
    extra_hosts:
      - "localhost:host-gateway"
YML

echo "Starting system..."

# Retry logic with sequential builds to avoid Docker daemon overload
MAX_RETRIES=3
RETRY_COUNT=0
SUCCESS=false

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
  if {
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" down --remove-orphans 2>/dev/null || true
    
    # Build services sequentially to avoid Docker daemon overload
    echo "Building silo..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" build --no-cache silo
    
    echo "Building api..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" build --no-cache api
    
    echo "Building admin..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" build --no-cache admin
    
    echo "Starting services..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" up -d mq silo api admin mock-oidc
  }; then
    SUCCESS=true
    break
  else
    RETRY_COUNT=$((RETRY_COUNT + 1))
    if [ $RETRY_COUNT -lt $MAX_RETRIES ]; then
      echo "Build failed (attempt $RETRY_COUNT/$MAX_RETRIES), retrying in 5 seconds..."
      sleep 5
    fi
  fi
done

if [ "$SUCCESS" = false ]; then
  echo "Failed to start system after $MAX_RETRIES attempts" >&2
  exit 1
fi

cat > "$STATE_FILE" <<EOF
TEMP_DIR=$TEMP_DIR
OVERRIDE_FILE=$OVERRIDE_FILE
EOF

echo "System started."
echo "Swagger: http://localhost:8080/swagger"
echo "Admin UI: http://localhost:8082/"
echo "Mock OIDC: http://localhost:8081/default"
echo "Storage dir: $STORAGE_DIR"
echo "Reports dir: $REPORT_DIR"
echo "Override file: $OVERRIDE_FILE"
echo "State file: $STATE_FILE"
