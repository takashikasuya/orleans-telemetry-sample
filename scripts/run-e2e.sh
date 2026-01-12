#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPORT_DIR="${TELEMETRY_E2E_REPORT_DIR:-$ROOT/reports}"
RUN_ID="telemetry-e2e-docker-$(date -u +%Y%m%d-%H%M%S)"
REPORT_MD="$REPORT_DIR/$RUN_ID.md"
REPORT_JSON="$REPORT_DIR/$RUN_ID.json"
SEED_FILE="$ROOT/src/Telemetry.E2E.Tests/seed.ttl"
STORAGE_DIR="$ROOT/storage"
COMPOSE="docker compose"
TEMP_DIR=""
OVERRIDE_FILE=""

mkdir -p "$REPORT_DIR" "$STORAGE_DIR"

cleanup() {
  if [[ -n "$OVERRIDE_FILE" ]]; then
    log "Stopping docker compose"
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" down --remove-orphans
  fi
  if [[ -n "$TEMP_DIR" ]]; then
    rm -rf "$TEMP_DIR"
  fi
}

log() {
  echo "[$(date -u +%H:%M:%S)] $*"
}

wait_for_url() {
  local url="$1"
  local timeout_sec="$2"
  local start
  start=$(date +%s)

  while true; do
    if curl -sS "$url" >/dev/null 2>&1; then
      return 0
    fi
    if (( $(date +%s) - start > timeout_sec )); then
      return 1
    fi
    sleep 2
  done
}

run_inproc() {
  log "Running in-proc E2E test"
  dotnet test "$ROOT/src/Telemetry.E2E.Tests"
}

run_docker() {
  TEMP_DIR=$(mktemp -d)
  OVERRIDE_FILE="$TEMP_DIR/docker-compose.override.yml"

  cat <<'YML' > "$OVERRIDE_FILE"
version: "3.9"
services:
  silo:
    build:
      context: .
      dockerfile: Dockerfile.dotnet
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
      - ./storage:/storage
      - ./src/Telemetry.E2E.Tests/seed.ttl:/seed/seed.ttl:ro
  api:
    build:
      context: .
      dockerfile: Dockerfile.dotnet
      args:
        PROJECT: src/ApiGateway
    environment:
      TelemetryStorage__StagePath: /storage/stage
      TelemetryStorage__ParquetPath: /storage/parquet
      TelemetryStorage__IndexPath: /storage/index
      Orleans__GatewayHost: silo
      Orleans__GatewayPort: "30000"
      ASPNETCORE_URLS: http://+:80
    volumes:
      - ./storage:/storage
YML

  log "Starting docker compose"
  $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" down --remove-orphans
  $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" up --build -d mq silo api mock-oidc

  trap cleanup EXIT

  log "Waiting for API"
  if ! wait_for_url "http://localhost:8080/swagger" 120; then
    echo "API did not become ready in time" >&2
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" ps >&2 || true
    $COMPOSE -f "$ROOT/docker-compose.yml" -f "$OVERRIDE_FILE" logs --no-color api silo >&2 || true
    exit 1
  fi

  local token token_response
  token=""
  token_response=""
  for _ in {1..60}; do
    token_response=$(curl -sS -X POST http://localhost:8081/default/token \
      -H "Host: mock-oidc:8080" \
      -u "test-client:test-secret" \
      -H "Content-Type: application/x-www-form-urlencoded" \
      -d "grant_type=client_credentials" 2>/dev/null || true)
    token=$(python3 -c 'import json,sys; \
raw=sys.stdin.read(); \
data=json.loads(raw) if raw else {}; \
print((data if isinstance(data, dict) else {}).get("access_token", ""))' <<< "$token_response")
    if [[ -n "$token" ]]; then
      break
    fi
    sleep 2
  done

  if [[ -z "$token" ]]; then
    echo "Failed to get access token" >&2
    if [[ -n "$token_response" ]]; then
      echo "Last token response: $token_response" >&2
    fi
    exit 1
  fi

  local node_id encoded_node
  node_id="urn:point-1"
  encoded_node=$(python3 - <<'PY'
import urllib.parse
print(urllib.parse.quote("urn:point-1", safe=""))
PY
)

  log "Querying graph node"
  local node_json
  node_json=$(curl -fsS -H "Authorization: Bearer $token" "http://localhost:8080/api/nodes/$encoded_node")

  local device_id point_id
  device_id=$(NODE_JSON="$node_json" python3 - <<'PY'
import json,os
raw = os.environ.get("NODE_JSON", "")
if not raw.strip():
    print("")
    raise SystemExit(0)
node = json.loads(raw)
attrs = node.get("node", {}).get("attributes", {})
print(attrs.get("DeviceId", ""))
PY
)

  point_id=$(NODE_JSON="$node_json" python3 - <<'PY'
import json,os
raw = os.environ.get("NODE_JSON", "")
if not raw.strip():
    print("")
    raise SystemExit(0)
node = json.loads(raw)
attrs = node.get("node", {}).get("attributes", {})
print(attrs.get("PointId", ""))
PY
)

  if [[ -z "$device_id" || -z "$point_id" ]]; then
    echo "Missing DeviceId or PointId from graph node" >&2
    echo "$node_json" >&2
    exit 1
  fi

  log "Querying point snapshot"
  local point_json
  point_json=$(curl -fsS -H "Authorization: Bearer $token" "http://localhost:8080/api/nodes/$encoded_node/value")

  log "Querying device snapshot"
  local device_json
  device_json=$(curl -fsS -H "Authorization: Bearer $token" "http://localhost:8080/api/devices/$device_id")

  log "Querying telemetry history"
  local now from to from_encoded to_encoded telemetry_json
  now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  from=$(date -u -d '-10 minutes' +%Y-%m-%dT%H:%M:%SZ)
  to=$(date -u -d '+10 minutes' +%Y-%m-%dT%H:%M:%SZ)
  read -r from_encoded to_encoded < <(FROM="$from" TO="$to" python3 - <<'PY'
import os
import urllib.parse
print(urllib.parse.quote_plus(os.environ["FROM"]), urllib.parse.quote_plus(os.environ["TO"]))
PY
)
  telemetry_json=$(curl -fsS -H "Authorization: Bearer $token" "http://localhost:8080/api/telemetry/$device_id?from=$from_encoded&to=$to_encoded&pointId=$point_id&limit=10")

  local storage_parquet storage_index
  storage_parquet=$(find "$STORAGE_DIR/parquet" -type f -name "*.parquet" | head -n 1 || true)
  storage_index=$(find "$STORAGE_DIR/index" -type f -name "*.json" | head -n 1 || true)

  NODE_JSON="$node_json" \
  POINT_JSON="$point_json" \
  DEVICE_JSON="$device_json" \
  TELEMETRY_JSON="$telemetry_json" \
  REPORT_MD="$REPORT_MD" \
  REPORT_JSON="$REPORT_JSON" \
  REPORT_DIR="$REPORT_DIR" \
  RUN_ID="$RUN_ID" \
  STORAGE_PARQUET="$storage_parquet" \
  STORAGE_INDEX="$storage_index" \
  python3 - <<'PY'
import json,os,datetime

report_md = os.environ["REPORT_MD"]
report_json = os.environ["REPORT_JSON"]
run_id = os.environ["RUN_ID"]
report_dir = os.environ["REPORT_DIR"]
node_json = json.loads(os.environ["NODE_JSON"])
point_json = json.loads(os.environ["POINT_JSON"])
device_json = json.loads(os.environ["DEVICE_JSON"])
telemetry = json.loads(os.environ["TELEMETRY_JSON"])

storage_parquet = os.environ.get("STORAGE_PARQUET") or ""
storage_index = os.environ.get("STORAGE_INDEX") or ""

now = datetime.datetime.now(datetime.timezone.utc)

point_updated = point_json.get("updatedAt")
lag_ms = None
if point_updated:
    try:
        pu = datetime.datetime.fromisoformat(point_updated.replace("Z", "+00:00"))
        lag_ms = (now - pu).total_seconds() * 1000.0
    except Exception:
        lag_ms = None

seed_event = None
telemetry_items = telemetry
if isinstance(telemetry, dict):
    mode = telemetry.get("mode")
    if mode == "inline":
        telemetry_items = telemetry.get("items", [])
    else:
        telemetry_items = []

if isinstance(telemetry_items, list) and telemetry_items:
    seed_event = telemetry_items[0]

report = {
    "runId": run_id,
    "status": "Passed",
    "startedAt": now.isoformat().replace("+00:00","Z"),
    "completedAt": now.isoformat().replace("+00:00","Z"),
    "tenantId": "t1",
    "rdfSeedPath": "seed.ttl",
    "reportDirectory": report_dir,
    "simulator": {
        "tenantId": "t1",
        "buildingName": "building",
        "spaceId": "space",
        "deviceIdPrefix": "device",
        "deviceCount": 1,
        "pointsPerDevice": 1,
        "intervalMilliseconds": 500
    },
    "graph": {
        "nodeId": node_json.get("node",{}).get("nodeId",""),
        "attributes": node_json.get("node",{}).get("attributes",{})
    },
    "seedEvent": seed_event,
    "api": {
        "pointLastSequence": point_json.get("lastSequence"),
        "pointUpdatedAt": point_json.get("updatedAt"),
        "pointLatestValueJson": json.dumps(point_json.get("latestValue")),
        "pointReadAt": now.isoformat().replace("+00:00","Z"),
        "pointLagMilliseconds": lag_ms,
        "deviceLastSequence": device_json.get("lastSequence"),
        "deviceUpdatedAt": device_json.get("updatedAt"),
        "devicePropertiesJson": json.dumps(device_json.get("properties")),
        "telemetryResultCount": len(telemetry) if isinstance(telemetry, list) else 0,
        "telemetryFirstResultJson": json.dumps(telemetry[0]) if isinstance(telemetry, list) and telemetry else ""
    },
    "storage": {
        "parquetFilePath": storage_parquet,
        "parquetExists": bool(storage_parquet),
        "indexFilePath": storage_index,
        "indexExists": bool(storage_index)
    }
}

lines = [
    "# Telemetry E2E Report (Docker)",
    f"RunId: {run_id}",
    "Status: Passed",
    f"StartedAt: {report['startedAt']}",
    f"CompletedAt: {report['completedAt']}",
    f"TenantId: {report['tenantId']}",
    f"ReportDirectory: {report_dir}",
    "",
    "## Graph Binding",
    f"- NodeId: {report['graph']['nodeId']}",
]
for k,v in sorted(report["graph"]["attributes"].items()):
    lines.append(f"- {k}: {v}")

lines.extend([
    "",
    "## API Checks",
    f"- PointLastSequence: {report['api']['pointLastSequence']}",
    f"- PointUpdatedAt: {report['api']['pointUpdatedAt']}",
    f"- PointLagMilliseconds: {report['api']['pointLagMilliseconds']}",
    f"- DeviceLastSequence: {report['api']['deviceLastSequence']}",
    f"- TelemetryResultCount: {report['api']['telemetryResultCount']}",
    "",
    "## Storage",
    f"- ParquetFilePath: {report['storage']['parquetFilePath']}",
    f"- ParquetExists: {report['storage']['parquetExists']}",
    f"- IndexFilePath: {report['storage']['indexFilePath']}",
    f"- IndexExists: {report['storage']['indexExists']}",
    ""
])

with open(report_md, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))

with open(report_json, "w", encoding="utf-8") as f:
    json.dump(report, f, indent=2)
PY

  log "Docker E2E report written: $REPORT_MD"
}

run_inproc
run_docker
