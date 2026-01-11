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
