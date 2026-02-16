# Telemetry Storage: BigQuery Sink 拡張設計案

## 目的
既存の Parquet ストレージを残したまま、新しい保存先として Google BigQuery を選択可能にする。あわせて、BigQuery へ保存したテレメトリを API から取得できるようにする。

---

## 現状整理（既存実装）
- **書き込み経路**: `TelemetryIngestCoordinator` -> `ITelemetryEventSink`（複数実装）
  - 既存実装: `ParquetTelemetryEventSink`, `LoggingTelemetryEventSink`
- **検索経路**: `ITelemetryStorageQuery`
  - 既存実装: `ParquetTelemetryStorageQuery`
- **有効 sink 制御**: `TelemetryIngest:EventSinks:Enabled` で `Name` 一致の実装を有効化

このため、**sink の追加は容易**だが、**query の切替は設計追加が必要**。

---

## ゴール
1. `ParquetStorage` を既定のまま維持する。
2. `BigQueryStorage` を sink として追加し、設定で有効化できる。
3. `BigQueryTelemetryStorageQuery` を追加し、設定で検索バックエンドを選択できる。
4. 段階導入（dual-write -> query 切替）が可能で、既存 API 契約は維持する。

---

## 提案アーキテクチャ

### 1) Sink 側（書き込み）
新規追加:
- `BigQueryTelemetryEventSink : ITelemetryEventSink`
  - `Name => "BigQueryStorage"`
  - 受信した `TelemetryEnvelope` を BigQuery へ投入

選択方式:
- 既存の `TelemetryIngest:EventSinks:Enabled` をそのまま利用
  - 例1: `['ParquetStorage']`（現状互換）
  - 例2: `['ParquetStorage', 'BigQueryStorage']`（dual-write）
  - 例3: `['BigQueryStorage']`（BigQuery 単独）

### 2) Query 側（取得）
新規追加:
- `BigQueryTelemetryStorageQuery : ITelemetryStorageQuery`
- `TelemetryStorageQueryRouter : ITelemetryStorageQuery`
  - 設定値に応じて実クエリ実装へ委譲

提案設定:
```json
{
  "TelemetryStorage": {
    "QueryProvider": "Parquet" // Parquet | BigQuery | Hybrid
  }
}
```

委譲ルール:
- `Parquet`: `ParquetTelemetryStorageQuery`
- `BigQuery`: `BigQueryTelemetryStorageQuery`
- `Hybrid`:
  - 原則 BigQuery を優先
  - 必要に応じて「最新 N 分は Parquet、過去は BigQuery」等のポリシーを将来拡張

---

## BigQuery データモデル案

テーブル例: `telemetry_events`

| 列名 | 型 | 説明 |
|---|---|---|
| tenant_id | STRING | テナント |
| device_id | STRING | デバイス |
| point_id | STRING | ポイント |
| occurred_at | TIMESTAMP | 発生時刻 (UTC) |
| value_number | FLOAT64 | 数値値（数値の場合） |
| value_text | STRING | 非数値値（文字列化） |
| quality | STRING | 品質コード |
| trace_id | STRING | トレース相関（任意） |
| metadata_json | JSON | 拡張属性 |
| ingested_at | TIMESTAMP | 取込時刻 |

パーティション/クラスタリング:
- **Partition**: `DATE(occurred_at)`
- **Cluster**: `(tenant_id, device_id, point_id)`

これにより時間範囲＋デバイス/ポイント検索のスキャン量を削減する。

---

## 書き込み方式（BigQuery）

### 推奨: Storage Write API
- 高スループット、低遅延、重複排除（オプション）に対応
- sink で小バッチ化して送信（例: 200〜1000件/flush）

代替: InsertAll
- 実装は容易だが、高負荷時や厳密な重複制御に弱い
- PoC では可、本番想定では Write API を推奨

### 再試行/失敗時
- 指数バックオフ + ジッタ
- 永続失敗時はローカル退避（DLQ ファイル）を検討
- sink 失敗は既存方針どおり ingest を止めない（ログ＋メトリクス）

---

## 取得方式（BigQuery）

`TelemetryQueryRequest` を SQL にマッピング:
- tenant/device/time range 必須条件を `WHERE` に反映
- point 指定は `IN UNNEST(@point_ids)`
- `LIMIT` を既存 API の上限仕様に準拠
- 並び順は `occurred_at ASC` を基本

SQL 例（概念）:
```sql
SELECT tenant_id, device_id, point_id, occurred_at, value_number, value_text, quality, metadata_json
FROM `project.dataset.telemetry_events`
WHERE tenant_id = @tenant_id
  AND device_id = @device_id
  AND occurred_at >= @from_utc
  AND occurred_at < @to_utc
  AND (@point_ids_is_empty OR point_id IN UNNEST(@point_ids))
ORDER BY occurred_at ASC
LIMIT @limit;
```

---

## 設定モデル案

`TelemetryStorage:BigQuery` セクションを追加:

```json
{
  "TelemetryStorage": {
    "QueryProvider": "Parquet",
    "BigQuery": {
      "ProjectId": "my-gcp-project",
      "DatasetId": "telemetry",
      "TableId": "telemetry_events",
      "Location": "asia-northeast1",
      "UseStorageWriteApi": true,
      "BatchSize": 500,
      "FlushIntervalMs": 1000,
      "MaxRetries": 5
    }
  }
}
```

認証:
- ローカル: `GOOGLE_APPLICATION_CREDENTIALS`（サービスアカウント JSON）
- GCP 実行環境: Workload Identity / Default Credentials

必要 IAM（最小権限）:
- 書き込み: `bigquery.dataEditor`（対象 dataset 限定）
- 読み取り: `bigquery.dataViewer` + `bigquery.jobUser`

---

## DI/実装差し込み案

1. `TelemetryStorageServiceCollectionExtensions` に以下を追加
   - `BigQueryTelemetryEventSink` を `ITelemetryEventSink` として登録
   - `ParquetTelemetryStorageQuery`, `BigQueryTelemetryStorageQuery`, `TelemetryStorageQueryRouter` を登録
2. `ITelemetryStorageQuery` の公開実装を Router に統一
3. Router が `QueryProvider` を見て委譲

互換性:
- `QueryProvider` 未設定時は `Parquet` 扱い（既存挙動維持）

---

## 導入手順（段階的移行）

### Phase 1: 実装追加（未使用）
- BigQuery sink/query を実装・登録
- 既定設定は Parquet のまま

### Phase 2: Dual-write
- `EventSinks.Enabled` に `BigQueryStorage` を追加
- 書き込み成功率、遅延、コストを観測

### Phase 3: Query 切替検証
- `QueryProvider=BigQuery` を検証環境で有効化
- API レスポンス整合（件数/時刻/point filter）を比較

### Phase 4: 本番運用方針確定
- Parquet 継続（バックアップ用途）か、BigQuery 集約かを決定

---

## 検証計画

### 自動テスト
- `BigQueryTelemetryEventSinkTests`
  - 変換（Envelope -> Row）
  - バッチング・再試行
- `BigQueryTelemetryStorageQueryTests`
  - `TelemetryQueryRequest` -> SQL/パラメータ変換
  - レスポンスマッピング
- `TelemetryStorageQueryRouterTests`
  - QueryProvider 切替挙動

### 統合検証（手動）
1. dual-write で Parquet/BigQuery 両方に同一データが入ること
2. 同一条件のクエリで Parquet と BigQuery の結果整合を確認
3. 負荷時の遅延、BigQuery スキャン量、コスト見積を取得

---

## リスクと対策

- **コスト増**: クエリのフルスキャン
  - 対策: partition/clustering、必須フィルタ、`LIMIT` 強制
- **遅延増**: 書き込み API のスロットリング
  - 対策: バッチサイズ調整、再試行制御、メトリクス監視
- **重複/欠損**: ネットワーク断・再送
  - 対策: idempotency key、DLQ、整合性監査ジョブ
- **運用複雑化**: 2系統保守
  - 対策: Phase ごとの運用基準とロールバック手順を定義

---

## Open Questions
1. `Hybrid` の厳密仕様（時間分割/フェイルオーバー）をどこまで初期実装に含めるか。
2. value の型表現（`value_number` + `value_text` 以外に JSON 単一列を許容するか）。
3. データ保持方針（BigQuery の partition expiration と Parquet 保持期間の整合）。

