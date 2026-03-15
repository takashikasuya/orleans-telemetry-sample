# GitHub Issue Workflow

このリポジトリでは、非自明な変更は原則として GitHub Issue を起点に進める。

## Purpose

- スコープを Issue 単位で固定する。
- 受け入れ条件と検証方法を着手前に明文化する。
- 実装ログはローカルの `plans/` と結び付け、履歴の参照性を保つ。

原則として、非自明な変更は Issue を作ってから着手する。

## Issue Types

- `bug`: 既存挙動の不具合修正
- `feat`: 新機能やユーザー価値の追加
- `chore`: ドキュメント、依存更新、構成整理、運用整備

## Title Convention

Issue title は次の形式を基本とする。

```text
<type>: <concise outcome>
```

例:

- `bug: fix Admin UI ingest volume under filtered logs`
- `feat: add 24h trend range to Admin UI`
- `chore: rotate plans.md and add issue templates`

ルール:

1. 先頭の type は `bug`、`feat`、`chore` のいずれかを使う。
2. outcome は「何をするか」ではなく「何ができるようになるか / 何が直るか」を短く書く。
3. 1 つの Issue に unrelated な目的を混ぜない。

## Recommended Labels

- `bug`
- `enhancement`
- `chore`
- `documentation`
- `area:api`
- `area:admin`
- `area:silo`
- `area:ingest`
- `area:storage`
- `area:tests`

ラベルは必要最小限にし、種別 1 個 + 領域 1 個を基本とする。

## Required Issue Content

Issue には最低限以下を含める。

1. 背景または問題
2. スコープ
3. 受け入れ条件
4. 検証方法
5. 関連ドキュメントやログ

`.github/ISSUE_TEMPLATE/` の template はこの前提で設計している。

## Mapping to Local Planning Notes

Issue を起票したら、着手時に以下を作る。

- `plans/YYYY-MM/issue-<number>-<slug>.md`

例:

- Issue `#42 chore: rotate plans.md and add issue templates`
- Planning note `plans/2026-03/issue-42-rotate-plans-and-add-issue-templates.md`

`slug` のルール:

1. 小文字 kebab-case を使う。
2. 冠詞や助詞は省略してよい。
3. 40 文字程度を目安に短く保つ。

## Execution Flow

1. Issue を起票する。
2. 受け入れ条件と検証方法を固める。
3. `plans.md` に active entry を追加する。
4. `plans/YYYY-MM/issue-<number>-<slug>.md` を作り、実装と検証を記録する。
5. 実装後に build/test/manual verification を記録する。
6. `plans.md` の status を更新する。
7. PR や commit message に Issue 番号を含める。

## Ad-hoc Work

Issue が無いまま着手する場合は次を使う。

- `plans/YYYY-MM/adhoc-YYYY-MM-DD-<slug>.md`

ただし次に当てはまる場合は、早い段階で Issue 化する。

1. 複数ターンにまたがる。
2. commit や PR に残す可能性が高い。
3. 仕様・挙動・テスト・設定・運用ルールに影響する。
4. 他の人があとから参照する可能性がある。

ad-hoc のまま残してよいのは、短時間の調査、使い捨てのローカル検証、軽微な文面調整などに限る。

## plans.md Rotation

`plans.md` は実行ログ本文を書く場所ではなく、アクティブな索引として保つ。

含めてよい内容:

- active task link
- short status
- archive link
- 簡潔な運用ルール

次の内容が増え始めたら `plans/archive/YYYY-MM.md` へ退避する。

1. 完了済みタスクの詳細経緯
2. 長い観察メモや retrospective
3. 複数タスク分の実装ログ

## Done Criteria

Issue は次を満たしたら完了として扱う。

- 受け入れ条件が満たされている
- 実施した検証が記録されている
- 未実施の手動確認があれば明記されている
- 関連ドキュメント更新が必要なら反映されている
