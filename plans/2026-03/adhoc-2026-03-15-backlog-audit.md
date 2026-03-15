# Task: リポジトリ backlog 監査

## Metadata
- Issue: なし
- Status: Completed

## Purpose
リポジトリ内のドキュメント、計画メモ、コード上の未解決事項を横断確認し、GitHub Issue として切り出せる backlog 候補を整理する。

## Success Criteria
1. 既存の `plans/`、`docs/`、主要コードから backlog 候補を抽出できている。
2. 候補ごとに Issue title、背景、期待成果、優先度の骨子が整理されている。
3. ローカル backlog 文書が生成され、今後の Issue 起票元として参照できる。

## Steps
1. 既存の planning docs と workflow docs を確認する。
2. `TODO`、`FIXME`、未実装メモ、既知ギャップをコードと文書から抽出する。
3. 重複や粒度を整理して Issue 候補へ正規化する。
4. backlog 文書を作成し、結果を記録する。

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4

## Observations
- `plans/archive/2026-03.md` に過去の調査・既知ギャップが集約されており、backlog 候補の一次ソースとして有効。
- `docs/` には未実装や設計案を示すファイルがあり、Issue 候補として再構成できる可能性が高い。
- backlog 候補は「遠隔制御の配送欠落」「認可設計ドラフト」「ストレージ拡張」「UI/テスト強化」の 4 系統に集約できた。
- コード上の小さな候補として `MqttTopicBindingOptions` の TODO と `TelemetryClient/Pages/Index.razor.bak` の残置も確認できた。

## Decisions
- backlog 候補は「すぐ起票できる粒度」まで落とし、広すぎるテーマは epic 候補として分ける。
- backlog 文書は `docs/backlog/` 配下へ追加し、GitHub Issue 起票前の一次整理場所として使う。

## Verification Plan
- `git diff -- plans.md plans/2026-03/adhoc-2026-03-15-backlog-audit.md docs/backlog/*.md`

## Verification Results
- `docs/backlog/2026-03-repository-backlog.md` を新規作成し、Issue 候補 9 件と後回し候補 2 件を整理した。
- 主要候補は以下の根拠から抽出した。
  - `docs/api-gateway-remote-control.md`
  - `docs/oidc-hierarchical-authorization-design.md`
  - `docs/telemetry-storage-bigquery-design.md`
  - `docs/admin-gateway-rdf-ui-test-strategy.md`
  - `docs/observability-opentelemetry.md`
  - `src/Libraries/Telemetry.Ingest/Connectors/Mqtt/MqttIngestOptions.cs`
  - `src/Services/TelemetryClient/Pages/Index.razor.bak`
- `git diff -- plans.md plans/2026-03/adhoc-2026-03-15-backlog-audit.md docs/backlog/*.md` で差分確認した。

## Retrospective
- 設計ドラフトがすでに存在する領域は、そのまま Epic/Issue に落としやすい。
- backlog を README のギャップ記述だけで作るより、実装メモとコード内 TODO を併読した方が優先順位を付けやすい。
