# AdminGateway: RDF入力でGrainを生成しUI検証するためのテスト設計方針

## 目的

AdminGateway (`src/AdminGateway`) に対して、RDF を入力に Orleans の Graph Grain 群を生成し、UI（MudTreeView ベース）の表示・選択・詳細表示を継続的に検証できるテスト体系を定義する。

この設計方針では、以下を同時に満たすことを重視する。

1. **RDF→Grain生成の妥当性**（データ起点）
2. **AdminGateway サービス層の整合性**（ツリー構築ロジック）
3. **UI 表示と操作の妥当性**（ノード展開・詳細表示）
4. **実運用に近い統合確認**（Docker + 実サービス）

## 現状整理（既存実装）

- RDF 解析と GraphSeedData 生成は `DataModel.Analyzer` が担っている。
- Silo 側は `GraphSeedGrain`/`GraphSeeder` が RDF から Graph ノードを作成し、インデックスへ登録する。
- AdminGateway は `AdminMetricsService.GetGraphTreeAsync` で `IGraphIndexGrain`/`IGraphNodeGrain` を参照し、ツリー用 DTO (`GraphTreeNode`) を生成する。
- UI は `Pages/Admin.razor` が MudTreeView を描画し、ノードクリック時に `GetGraphNodeAsync` で詳細を表示する。

## テスト設計の基本方針

### 1. テストを 3 層 + 1 統合検証で分離する

1. **層A: RDF→GraphSeedData（既存 + 追加）**
   - 対象: `DataModel.Analyzer.Tests`
   - 役割: RDF 由来の階層・関係が Orleans 初期化データに落ちることを保証
   - 期待値: Site/Building/Level/Area/Equipment/Point のノード/エッジが定義通り

2. **層B: AdminMetricsService（新規）**
   - 対象: `AdminGateway` の `GetGraphTreeAsync` / `BuildGraphRelations` の仕様
   - 役割: Grain スナップショットから UI ツリーに変換するルールを固定化
   - 期待値: `hasPart/isPartOf/locatedIn/isLocationOf` 優先順位、`Device→Equipment` 正規化

3. **層C: Blazor UI コンポーネント（新規）**
   - 対象: `Admin.razor`
   - 役割: Tree 表示、ノード選択、詳細パネル表示の UI 振る舞いを担保
   - 期待値: ツリー要素数、表示ラベル、クリックで詳細更新

4. **統合D: E2E（任意だが推奨）**
   - 対象: Docker compose で起動した実スタック
   - 役割: RDF を投入して UI まで end-to-end で確認
   - 期待値: シード実行後に UI 上で期待ノードが可視化される

### 2. 「壊れやすいポイント」に先にテストを当てる

優先順位は以下。

1. **関係解釈の変換**（`locatedIn` / `isLocationOf` / `hasPart` / `isPartOf`）
2. **ノードタイプ正規化**（`Device` を `Equipment` 扱い）
3. **親子一意性の解決**（複数候補時の Priority）
4. **UI クリック時の詳細ロード**（`GetGraphNodeAsync` 呼び出し）

### 3. 最小の実装変更で testability を上げる

`AdminMetricsService` 内の private static ロジックは直接テストしにくいため、以下のどちらかを採用する。

- 案A（推奨）: `GraphTreeBuilder` のような `internal` 純粋関数クラスへ抽出し単体テスト
- 案B: 現状維持のまま `GetGraphTreeAsync` をモックした Grain 応答で検証

最初は案Bでも良いが、長期運用は案Aの方が保守しやすい。

## 推奨テスト実装プラン

## Phase 1: サービス層テスト基盤を作る

- `src/AdminGateway.Tests`（新規 xUnit）を追加
- 参照:
  - `src/AdminGateway/AdminGateway.csproj`
  - `src/Grains.Abstractions/Grains.Abstractions.csproj`
- モック方針:
  - `IClusterClient` を Moq/NSubstitute で差し替え
  - `IGraphIndexGrain` の `GetByTypeAsync` 戻り値を fixture 化
  - `IGraphNodeGrain.GetAsync` をノード辞書から返す

**代表ケース**

1. `hasBuilding/hasLevel/hasArea/hasPoint` がツリーに反映される
2. `isPartOf` しか無い RDF でも逆向き解釈で親子化される
3. `locatedIn` / `isLocationOf` で Area 配下に Equipment が入る
4. Device 型ノードが Equipment 表示になる
5. 循環参照があっても無限再帰にならない

## Phase 2: Blazor UI テストを追加する

- `bUnit` 導入（`AdminGateway.Tests` に追加）
- `AdminMetricsService` をテストダブルで注入
- `Admin.razor` をレンダリングし、以下を検証

**代表ケース**

1. 初期表示で Graph Tree 見出しとノードラベルが表示
2. ノードクリックで詳細カードに `NodeId`/`NodeType` が表示
3. テナント切り替え時にツリーが更新される
4. エラー時に graceful な文言（空表示/警告）が出る

## Phase 3: E2E UI テスト（Playwright）を整備する

- `Telemetry.E2E.Tests` と並列に `AdminGateway.E2E.Tests`（または同プロジェクト内分類）
- 手順:
  1. `docker compose up --build`
  2. `/admin/graph/import` へ seed-complex.ttl を指定して投入
  3. `http://localhost:8082/` を Playwright で開く
  4. ツリー展開し、期待ノード（例: Lobby, HVAC 等）をアサート

E2E は実行コストが高いため、CI では nightly/手動トリガー、PR では層A〜Cを必須にする。

## テストデータ戦略（RDF）

### 基本方針

- `src/Telemetry.E2E.Tests/seed-complex.ttl` を「実運用相当 fixture」として再利用
- UI テスト用に **最小RDF fixture** を別途追加（例: `seed-admin-minimal.ttl`）
  - Site 1 / Building 1 / Level 1 / Area 1 / Equipment 1 / Point 1
  - `isPartOf`, `locatedIn` の片方向だけ持つバリエーションも作る

### 命名規約例

- `seed-admin-minimal.ttl`（正常最小）
- `seed-admin-relations-reverse-only.ttl`（逆関係のみ）
- `seed-admin-cyclic-invalid.ttl`（防御確認）

## CI への載せ方（推奨）

- PR 必須:
  - `dotnet test src/DataModel.Analyzer.Tests`
  - `dotnet test src/AdminGateway.Tests`
- 任意（手動/夜間）:
  - Docker 起動を伴う E2E UI

これにより、通常 PR のフィードバック速度を維持しつつ、RDF→UI の経路を段階的に保証できる。

## 受け入れ基準（Definition of Ready/Done）

### Ready

- 対象 RDF fixture が確定している
- 期待するツリー構造（親子）をテキストで定義済み
- Device→Equipment 正規化の仕様が明文化されている

### Done

- 層A/B/C の自動テストが全て green
- 失敗時に原因レイヤーが即判別できる（データ/サービス/UI）
- E2E（統合D）は少なくとも 1 シナリオで通過記録がある

## 最初の 1 スプリントで実施する最小セット

1. `AdminGateway.Tests` 新設
2. `GetGraphTreeAsync` のサービス層テストを 5 ケース追加
3. `Admin.razor` の bUnit テストを 2 ケース追加（表示 + クリック）
4. README または docs にテスト実行コマンドを追記

この最小セットで、RDF 由来の Grain データが UI ツリーに反映される経路を、現実的なコストで継続検証できるようになる。
