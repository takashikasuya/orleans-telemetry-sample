# Orleans Telemetry Sample - Project Overview

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
- `src/Publisher`
  - RabbitMQ にデモ用テレメトリを送信するコンソールアプリです。
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

## 現状の制約

- gRPC サービスはスキャフォールドのみで、実装はコメントアウトされています。
- Control Flow はインターフェース定義のみで、制御 Grain/エグレス連携は未実装です。
