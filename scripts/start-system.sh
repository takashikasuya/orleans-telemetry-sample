#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPORT_DIR="${TELEMETRY_E2E_REPORT_DIR:-$ROOT/reports}"
STORAGE_DIR="$ROOT/storage"
COMPOSE="docker compose"
STATE_FILE="$ROOT/scripts/.system-state"
DOCKERFILE="$ROOT/Dockerfile.dotnet"
SEED_FILE="$ROOT/data/sample.ttl"

USE_SIMULATOR=false
USE_RABBITMQ=false

usage() {
  cat <<'USAGE'
Usage: ./scripts/start-system.sh [--simulator] [--rabbitmq]

  --simulator   Enable Simulator ingest connector.
  --rabbitmq    Enable RabbitMq ingest connector (also starts publisher).

If no options are provided, no ingest connectors are enabled.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --simulator|-s)
      USE_SIMULATOR=true
      shift
      ;;
    --rabbitmq|-r)
      USE_RABBITMQ=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

mkdir -p "$REPORT_DIR" "$STORAGE_DIR"

if [[ -f "$STATE_FILE" ]]; then
  rm -f "$STATE_FILE"
fi

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/telemetry-system-XXXXXX")"
OVERRIDE_FILE="$TEMP_DIR/docker-compose.override.yml"

INGEST_ENABLED_LINES=""
SIMULATOR_LINES=""
RABBITMQ_LINES=""
INGEST_SINK_LINES=""
MQ_BLOCK=""
PUBLISHER_BLOCK=""
PUBLISHER_SERVICE=""

enabled_index=0
if $USE_SIMULATOR; then
  INGEST_ENABLED_LINES="${INGEST_ENABLED_LINES}      TelemetryIngest__Enabled__${enabled_index}: Simulator"$'\n'
  enabled_index=$((enabled_index + 1))
  SIMULATOR_LINES=$(cat <<'SIM'
      TelemetryIngest__Simulator__TenantId: t1
      TelemetryIngest__Simulator__BuildingName: Simulator-Building
      TelemetryIngest__Simulator__SpaceId: Simulator-Area
      TelemetryIngest__Simulator__DeviceIdPrefix: device
      TelemetryIngest__Simulator__DeviceCount: "1"
      TelemetryIngest__Simulator__PointsPerDevice: "1"
      TelemetryIngest__Simulator__IntervalMilliseconds: "500"
SIM
)
fi

if $USE_RABBITMQ; then
  INGEST_ENABLED_LINES="${INGEST_ENABLED_LINES}      TelemetryIngest__Enabled__${enabled_index}: RabbitMq"$'\n'
  enabled_index=$((enabled_index + 1))
  RABBITMQ_LINES=$(cat <<'RMQ'
      TelemetryIngest__RabbitMq__HostName: mq
      TelemetryIngest__RabbitMq__Port: "5672"
      TelemetryIngest__RabbitMq__UserName: user
      TelemetryIngest__RabbitMq__Password: password
      TelemetryIngest__RabbitMq__QueueName: telemetry
      TelemetryIngest__RabbitMq__PrefetchCount: "100"
RMQ
)
  MQ_BLOCK=$(cat <<'YML'
  mq:
    environment:
      RABBITMQ_DEFAULT_USER: user
      RABBITMQ_DEFAULT_PASS: password
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20
YML
)
  PUBLISHER_BLOCK=$(cat <<YML
  publisher:
    build:
      context: $ROOT
      dockerfile: $DOCKERFILE
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
      - $SEED_FILE:/seed/seed.ttl:ro
YML
)
  PUBLISHER_SERVICE=" publisher"
fi

# Always enable ParquetStorage for telemetry persistence
INGEST_SINK_LINES="      TelemetryIngest__EventSinks__Enabled__0: Logging
      TelemetryIngest__EventSinks__Enabled__1: ParquetStorage"

cat <<YML > "$OVERRIDE_FILE"
services:
$MQ_BLOCK
  silo:
    depends_on:
      mq:
        condition: service_healthy
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
$INGEST_ENABLED_LINES$INGEST_SINK_LINES
$RABBITMQ_LINES
$SIMULATOR_LINES
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
      OIDC_AUTHORITY: http://mock-oidc:8080/default
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
      OIDC_AUTHORITY: http://mock-oidc:8080/default
      OIDC_AUDIENCE: default
      TelemetryStorage__StagePath: /storage/stage
      TelemetryStorage__ParquetPath: /storage/parquet
      TelemetryStorage__IndexPath: /storage/index
      Orleans__GatewayHost: silo
      Orleans__GatewayPort: "30000"
    volumes:
      - $STORAGE_DIR:/storage
    extra_hosts:
      - "localhost:host-gateway"
$PUBLISHER_BLOCK
YML

echo "Starting system..."

# Retry logic with sequential builds to avoid Docker daemon overload
MAX_RETRIES=3
RETRY_COUNT=0
SUCCESS=false

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
  if {
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" down --remove-orphans 2>/dev/null || true
    
    echo "Building services (silo, api, admin${PUBLISHER_SERVICE})..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" build silo api admin${PUBLISHER_SERVICE}
    
    echo "Starting base services (mq, silo, mock-oidc)..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" up -d mq silo mock-oidc

    echo "Waiting for Orleans gateway (localhost:30000)..."
    GATEWAY_RETRIES=24
    GATEWAY_DELAY=2
    GATEWAY_READY=false
    for ((i=1; i<=GATEWAY_RETRIES; i++)); do
      # Test gateway from host using nc (netcat) with timeout
      if command -v nc >/dev/null 2>&1; then
        if nc -z -w 1 localhost 30000 >/dev/null 2>&1; then
          GATEWAY_READY=true
          echo "Orleans gateway is ready."
          break
        fi
      elif command -v timeout >/dev/null 2>&1; then
        # Fallback: use bash /dev/tcp from host
        if timeout 1 bash -c "</dev/tcp/localhost/30000" >/dev/null 2>&1; then
          GATEWAY_READY=true
          echo "Orleans gateway is ready."
          break
        fi
      fi
      echo "Gateway not ready (attempt $i/$GATEWAY_RETRIES). Retrying in ${GATEWAY_DELAY}s..."
      sleep "$GATEWAY_DELAY"
    done

    if [ "$GATEWAY_READY" = false ]; then
      echo "Orleans gateway did not become ready after $GATEWAY_RETRIES attempts." >&2
      exit 1
    fi

    echo "Starting API/Admin/Publisher..."
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" up -d api admin$PUBLISHER_SERVICE
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
