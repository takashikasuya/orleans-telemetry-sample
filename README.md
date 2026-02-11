# Orleans Telemetry Sample

Orleans を使ったテレメトリー取り込みサンプルです。RabbitMQ / Kafka / Simulator から受信したメッセージを Grain にルーティングし、最新値を API で参照、履歴を Parquet に保存して検索できます。

## Quick Start

```bash
docker compose up --build
```

起動後の主要 URL:
- Swagger: http://localhost:8080/swagger
- Mock OIDC: http://localhost:8081/default
- Admin UI: http://localhost:8082/
- Telemetry Client: http://localhost:8083/

## System Overview

```mermaid
flowchart LR
    Pub[Publisher/Connector] --> MQ[(RabbitMQ/Kafka/Simulator)]
    MQ --> Silo[SiloHost (Orleans)]
    Silo --> API[ApiGateway (REST/gRPC)]
    Silo --> Storage[(Parquet + Index)]
    API --> Client[Telemetry Client / Admin]
```

## Repository Structure

- `src/SiloHost`: Orleans サイロ（ingest / grain / graph seed）
- `src/ApiGateway`: REST/gRPC API
- `src/Telemetry.Ingest`: コネクタ/バッチ取り込み
- `src/Telemetry.Storage`: JSONL ステージ + Parquet compaction
- `src/AdminGateway`: 運用向け管理 UI
- `src/TelemetryClient`: 階層ツリー/ポイント閲覧 UI
- `src/Publisher`: サンプルテレメトリー送信

## Documentation Map

概要と導入:
- [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md)
- [ローカルセットアップ & オペレーションガイド](docs/local-setup-and-operations.md)

機能別ドキュメント:
- [API Gateway API](docs/api-gateway-apis.md)
- [コネクタ & テレメトリーインジェスト](docs/telemetry-connector-ingest.md)
- [ルーティングと値バインディング](docs/telemetry-routing-binding.md)
- [テレメトリーストレージ](docs/telemetry-storage.md)
- [RDF ロードと Grain 初期化](docs/rdf-loading-and-grains.md)
- [Admin Console](docs/admin-console.md)
- [Telemetry Client Spec](docs/telemetry-client-spec.md)
- [負荷試験ガイド](docs/telemetry-ingest-loadtest.md)
- [OpenTelemetry 運用メモ](docs/observability-opentelemetry.md)

## Build & Test

```bash
dotnet build
dotnet test
```

## Notes

- このリポジトリはローカル検証・学習用途のサンプルです（本番運用向けの堅牢化は未実装）。
- gRPC は構成を含みますが、実装は一部スキャフォールド段階です。

## License

MIT License. 詳細は [LICENSE](LICENSE) を参照してください。
