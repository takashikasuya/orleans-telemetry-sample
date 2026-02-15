# Telemetry.Ingest.LoadTest 実行手順

テレメトリーインジェストのバックプレッシャー/バッチ動作をローカルで検証するための負荷試験ツールです。既定のステージを連続実行し、Markdown と JSON のレポートを `reports/` 配下へ書き出します。

## 前提
- 実行ディレクトリはリポジトリルート。
- .NET 8 SDK がインストール済み。
- **RabbitMQ を必ず起動しておくこと。** RabbitMQ を使うステージ（`--soak`/`--spike`/`--multi-connector`）に限らず、RabbitMQ コネクタを含むシナリオでは起動済みである必要があります。未起動の場合、コネクタ接続で失敗します。
	- ローカルで簡単に立ち上げる例:
		```bash
		# 管理 UI は http://localhost:15672/ （guest/guest）
		docker compose up -d mq
		```
	- 環境変数 `RABBITMQ_HOST`/`RABBITMQ_PORT`/`RABBITMQ_USER`/`RABBITMQ_PASS` を必要に応じて設定してください（未設定時は `mq:5672`、`user`/`password` を利用）。

## クイックスタート
```bash
# 既定ステージ（baseline/ramp/overload）を実行
dotnet run --project src/Telemetry.Ingest.LoadTest
```
- 実行後、`reports/telemetry-ingest-backpressure-YYYYMMDD-HHMMSS.md` と同名の JSON が生成されます。

## 主なオプション
- `--quick` : ステージ時間を短縮した簡易実行。
- `--output-dir <path>` : レポート出力先を変更（既定 `reports`）。
- `--config <path>` : 追加の設定ファイルを読み込む（既定 `appsettings.loadtest.json`）。`RabbitMq` セクションを `RabbitMqIngestOptions` にバインドします。
- `--batch-sweep` : バッチサイズのスイープステージを追加。
- `--abnormal` : シンク障害/コネクタ強制停止ステージを追加。
- `--soak` : RabbitMQ ソーク（長時間）ステージを追加。
- `--spike` : RabbitMQ スパイク（短時間高負荷）ステージを追加。
- `--multi-connector` : LoadTest コネクタと RabbitMQ コネクタを混在させたステージを追加。

複数オプションは組み合わせ可能です。

## 追加の実行例
```bash
# 短時間で概要を掴む
dotnet run --project src/Telemetry.Ingest.LoadTest -- --quick

# 設定ファイルを指定（RabbitMq セクションを使用）
dotnet run --project src/Telemetry.Ingest.LoadTest -- --config appsettings.loadtest.json

# バッチサイズの影響を比較
dotnet run --project src/Telemetry.Ingest.LoadTest -- --batch-sweep

# RabbitMQ を使ったスパイク試験（RabbitMQ が起動していること）
dotnet run --project src/Telemetry.Ingest.LoadTest -- --spike

# 複数ステージをまとめて実施（クイック + バッチスイープ + 異常系）
dotnet run --project src/Telemetry.Ingest.LoadTest -- --quick --batch-sweep --abnormal
```

## 設定ファイル例（`appsettings.loadtest.json`）
```json
{
	"RabbitMq": {
		"HostName": "localhost",
		"Port": 5672,
		"UserName": "guest",
		"Password": "guest",
		"QueueName": "telemetry",
		"PrefetchCount": 200
	}
}
```
環境変数も併せて読み込みます（キーが重複する場合は環境変数が優先されます）。
- サンプルを同梱しています: `src/Telemetry.Ingest.LoadTest/appsettings.loadtest.sample.json`

## 出力について
- Markdown レポート: `telemetry-ingest-backpressure-<RunId>.md`
- JSON レポート: `telemetry-ingest-backpressure-<RunId>.json`
- `RunId` は UTC 時刻の `yyyyMMdd-HHmmss` 形式。
- レポートにはステージ別のスループット、待ち時間統計、ステージ構成が含まれます。


## Docker Compose ローカルクラスタでの性能比較（1 Silo vs 複数 Silo）

本リポジトリの負荷試験は、以下 2 シナリオを同一条件で比較する運用を推奨します。

- **Scenario A (Baseline)**: `docker-compose.yml` の単一 `silo`
- **Scenario B (Scale-out)**: `docker-compose.silo-multi.yml` 併用で複数 `silo-*`

### 比較手順（推奨）

1. クラスタ起動
   - 単一 Silo: `docker compose up --build -d`
   - 複数 Silo: `docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml up --build -d`
2. 同一パラメータで `Telemetry.Ingest.LoadTest` を実行
3. 生成される `reports/*.md` / `reports/*.json` をシナリオ別に保存
4. 下記 KPI を横並び比較

### KPI（最低限）

- Throughput (events/sec)
- End-to-end latency（p50/p95/p99）
- Backpressure 発生率（ステージ内の遅延増加・滞留）
- エラー率（ingest failure, connector failure）

### 公平な比較のための固定条件

- RabbitMQ キュー名・prefetch・publisher レートを固定
- RDF seed（デバイス/ポイント数）を固定
- 試験時間（warm-up / measure / cool-down）を固定
- 実行ホスト（CPU/メモリ制限）を固定

### 判定例

- **スケール有効**: 複数 Silo で Throughput が増加し、p95/p99 が悪化しない
- **改善余地あり**: Throughput 増加が小さく、p95/p99 が大幅悪化
- **要調査**: 複数 Silo でエラー率やバックプレッシャーが増加

### 追加観測（任意）

- `docker stats` によるコンテナ別 CPU/Memory
- Orleans membership 変化（起動/停止時）
- API 側の REST/gRPC 応答時間の変化
