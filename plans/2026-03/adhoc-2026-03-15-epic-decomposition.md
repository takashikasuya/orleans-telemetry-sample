# Task: Epic Issue のタスク分解

## Metadata
- Issue: なし
- Status: Completed

## Purpose
Epic として起票した backlog 項目を、実際に着手できる粒度の GitHub Issue へ分解し、Issue ドリブンで進めやすい状態にする。

## Success Criteria
1. `#62` と `#64` の子タスク Issue が作成されている。
2. 各子 Issue が単独で実装・検証可能な粒度になっている。
3. 親子対応がローカル planning note と backlog 文書に記録される。

## Steps
1. 親 Issue と設計ドキュメントを確認する。
2. 子タスクの境界を定義する。
3. GitHub Issue を起票する。
4. 作成結果を planning note と backlog に記録する。

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4

## Observations
- `#62` は schema、認可サービス、適用、UI、監査の複数レイヤーを含み、親 Issue のままでは大きすぎる。
- `#64` は sink、query、router、検証の 4 系統に分けるのが自然。
- 子 Issue は親よりも「単独で完了条件を持てるか」を基準に切ると扱いやすい。

## Decisions
- 今回は `#62` と `#64` のみを分解対象とする。
- 親 Issue 自体は epic として残し、子 Issue 一覧は親 Issue のコメントとローカル backlog の両方に記録する。

## Verification Plan
- `gh issue list --limit 30`
- `git diff -- plans.md plans/2026-03/adhoc-2026-03-15-epic-decomposition.md docs/backlog/*.md`

## Verification Results
- `#62` から以下の子 Issue を作成した。
  - `#69 feat: add authorization schema and migrations for OIDC RBAC`
  - `#70 feat: implement OIDC user sync and shared authorization service`
  - `#71 feat: enforce hierarchical authorization in API and gRPC endpoints`
  - `#72 feat: add Admin UI workflows for scoped role assignment`
  - `#73 feat: add audit logging for authorization changes`
- `#64` から以下の子 Issue を作成した。
  - `#74 feat: add BigQuery telemetry event sink`
  - `#75 feat: add BigQuery telemetry query implementation`
  - `#76 feat: add query provider routing for BigQuery telemetry storage`
  - `#77 chore: validate dual-write and query parity for BigQuery storage`
- 親 Issue へ子 Issue 一覧コメントを追加した。
  - `#62`: `issuecomment-4063106926`
  - `#64`: `issuecomment-4063106969`
- `gh issue list --limit 30` で `#69` から `#77` が open として並ぶことを確認した。
- `docs/backlog/2026-03-repository-backlog.md` に child Issue 番号を追記した。

## Retrospective
- epic を親のまま残し、子を実装単位に切ると進捗管理と優先順位付けがかなりしやすくなる。
- 親 Issue 本文を大きく書き換えるより、子一覧コメントを追加する方が履歴を崩さず扱いやすい。
