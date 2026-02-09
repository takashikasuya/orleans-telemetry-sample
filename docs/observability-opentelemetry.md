# OpenTelemetry Collector を使ったシステム監視方針

本ドキュメントは、OpenTelemetry Collector を中心に据えた監視方針を整理したものです。
監視ツール（可視化/アラート基盤）は後段で選定し、Collector からの export を前提にします。

## 目的

- モジュールごとの問題を **早く・局所的に** 特定できるようにする。
- シグナル過多を避け、**最小限の指標**で異常検知と原因切り分けを可能にする。
- 開発/検証環境でも手軽に動かせる構成にする。

## 対象モジュール

- mq (RabbitMQ)
- silo (SiloHost / Orleans + ingest + storage)
- api (ApiGateway)
- admin (AdminGateway)
- publisher (Publisher)
- telemetry-client (TelemetryClient)

## 基本方針（過剰にしないための原則）

1. **ゴールデンシグナル優先**: 
   - レイテンシ / エラー率 / トラフィック / 飽和（飽和は CPU/メモリ/キュー長のみに限定）
2. **ログは WARN 以上**を基準に収集し、INFO は必要時に限定。
3. **トレースは低サンプリング**で常時、障害時にサンプリング率を上げる。
4. **モジュール固有メトリクスは 2〜4 個まで**に限定。
5. **タグ（属性）は最小限**:
   - `service.name`, `service.version`, `deployment.environment`, `tenant`（ある場合）
   - `deviceId` など高カーディナリティはログ/トレースに限定。

## OpenTelemetry Collector の配置イメージ

- **各サービス横に sidecar** もしくは **中央 Collector** を配置。
- 収集方式は以下を想定:
  - **Metrics**: OTLP (gRPC/HTTP) / Prometheus scrape
  - **Logs**: OTLP or filelog receiver
  - **Traces**: OTLP
- 出力先（observability backend）は後段で差し替え可能な exporter を想定。

## 具体設計（Collector 構成方針）

### 収集経路

- .NET サービス（silo/api/admin/publisher/telemetry-client）は **OTLP (gRPC/HTTP)** で Collector に送信。
- RabbitMQ は **Prometheus scrape** で Collector に収集。
- コンテナログは **filelog receiver** で WARN/ERROR を中心に収集（環境次第で省略可）。

### Collector パイプライン（最小構成の雛形）

> 実際の exporter は後段の監視基盤に合わせて差し替え可能。
> リポジトリには最小構成の設定ファイル (`config/otel-collector.yaml`) を追加している。

```yaml
receivers:
  otlp:
    protocols:
      grpc:
      http:
  prometheus:
    config:
      scrape_configs:
        - job_name: rabbitmq
          static_configs:
            - targets: ["mq:15692"]
  filelog:
    include: ["/var/log/app/*.log"]
    start_at: end

processors:
  memory_limiter:
    limit_mib: 512
  batch:
    send_batch_size: 1024
    timeout: 5s
  resourcedetection:
    detectors: [env, system]
  attributes:
    actions:
      - key: deployment.environment
        from_attribute: env
        action: upsert

exporters:
  logging:
    verbosity: normal

service:
  pipelines:
    metrics:
      receivers: [otlp, prometheus]
      processors: [memory_limiter, batch, resourcedetection]
      exporters: [logging]
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch, resourcedetection]
      exporters: [logging]
    logs:
      receivers: [otlp, filelog]
      processors: [memory_limiter, batch, resourcedetection, attributes]
      exporters: [logging]
```

> 監視基盤を導入する場合は `exporters` に `otlp` を追加し、
> `exporters: [logging]` を `exporters: [otlp]` に置き換える。

### Docker Compose での起動（任意）

Collector の起動は `docker-compose.observability.yml` を重ねて行う。

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d otel-collector
```

> Collector は OTLP (4317/4318) を公開するため、.NET 側の OTLP 出力を有効化する場合は
> `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` を各サービスに設定する。
> 現時点では SDK 導入は行っていないため、Collector 側の受信のみを先に用意する設計とする。

### リソース属性（共通）

最小セットのみ付与し、カーディナリティを抑える。

- `service.name`（例: `silo`, `api`）
- `service.version`
- `deployment.environment`（例: `local`, `dev`）
- `tenant`（存在する場合のみ）

## モジュール別の計測ポイント（具体）

### mq (RabbitMQ)
**収集方法**: Prometheus scrape（Management plugin / 15692）。

- キュー滞留: `rabbitmq_queue_messages_ready`
- ACK 遅延: `rabbitmq_queue_messages_unacknowledged`
- 消費者数: `rabbitmq_channel_consumers`
- メモリ飽和: `rabbitmq_node_mem_used`

### silo (SiloHost / Orleans + ingest + storage)
**収集方法**: .NET OTLP + Prometheus scrape（Orleans 露出がある場合）。

- Orleans 呼び出し: `orleans_silo_request_duration`, `orleans_silo_failed_requests`
- Ingest バッチ: `telemetry_ingest_batch_size`, `telemetry_ingest_batch_latency`
- Storage compaction: `telemetry_storage_compaction_duration`

ログは ingest デシリアライズ失敗/ルーティング失敗/書込失敗を WARN/ERROR で明示。

### api (ApiGateway)
**収集方法**: .NET OTLP。

- HTTP 基本: `http.server.duration`, `http.server.request_count`, `http.server.error_count`
- Orleans 依存: `orleans_client_request_duration`

### admin (AdminGateway)
**収集方法**: .NET OTLP。

- HTTP 基本のみ（`http.server.duration`, `http.server.error_count`）

### publisher
**収集方法**: .NET OTLP（メトリクスのみでも可）。

- `telemetry_published_count`
- `telemetry_publish_failures`

### telemetry-client
**収集方法**: .NET OTLP（サーバー側のみ）。

- `http.client.duration`（API 呼び出しの遅延）

## 収集とサンプリング方針

- **トレースのサンプリング**: 1% から開始。
  - エラー率上昇時は 5〜10% まで引き上げ。
- **メトリクス周期**: 30s 〜 60s。
- **ログ保持**: WARN/ERROR のみ長期保存。INFO は短期（数日）でローテーション。

### サンプリング運用の具体案

- 既定: `parentbased_traceidratio` で 1%。
- エラー率が閾値超過時のみ 10% に引き上げ。
- 高カーディナリティ属性（`deviceId` など）は **トレースのイベント/ログ**に限定し、メトリクスには載せない。

## アラート設計（例）

> 監視ツール選定後に閾値は調整。

- **mq キュー滞留**: `messages_ready` が 5 分連続増加
- **silo ingest 遅延**: `telemetry_ingest_batch_latency` の p95 が閾値超過
- **silo 失敗率**: `orleans_silo_failed_requests` が一定割合超過
- **api 5xx 増加**: `http.server.error_count` 比率超過
- **storage compaction 停滞**: `telemetry_storage_compaction_duration` の長期化

## ダッシュボード構成（最小）

1. **Ingest & MQ Overview**: MQ キュー長 / ingest レイテンシ / batch size
2. **Orleans Health**: Grain 呼び出し遅延 / 失敗率 / メモリ
3. **API Health**: レイテンシ / エラー率 / リクエスト数
4. **Storage Health**: compaction duration / bucket backlog

## 運用メモ

- 初期は **Collector 側でメトリクス/ログを drop 可能**にして過剰収集を防止する。
- 新しい指標は「問題が起きた後に追加」する方針とし、初期セットを肥大化させない。
- 変更後は、モジュール単位で「検知→原因特定」の時間が短縮されたかレビューする。
