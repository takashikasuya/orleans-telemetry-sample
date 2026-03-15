# Task: backlog から GitHub Issue を起票

## Metadata
- Issue: なし
- Status: Completed

## Purpose
ローカル backlog に整理した候補から、優先度の高いものを GitHub Issue として実際に起票し、今後の Issue ドリブン開発へ移行する。

## Success Criteria
1. backlog 文書を元に GitHub Issue を複数件作成している。
2. 各 Issue が title、背景、受け入れ条件、検証方法を含む。
3. 作成した Issue 番号と概要がローカル planning note に記録される。

## Steps
1. GitHub remote と起票方法を確認する。
2. 起票対象の backlog 項目を選定する。
3. `gh issue create` で Issue を作成する。
4. 作成結果を planning note に記録する。

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4

## Observations
- `.github/ISSUE_TEMPLATE/` は整備済みなので、body もその前提に合わせて記述できる。
- backlog には epic 候補と単発 Issue 候補が混在しているため、今回は起票順と粒度を明示する必要がある。
- `gh issue list` は空だったため、今回の backlog 候補は重複なく起票できた。

## Decisions
- まずは優先度高の項目を中心に起票する。
- 推奨 backlog 9 件をそのまま全件起票し、epic 候補も親 Issue として先に置く。

## Verification Plan
- `gh issue list --limit 20`
- `git diff -- plans.md plans/2026-03/adhoc-2026-03-15-backlog-issue-filing.md`

## Verification Results
- `gh auth status` で `takashikasuya` アカウントの認証を確認した。
- `gh issue list --limit 50` では既存 Issue が無いことを確認した。
- 以下の Issue を作成した。
  - `#60 feat: deliver control commands from ApiGateway to connector egress`
  - `#61 feat: expose control command status and history endpoint`
  - `#62 feat: add hierarchical RBAC and ABAC for OIDC users`
  - `#63 test: add RDF-to-Admin UI tree verification suite`
  - `#64 feat: add BigQuery telemetry sink and query provider`
  - `#65 feat: wire .NET services to OpenTelemetry Collector`
  - `#66 feat: add live telemetry push to Telemetry Client`
  - `#67 chore: align MQTT topic binding defaults with actual ingest contract`
  - `#68 chore: remove legacy backup artifacts from TelemetryClient`
- `gh issue list --limit 20` で `#60` から `#68` が open として並ぶことを確認した。
- `docs/backlog/2026-03-repository-backlog.md` に Issue 番号を追記した。

## Retrospective
- backlog を先にローカルで粒度調整しておくと、Issue 起票時の body 記述が機械的に進めやすい。
- epic 候補も先に親 Issue として起票しておくと、後続の分割作業を Issue 番号ベースで追いやすい。
