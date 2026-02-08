# Orleans Telemetry Connector Sample - Project Overview

このリポジトリは、Orleans を用いたテレメトリ取り込みとグラフ参照のサンプルです。RabbitMQ/Kafka/シミュレータ経由のテレメトリを Orleans の Grain にマッピングし、REST API で最新状態やグラフ情報を取得できるようにしています。RDF からのグラフシードにも対応しています。

## ソリューション構成

- `src/SiloHost`
  - Orleans サイロ本体。Device/Graph/Value などの Grain 実装と Telemetry.Ingest の登録を担当します。
  - `GraphSeedService` が `RDF_SEED_PATH` を読み込み、GraphNode/GraphIndex Grain を初期化します。
- `src/ApiGateway`
  - REST API を提供する ASP.NET Core アプリです。
  - OIDC/JWT 認証を前提にしており、`tenant` claim を `TenantResolver` が解決します。
  - gRPC はスキャフォールドのみで、実サービスはコメントアウトされています。
- `src/Telemetry.Ingest`
  - テレメトリ取り込み基盤。RabbitMQ/Kafka/Simulator コネクタと `TelemetryIngestCoordinator` を提供します。
- `src/Telemetry.Storage`
  - テレメトリ永続化モジュール。取り込んだイベントをステージファイル（JSONL）に書き込み、バックグラウンドで Parquet + インデックスに圧縮します。
  - クエリ API でテナント・デバイス・時間範囲によるテレメトリ検索が可能です。
- `src/Connector`
  - RabbitMQ にデモ用テレメトリを送信するコンソールアプリです。
  - RDF で定義された writable なポイントに対して `telemetry-control` キュー経由で JSON 制御コマンドを受け付け、値を上書きできます。
- `src/DataModel.Analyzer`
  - RDF 解析と BuildingDataModel の構築、Orleans 連携用データ生成を担当します。
- `src/Grains.Abstractions`
  - Grain のインターフェース/キー/契約モデルを集約しています。
- `src/*Tests`
  - `DataModel.Analyzer` と `Telemetry.Ingest` のテストプロジェクトです。

## データフロー概要

1. Telemetry.Ingest が RabbitMQ/Kafka/Simulator からメッセージを受信。
2. `TelemetryRouterGrain` が `DeviceGrain` にルーティングし、最新値を保存。
3. `DeviceGrain` は Orleans Stream (`DeviceUpdates`) にスナップショットを発行。
4. `ParquetTelemetryEventSink` がテレメトリイベントをステージファイル（JSONL）に書き込み、バックグラウンドサービスが定期的に Parquet へ圧縮。
5. REST API (`ApiGateway`) が Grain から最新スナップショットやグラフ情報を取得。クエリ API で過去のテレメトリを Parquet から検索。
6. `GraphSeedService` が RDF を解析し、GraphNode/GraphIndex を構築。

## API と認証

- REST エンドポイントは JWT 必須です。
- `tenant` claim が無い場合は `t1` が使用されます。
- OIDC は `OIDC_AUTHORITY`/`OIDC_AUDIENCE` で設定し、Docker Compose では mock-oidc が同梱されています。

## 主な設定ポイント

- `TelemetryIngest` 設定で有効化コネクタを指定します。
  - `RabbitMq` / `Kafka` / `Simulator` の各オプションを `appsettings.json` などで設定可能です。
- `TelemetryStorage` 設定で永続化パス、バケット間隔、圧縮間隔を制御します。
  - `ParquetStorage` を `EventSinks.Enabled` に追加することでストレージ機能を有効化します。
- RDF シードは `RDF_SEED_PATH` と `TENANT_ID` で制御します。

## テスト体系

### ユニットテスト

#### `DataModel.Analyzer.Tests`
- **RdfAnalyzerServiceTests**: RDF (Turtle形式) の解析とモデル構築をテスト。Site/Building/Level/Space/Equipment/Point の階層構造を検証。
- **RdfAnalyzerServiceShaclTests**: SHACL 検証ルールが正しく適用されることをテスト。
- **LevelModelTests**: Floor/Level の数値化と `EffectiveLevelNumber` ヘルパーをテスト。
- **OrleansIntegrationServiceBindingTests**: RDF モデルから Orleans Grain 初期化データへのマッピングをテスト。

#### `Telemetry.Ingest.Tests`
- **TelemetryIngestCoordinatorTests**: コネクタからのメッセージ受信、ルーティング、バッチ処理をテスト。有効化/無効化されたコネクタの制御を検証。
- **SimulatorIngestConnectorTests**: シミュレータコネクタがテレメトリを正しく生成することをテスト。

#### `ApiGateway.Tests`
- **RegistryEndpointsTests**: Graph Registry サービスがノードタイプ別の検索、ページング、キャッシュを正しく実装していることをテスト。
- **TelemetryExportEndpointTests**: REST API が正しくテレメトリをエクスポート、フォーマット（JSON/CSV）をテスト。
- **GraphRegistryServiceTests**: グラフの検索・フィルタリング機能をテスト。

#### `SiloHost.Tests`
- **GraphIndexGrainTests**: GraphIndexGrain がノードをタイプ別に管理し、追加/削除が正しく動作することをテスト。

#### `Connector.Tests`
- **RdfTelemetryGeneratorTests**: RDF モデルからテレメトリメッセージを生成する処理をテスト。Device/Point のマッピング、値範囲の尊重、メタデータの埋め込みを検証。

#### `Telemetry.Storage.Tests`
- **ParquetTelemetryStorageTests**: テレメトリイベントのステージファイル書き込み、Parquet 圧縮、インデックス構築をテスト。

### E2E テスト

#### `Telemetry.E2E.Tests`
- **TelemetryE2ETests**: エンド・ツー・エンドの統合テスト。Orleans Silo/API Gateway を起動し、RDF シードの読み込み、テレメトリインジェスト、REST API からのデータ取得を検証。
  - RDF シード (`seed.ttl`) を使用した初期化
  - シミュレータからのテレメトリ生成・取り込み
  - Grain 状態の更新確認
  - REST API による最新値取得・グラフ検索
  - レポート生成（JSON/Markdown）
- **TelemetryExportServiceTests**: テレメトリデータのエクスポート形式（JSON/CSV）とメタデータの検証。

### ロードテスト・メモリテスト

- **Telemetry.Ingest.LoadTest**: RabbitMQ/Kafka/Simulator を使用した高スループットテレメトリ取り込みのベンチマーク。バックプレッシャー、スループット、レイテンシを測定。
- **Telemetry.Orleans.MemoryLoadTest**: Orleans Grain のメモリ使用量と GC 圧力のプロファイリング。大規模デバイス数・ポイント数での安定性を検証。

## 現状の制約

- gRPC サービスはスキャフォールドのみで、実装はコメントアウトされています。
- Control Flow はインターフェース定義のみで、制御 Grain/エグレス連携は未実装です。
