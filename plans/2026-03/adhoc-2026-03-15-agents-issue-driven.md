# Task: AGENTS.md の Issue ドリブン化と plans ローテーション導入

## Metadata
- Issue: なし
- Status: Completed

## Purpose
リポジトリのエージェント運用を GitHub Issue ドリブンへ寄せ、肥大化した `plans.md` をインデックス用途に縮小する。

## Success Criteria
1. `AGENTS.md` に GitHub Issue を作業の基本単位とする方針が明記される。
2. `AGENTS.md` に `plans.md` を索引化し、詳細を `plans/YYYY-MM/` に記録するローテーション方針が明記される。
3. 既存の肥大化した `plans.md` がアーカイブされ、ルートの `plans.md` が簡潔な運用インデックスへ置き換わる。
4. 関連ドキュメント間で新運用に矛盾がない。

## Steps
1. 現行 `AGENTS.md` と `plans.md` の運用上の問題点を確認する。
2. `AGENTS.md` に Issue ドリブン運用と plans ローテーション規約を追加する。
3. 既存 `plans.md` をアーカイブへ移し、ルートに新しいインデックスを作成する。
4. 差分を確認し、結果を記録する。

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4

## Observations
- 既存 `plans.md` は複数タスクの全文履歴が単一ファイルへ蓄積しており、次の作業の起点としては重すぎる。
- 既存 `AGENTS.md` は `plans.md` を唯一の一次情報として要求しており、Issue ベースの粒度や月次ローテーションを前提としていなかった。
- 既存履歴は削除せず、`plans/archive/2026-03.md` へ退避する形で保持できる。

## Decisions
- ルート `plans.md` は「アクティブな索引」のみに限定する。
- 詳細な実行ログは `plans/YYYY-MM/issue-<number>-<slug>.md` または `adhoc-...` に分離する。
- 今回の変更自体はまだ GitHub Issue に紐づいていないため、暫定的に ad-hoc ノートとして記録する。

## Verification Plan
- `git diff -- AGENTS.md plans.md plans/2026-03/adhoc-2026-03-15-agents-issue-driven.md plans/archive/2026-03.md`

## Verification Results
- `git diff -- AGENTS.md plans.md plans/2026-03/adhoc-2026-03-15-agents-issue-driven.md plans/archive/2026-03.md` を確認し、以下を検証した。
- `AGENTS.md` に Issue ドリブン運用と plans ローテーション規約が追加されている。
- 旧 `plans.md` は `plans/archive/2026-03.md` に退避され、ルート `plans.md` は索引用の軽量ファイルになっている。
- `plans/2026-03/adhoc-2026-03-15-agents-issue-driven.md` が今回作業の詳細記録として参照可能になっている。

## Retrospective
- ルート `plans.md` を薄く保つだけでも、次の作業開始コストは大きく下がる。
- 今後は ad-hoc ノートを増やしすぎないためにも、恒常的な作業は GitHub Issue を先に切ってから着手する運用の方が整合する。
