# Repository Backlog (2026-03 Audit)

この backlog は 2026-03-15 時点のリポジトリ監査から抽出した Issue 候補一覧です。
GitHub Issue 化しやすいように、各候補を `type: concise outcome` 形式の title で整理しています。

## Selection Rules

- 既存ドキュメントやコード上で、未実装・設計案・運用ギャップとして明示されているものを優先
- 1 つの Issue で追える粒度へ分解
- 広すぎるものは Epic 候補として扱う

## Recommended First Issues

### 1. `feat: deliver control commands from ApiGateway to connector egress`

- Issue: `#60`
- Priority: High
- Area: `area:api`, `area:ingest`
- Why:
  - 遠隔制御は受付とルーティング決定までは実装済みだが、実際の配送が未実装
  - README / PROJECT_OVERVIEW 上でも機能ギャップとして明示されている
- Evidence:
  - `docs/api-gateway-remote-control.md`
  - `PROJECT_OVERVIEW.md`
  - `README.md`
- Scope:
  - `ConnectorName` に応じた egress 実装を追加
  - RabbitMQ を最初の対象として最小実装
  - 配送失敗のログと基本的な result 反映を追加
- Done shape:
  - `POST /api/devices/{deviceId}/control` の accepted 要求が実際に connector 側へ配送される
  - 最低 1 系統の自動テストとローカル検証手順がある

### 2. `feat: expose control command status and history endpoint`

- Issue: `#61`
- Priority: High
- Area: `area:api`
- Why:
  - `Location` ヘッダ先の取得 API が未実装で、受理後の追跡ができない
  - `ControlRequestStatus` も `Accepted` 止まりで、運用上の完結性が不足
- Evidence:
  - `docs/api-gateway-remote-control.md`
- Scope:
  - `GET /api/devices/{deviceId}/control/{commandId}` を追加
  - `Applied` / `Failed` などの状態遷移モデルを定義
  - 必要なら control history 一覧 API を別 issue で切る
- Done shape:
  - accepted 時に返した `Location` から状態参照できる
  - API contract とテストが追加される

### 3. `feat: add hierarchical RBAC and ABAC for OIDC users`

- Issue: `#62`
- Priority: High
- Type: Epic candidate
- Area: `area:api`, `area:admin`
- Why:
  - 現状は tenant 分離のみで、README でも権限制御が弱いと明記されている
  - 設計ドラフトがすでにあり、Issue 化しやすい段階にある
- Evidence:
  - `docs/oidc-hierarchical-authorization-design.md`
  - `README.md`
- Scope:
  - 内部ユーザー、ロール、権限、リソース階層の永続化モデル
  - API / gRPC / Admin UI で共通認可判定
  - Admin UI での権限管理
- Suggested split:
  - `#69 feat: add authorization schema and migrations for OIDC RBAC`
  - `#70 feat: implement OIDC user sync and shared authorization service`
  - `#71 feat: enforce hierarchical authorization in API and gRPC endpoints`
  - `#72 feat: add Admin UI workflows for scoped role assignment`
  - `#73 feat: add audit logging for authorization changes`

### 4. `test: add RDF-to-Admin UI tree verification suite`

- Issue: `#63`
- Priority: Medium
- Area: `area:admin`, `area:tests`
- Why:
  - RDF から Graph Grain を生成して UI までつながる経路は壊れやすいが、専用の段階的テスト戦略がまだ未実施
  - 既存文書に具体的なテスト層とケースがまとまっている
- Evidence:
  - `docs/admin-gateway-rdf-ui-test-strategy.md`
- Scope:
  - `AdminMetricsService` のサービス層テスト
  - `Admin.razor` の bUnit テスト
  - 必要に応じて最小 RDF fixture を追加
- Done shape:
  - サービス層と UI 層の最低限ケースが自動化される
  - RDF 変更時に Admin UI の回帰を検知できる

### 5. `feat: add BigQuery telemetry sink and query provider`

- Issue: `#64`
- Priority: Medium
- Type: Epic candidate
- Area: `area:storage`
- Why:
  - Parquet に加えて BigQuery への dual-write / query 切替を行う設計案がある
  - ストレージ拡張として独立した backlog にしやすい
- Evidence:
  - `docs/telemetry-storage-bigquery-design.md`
- Scope:
  - `BigQueryTelemetryEventSink`
  - `BigQueryTelemetryStorageQuery`
  - `TelemetryStorageQueryRouter`
  - 設定、テスト、段階導入手順
- Suggested split:
  - `#74 feat: add BigQuery telemetry event sink`
  - `#75 feat: add BigQuery telemetry query implementation`
  - `#76 feat: add query provider routing for BigQuery telemetry storage`
  - `#77 chore: validate dual-write and query parity for BigQuery storage`

### 6. `feat: wire .NET services to OpenTelemetry Collector`

- Issue: `#65`
- Priority: Medium
- Area: `area:api`, `area:admin`, `area:silo`, `area:ingest`
- Why:
  - Collector 方針と compose profile の考え方はあるが、.NET 側 SDK 導入は未着手
  - 運用や障害切り分けの改善余地が大きい
- Evidence:
  - `docs/observability-opentelemetry.md`
- Scope:
  - API / Admin / Silo / Publisher の OTLP 出力追加
  - 必要最小限のメトリクス / トレース / ログ設定
  - ローカル compose overlay で確認可能にする
- Done shape:
  - Collector に最低 1 系統のメトリクスとトレースが入る
  - 設定方法と確認手順が README か docs にある

## Secondary Issues

### 7. `feat: add live telemetry push to Telemetry Client`

- Issue: `#66`
- Priority: Medium
- Area: `area:admin` or `area:api`
- Why:
  - Telemetry Client spec では optional WebSocket upgrade が将来課題として残っている
  - 現状 UI は手動 `Load Telemetry` 主体で、運用体験に差がある
- Evidence:
  - `docs/telemetry-client-spec.md`
  - `src/Services/TelemetryClient/Pages/Index.razor`
- Scope:
  - SignalR もしくは polling 戦略の整理
  - 選択ポイントの live trend 更新
  - 回帰テスト追加

### 8. `chore: align MQTT topic binding defaults with actual ingest contract`

- Issue: `#67`
- Priority: Low
- Area: `area:ingest`
- Why:
  - `MqttTopicBindingOptions` に `TODO 実態に合わせて修正` が残っている
  - デフォルト topic / regex が現行契約とズレると設定なし利用時に事故りやすい
- Evidence:
  - `src/Libraries/Telemetry.Ingest/Connectors/Mqtt/MqttIngestOptions.cs`
- Scope:
  - topic binding の想定契約を文書化
  - default 値とサンプル設定を見直す
  - 対応テストを追加

### 9. `chore: remove legacy backup artifacts from TelemetryClient`

- Issue: `#68`
- Priority: Low
- Area: `area:tests`, `documentation`
- Why:
  - `Index.razor.bak` が残っており、現在の実装との差分管理やレビューのノイズになる
  - 完了条件が単純で、保守用の小さな cleanup issue として扱える
- Evidence:
  - `src/Services/TelemetryClient/Pages/Index.razor.bak`
- Scope:
  - `.bak` ファイルの扱いを整理して削除
  - 必要なら Git 履歴参照に寄せる
  - 関連ドキュメント更新の要否を確認

## Not Recommended Yet

### `feat: add field device authentication`

- Reason:
  - README 上のギャップとしては重要だが、現時点では設計素材が不足しており scope が広すぎる
  - 先に control egress / authorization / observability を固めた方が着手しやすい

### `feat: implement gateway remote update`

- Reason:
  - README の対応表上では未実装だが、現行リポジトリの主要ユースケースからは少し遠い
  - 要件や対象コンポーネントがまだ具体化されていない

## Suggested Backlog Order

1. Control egress
2. Control status/history API
3. Admin RDF-to-UI test suite
4. OpenTelemetry wiring
5. Hierarchical RBAC/ABAC epic
6. BigQuery storage epic
7. Telemetry Client live updates
8. MQTT binding cleanup
9. TelemetryClient backup artifact cleanup
