# E2Eテスト内容レビュー（必要十分性の分析）

## Purpose
`src/Telemetry.E2E.Tests` の E2E テストが、テレメトリー取り込みパイプラインの回帰検知として「必要十分」かを評価する。

## 対象
- `TelemetryE2ETests.EndToEndReport_IsGenerated`
- `TelemetryE2ETests.RdfPublisherTelemetry_IsVisibleThroughApi`
- `TelemetryExportServiceTests.CreateExportAsync_WritesMetadataAndData`

## 現在のテストで担保できていること（十分な点）

### 1) Ingest → Orleans → API の主経路
- インプロセスで SiloHost を起動し、Simulator ingest を有効化して実データを投入している。
- API (`/api/nodes/{id}`, `/api/nodes/{id}/value`, `/api/devices/{id}`) で最終状態を確認している。
- `PointLagMilliseconds` に上限（設定値）を設け、可観測な遅延上限を検証している。

### 2) RDF seed とグラフ属性の整合
- `seed.ttl` を読み込み、`urn:point-1` の属性取得を待機。
- 取得した属性の `PointId` / `DeviceId` が ingest 実イベントと一致することを検証。

### 3) ストレージ経路（Stage/Parquet/Index）
- Stage(JSONL) の作成を待機してレコードを読み取り。
- Compactor を明示実行し、Parquet/Index ファイルの存在を検証。
- `/api/telemetry/{deviceId}` から検索結果が返ることを検証（inline/url 両モードの読み取り実装あり）。

### 4) RDF 生成テレメトリーの反映
- `RdfTelemetryGenerator` で作成したポイント値を `ITelemetryRouterGrain.RouteAsync` に投入し、API 側で反映を確認。
- メタデータ（`PointType`）が保持されることも確認。

### 5) エクスポート機能の基本ライフサイクル
- `TelemetryExportService` 単体で、
  - エクスポート生成
  - メタデータ参照
  - ストリーム読取
  - TTL 後クリーンアップ
  を検証。

## 不足している点（十分ではない点）

### A. 実運用トポロジに近い「外部依存込み」E2Eが薄い
- 現在の E2E は主に in-proc 構成で、RabbitMQ/Kafka 実コネクタの疎通は未検証。
- `scripts/run-e2e.sh` には Docker モードがあるが、CI で必須化される前提が明確でない。

### B. 認証の実 E2E が未カバー
- E2E API は `TestAuthHandler` で差し替えており、OIDC/JWT の end-to-end 経路（token発行→Bearer付与→tenant解決）は未検証。

### C. gRPC 経路は未対象
- REST 経路中心で、gRPC エンドポイントの接続性/契約確認は E2E で未実施。

### D. 失敗系・境界系の網羅が不足
- 無効 tenant / 存在しない point / 時間範囲外 / 空結果時の契約などの negative path が弱い。
- 冪等性（重複 sequence）や順序乱れに対する期待挙動は E2E として未定義。

### E. スループット・負荷・安定性は別テストに分離され、E2Eには含まれない
- これは設計上妥当だが、「E2Eだけで十分」とは言えない。

## 判定
- **機能スモークとしては十分（○）**
  - 主経路（seed→ingest→grain→storage→api）を1本通して検証できている。
- **リリース品質ゲートとしては不十分（△）**
  - 認証実経路、外部ブローカ実体、negative path、gRPC などが不足。

## 推奨（優先順）

1. **Docker E2E（mq + silo + api + mock-oidc）を定期実行に追加**
   - 最低 1 ケースで token 取得→Bearer 呼び出しまでを自動化。
2. **Negative E2Eを2〜3本追加**
   - 例: 未認証 401、存在しない node 404、空時間範囲 200+empty。
3. **Export API のE2E化**
   - `TelemetryExportServiceTests` は現状サービス単体寄りのため、API経由テストを1本追加。
4. **gRPC が有効化されたタイミングで parity テスト追加**
   - REST と同等の最小契約確認を実施。

## 受け入れ観点（必要十分の目安）
- 最低限（現状）:
  - 主データ経路の成功確認 + ストレージ生成 + API読み出し
- 追加で必要（品質ゲート）:
  - 認証実経路、外部依存実体、主要 negative path、（将来）gRPC parity

