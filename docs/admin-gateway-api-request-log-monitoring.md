# AdminGateway: API Gatewayリクエスト監視とログ検索の検討

## 目的

AdminGateway で以下を実現するための設計方針を整理する。

1. API Gateway へのリクエストをリアルタイム監視する。
2. 過去ログをファイル保存し、検索・閲覧できる。
3. 可能な限り既存 sink モジュール (`ITelemetryEventSink`) をそのまま利用する。

---

## 現状整理（既存機能の再利用可能性）

- `Telemetry.Ingest` には `TelemetryEventEnvelope` があり、`EventType.Log` と `Severity` を保持できる。
- `Telemetry.Storage` の `ParquetTelemetryEventSink` は `ITelemetryEventSink` 実装で、stage(JSONL) → parquet/index への既存導線を持つ。
- `AdminGateway` は既に SignalR (`TelemetryHub`) を持っているため、リアルタイム画面は同様の方式で拡張しやすい。

> 結論: 「ログ保存」については新規ストレージを増やさず、`EventType.Log` と既存 sink を流用するのが最小変更で実現可能。

---

## 要件の分解

### A. リアルタイム監視

- 監視対象: API Gateway の HTTP request/response（最低限: method, path, status, latency, tenant, traceId）
- 表示要件:
  - 新着順ストリーム
  - エラー（4xx/5xx）を強調
  - tenant/path/status でクイックフィルタ

### B. ログ保存

- 保存要件:
  - 監査用途に使える時刻/テナント/結果コード/トレースID
  - 高頻度でも書き込み負荷を平準化
  - 既存 sink を優先利用

### C. 検索・閲覧

- 主な検索軸:
  - 期間
  - tenant
  - path prefix
  - status code range
  - free text（message/payload）
- UI:
  - 一覧＋詳細ペイン
  - CSV/JSON エクスポート（将来）

---

## 推奨アーキテクチャ（既存 sink 再利用）

## 1) ApiGateway に RequestLog ミドルウェアを追加

- `app.Use(...)` で request 開始〜終了を計測。
- response 完了時に `TelemetryEventEnvelope` を生成。
  - `EventType = TelemetryEventType.Log`
  - `Severity = status >= 500 ? Error : status >= 400 ? Warning : Information`
  - `DeviceId` は固定（例: `api-gateway`）
  - `PointId` は固定（例: `http-request`）
  - `Tags` に `method`, `path`, `status`, `traceId`, `tenant` などを格納
  - 詳細は `Payload`（JSON）へ格納

## 2) 既存 `ITelemetryEventSink` に書き込み

- `TelemetryIngestCoordinator` と同様に、有効化された sink に非同期で書き込む。
- 最初は `ParquetStorage` を有効化し、必要なら `Logging` sink 併用。
- これにより stage/parquet/index まで既存導線を再利用可能。

## 3) AdminGateway のリアルタイム表示

- ApiGateway 側で SignalR Hub または Server-Sent Events を公開。
- AdminGateway は購読クライアントとして接続し、直近 N 件を表示。
- SignalR 採用時は現行 `TelemetryHub` と同じ運用モデルで統一可能。

## 4) AdminGateway の検索 API

- まずは「stage(JSONL) スキャン」で実装し、短期導入。
- データ量増加後は parquet/index を利用したクエリへ段階移行。
- 既存 `TelemetryStorageOptions` のパス設定を共用し、運用一貫性を維持。

---

## データモデル案（ログイベント）

```json
{
  "tenantId": "default",
  "deviceId": "api-gateway",
  "pointId": "http-request",
  "eventType": "Log",
  "severity": "Information",
  "occurredAt": "2026-03-05T10:00:00Z",
  "tags": {
    "method": "GET",
    "path": "/api/telemetry/DEV001",
    "status": "200",
    "traceId": "00-abc...",
    "tenant": "default"
  },
  "payload": {
    "durationMs": 18.2,
    "query": "limit=100",
    "user": "sub:operator-01"
  }
}
```

---

## 代替案比較

- 案1（推奨）: `TelemetryEventEnvelope(Log)` + 既存 sink
  - 長所: 実装差分が小さい、保守先が既存に集約、保存形式が統一
  - 短所: HTTPログ向け専用インデックスは別途検討が必要

- 案2: Serilog など専用 file sink を新規導入
  - 長所: ログ用途として成熟
  - 短所: 保存経路が二重化し、既存 telemetry storage と分断

- 案3: OpenTelemetry Logs を collector 経由で外部基盤へ送る
  - 長所: 標準化、外部ツール活用
  - 短所: このリポジトリ方針（ローカル検証中心）では初期導入コストが高い

---

## 段階的実装ステップ（提案）

### Phase 1（最小実装）

1. ApiGateway request logging middleware 追加
2. `EventType.Log` を `ParquetStorage` sink へ出力
3. AdminGateway で「最新ログ一覧（リアルタイム）」を表示

### Phase 2（検索強化）

1. 期間/tenant/path/status フィルタ API
2. Admin UI に検索フォーム＋ページング
3. エラー率・上位 path 集計の簡易可視化

### Phase 3（運用強化）

1. parquet/index クエリ最適化
2. 長期保存ポリシー（圧縮/保持期間）
3. 必要時のみ OpenTelemetry collector 連携

---

## 注意点

- 個人情報や機微情報（Authorization header, token, payload 生値）は必ずマスクする。
- 高カーディナリティ値（full query, user-agent 全文）は `Tags` ではなく `Payload` に寄せる。
- ログ量過多を防ぐため、成功系はサンプリング可能な設計にしておく。

---

## まとめ

- 要望（リアルタイム監視＋保存検索）は、既存 `ITelemetryEventSink` を活用した設計で実現可能。
- 特に `TelemetryEventEnvelope` の `EventType.Log` と `ParquetTelemetryEventSink` の再利用により、保存導線を新設せず段階導入できる。
- 次アクションは Phase 1 実装（middleware + sink 出力 + AdminGateway 最小ビュー）が妥当。
