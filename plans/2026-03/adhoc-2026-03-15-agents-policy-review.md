# Task: AGENTS.md 運用方針の再整理

## Metadata
- Issue: なし
- Status: Completed

## Purpose
`AGENTS.md` を見直し、GitHub Issue 起点の開発フローを既定動作としてより明確にしつつ、`plans.md` を肥大化させないローテーション条件を明文化する。

## Success Criteria
1. `AGENTS.md` に、非自明タスクは原則 Issue を先に用意する運用条件と例外条件が明記される。
2. `AGENTS.md` に、`plans.md` を索引として維持するためのローテーション契機とアーカイブ先がより明確に記載される。
3. `docs/github-issue-workflow.md` または `plans.md` の記述が更新され、`AGENTS.md` と矛盾しない。
4. 今回の見直し内容と確認結果がこの task note に記録される。

## Steps
1. 現行 `AGENTS.md` の曖昧な運用箇所を特定する。
2. Issue 起票前提と ad-hoc 例外の扱いを整理して `AGENTS.md` を修正する。
3. `plans.md` ローテーションの契機と archive 運用を補強し、必要な関連文書を揃える。
4. 差分確認結果を記録する。

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4

## Observations
- 現行 `AGENTS.md` でも Issue ドリブン方針は入っているが、「いつ Issue を必須とみなすか」「ad-hoc をいつ Issue 化するか」がまだ弱い。
- `plans.md` のローテーション方針はあるが、「いつ rotate するか」の判断基準が軽く、再び肥大化する余地がある。
- `docs/github-issue-workflow.md` に同じ閾値が無いと、`AGENTS.md` だけ厳しくても実務運用が追随しにくい。

## Decisions
- 今回の変更は process/documentation 変更として扱い、コードや設定には触れない。
- 既存の Issue template 整備タスクとは分けて、`AGENTS.md` の規約自体を先に安定化させる。
- ad-hoc を全面禁止にはせず、短時間の調査や局所的な文面調整だけを例外として残す。

## Verification Plan
- `git diff -- AGENTS.md plans.md docs/github-issue-workflow.md plans/2026-03/adhoc-2026-03-15-agents-policy-review.md`

## Verification Results
- `git diff -- AGENTS.md plans.md docs/github-issue-workflow.md plans/2026-03/adhoc-2026-03-15-agents-policy-review.md` を確認した。
- `AGENTS.md` に、Issue-first と ad-hoc 許容範囲の目安、ad-hoc から Issue-backed へ昇格させる条件を追加した。
- `AGENTS.md` に、`plans.md` に残してよい内容と archive へ退避すべき内容を明文化した。
- `docs/github-issue-workflow.md` に、ad-hoc を Issue 化すべき条件と `plans.md` rotation の判断基準を追加し、`AGENTS.md` と整合させた。
- `plans.md` に今回の見直し task note を active entry として追加した。

## Retrospective
- 「Issue を基本とする」だけでは運用が曖昧に戻りやすく、ad-hoc の例外条件まで書いておく方が再現性が高い。
- `plans.md` は索引に徹する、と文書上で繰り返し定義しておくことで再肥大化を抑えやすい。
