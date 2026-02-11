# ローカルセットアップ & オペレーションガイド

このドキュメントは README から詳細手順を分離した運用向けガイドです。初回導入は README の Quick Start を参照し、ここでは起動バリエーション、認証、検証、テスト実行を扱います。

## 起動パターン

### Docker Compose（推奨）

```bash
docker compose up --build
```

主な公開先:
- API Swagger: `http://localhost:8080/swagger`
- Mock OIDC: `http://localhost:8081/default`
- Admin UI: `http://localhost:8082/`
- Telemetry Client: `http://localhost:8083/`

### ヘルパースクリプト

- 起動: `scripts/start-system.sh` / `scripts/start-system.ps1`
- 停止: `scripts/stop-system.sh` / `scripts/stop-system.ps1`

Linux/macOS:

```bash
./scripts/start-system.sh --simulator
./scripts/stop-system.sh
```

PowerShell:

```powershell
.\scripts\start-system.ps1 -Simulator
.\scripts\stop-system.ps1
```

補足:
- `--simulator` は `data/seed.ttl` と `TENANT_ID=t1` を使った ingest を有効化。
- `--rabbitmq` は RabbitMQ ingest と publisher コンテナを有効化。

### Docker なしでのローカル起動

```bash
# RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management

# Orleans Silo
dotnet run --project src/SiloHost

# API Gateway
dotnet run --project src/ApiGateway

# Publisher (optional)
dotnet run --project src/Publisher
```

## 認証（Mock OIDC）

```bash
TOKEN=$(curl -s -X POST http://localhost:8081/default/token \
  -u "test-client:test-secret" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" | jq -r '.access_token')

curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/devices/device-1"
```

## 主な環境変数

### Telemetry Source
- `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USER`, `RABBITMQ_PASS`
- `KAFKA_BOOTSTRAP_SERVERS`, `KAFKA_TOPIC`, `KAFKA_GROUP_ID`

### 認証
- `OIDC_AUTHORITY`, `OIDC_AUDIENCE`, （必要に応じて）`ADMIN_AUDIENCE`

### RDF シード
- `RDF_SEED_PATH`
- `TENANT_ID`

> Docker Compose では `silo` と `publisher` が同一 RDF seed を参照するように揃えてください。

## API 検証クライアント

```bash
dotnet run --project src/ApiGateway.Client
```

オプション例:

```bash
dotnet run --project src/ApiGateway.Client -- \
  --api-base http://localhost:8080 \
  --authority http://localhost:8081/default \
  --client-id test-client \
  --client-secret test-secret \
  --history-minutes 15 \
  --report-dir reports
```

## テスト/検証コマンド

```bash
dotnet build
dotnet test
```

スクリプト利用:
- `scripts/run-all-tests.sh` / `scripts/run-all-tests.ps1`
- `scripts/run-e2e.sh` / `scripts/run-e2e.ps1`
- `scripts/run-loadtest.sh` / `scripts/run-loadtest.ps1`

## メモリロードテスト

```bash
dotnet run --project src/Telemetry.Orleans.MemoryLoadTest -- --config src/Telemetry.Orleans.MemoryLoadTest/appsettings.memoryloadtest.sample.json
```

レポートは既定で `reports/` に出力されます（`TELEMETRY_MEMORY_REPORT_DIR` で上書き可能）。
