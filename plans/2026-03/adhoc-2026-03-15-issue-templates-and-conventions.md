# Task: GitHub Issue template と命名規約の整備

## Metadata
- Issue: なし
- Status: In Progress

## Purpose
Issue ドリブン運用を実際に回せるように、GitHub Issue template、config、命名規約ドキュメントを追加する。

## Success Criteria
1. `.github/ISSUE_TEMPLATE/` に主要な Issue 種別のテンプレートが追加される。
2. `.github/ISSUE_TEMPLATE/config.yml` で新規 Issue 作成導線が整理される。
3. 命名規約と運用フローを記したローカル文書が追加される。
4. `AGENTS.md` または `README.md` から新運用への参照が追加される。

## Steps
1. 既存の `.github` 構成を確認する。
2. Issue template と config を追加する。
3. 命名規約と運用フロー文書を追加する。
4. 関連ドキュメントの参照を更新する。
5. 差分を確認し、結果を記録する。

## Progress
- [x] Step 1
- [ ] Step 2
- [ ] Step 3
- [ ] Step 4
- [ ] Step 5

## Observations
- `.github` 配下には現状 `copilot-instructions.md` しかなく、Issue template や config は未整備。
- `AGENTS.md` では Issue ドリブン方針を追加済みだが、実際の起票フォーマットと命名規約は未定義。

## Decisions
- バグ、機能、運用タスクの 3 種類をテンプレート化する。
- 命名規約は `type: concise outcome` 形式を基本とし、詳細はローカル文書へ記載する。

## Verification Plan
- `git diff -- .github AGENTS.md README.md docs/github-issue-workflow.md plans.md plans/2026-03/adhoc-2026-03-15-issue-templates-and-conventions.md`

## Verification Results
- 未実施

## Retrospective
- 作業完了後に記載する。
