# OIDC認証アカウント向け階層RBAC/ABAC設計方針（Draft）

## 1. 目的

OIDCで認証されたユーザーに対して、以下を実現する認可基盤を追加する。

- テナント → 敷地内（Site）→ ビル（Building）→ フロア（Floor）→ 部屋（Room）→ デバイス（Device）の階層単位でアクセス権を設定。
- 上位ノードで与えた権限が下位ノードへ継承される。
- 「全リソースへのアクセス/制御」を持つ特権ユーザー（Super Admin）を定義。
- 管理者画面（Admin UI）からユーザー・ロール・権限割当を編集可能にする。
- 権限データを PostgreSQL に永続化し、API / gRPC / 管理UI / 制御系で統一的に判定する。

> 本ドキュメントは設計方針であり、実装は対象外。

---

## 2. 要件整理

### 2.1 機能要件

1. OIDC認証済みユーザーを内部ユーザーに紐付ける（初回ログイン時の自動登録/同期を含む）。
2. 階層リソース単位でロール割当可能。
3. 権限継承: 例) Building管理者は配下Floor/Room/Deviceにも同等権限。
4. 明示 deny よりもまずは allow-only モデルで開始（必要時に deny 拡張）。
5. 特権ユーザーは階層判定をバイパスして全許可。
6. Admin UIから以下を操作可能:
   - ユーザー一覧/検索
   - ユーザーのOIDC情報確認（sub, issuer, email等）
   - ロール付与/剥奪
   - スコープ（tenant/site/.../device）選択と権限割当
   - 監査ログ参照

### 2.2 非機能要件

- 認可判定は低遅延（API呼び出しごとに実行されるため）。
- 監査可能性（誰が、いつ、何を変更したか）。
- 将来のマルチテナント分離を考慮。
- 権限変更反映の遅延を最小化（キャッシュ利用時は失効戦略を持つ）。

---

## 3. 認可モデル方針

## 3.1 基本モデル: 「Role + Scope + Action」

- **Role**: 例 `Viewer`, `Operator`, `TenantAdmin`, `SuperAdmin`
- **Scope**: リソース階層ノード（tenant/site/building/floor/room/device）
- **Action**: `read`, `control`, `manage_acl`, `manage_users`, `export` など

実体としては「ユーザーに対するロール割当（assignment）」を保持し、
ロールは複数アクションを持つ。

## 3.2 階層継承ルール

- 各リソースは `parent_id` を持つツリー構造。
- 認可判定時、対象リソースから親方向へ辿り、
  ユーザーの assignment が見つかれば許可候補とする。
- 最初は **allow のみ** を採用:
  - 一致する allow が1つでもあれば許可
  - なければ拒否

## 3.3 特権ユーザー

- `SuperAdmin` ロールをシステムスコープ（`scope = global`）で付与。
- 判定時に最優先で許可。
- 付与/剥奪は二重承認または厳格監査対象（運用ルール）。

## 3.4 将来拡張

- denyルール、時間帯条件、属性条件（ABAC: 例 `tenant_id` 一致）を追加可能な設計にする。
- 現段階はデータモデルだけ reserve カラムを用意し、判定ロジックはシンプルに保つ。

---

## 4. PostgreSQL データモデル案

## 4.1 主テーブル

1. `auth_users`
   - `id (uuid, pk)`
   - `oidc_issuer (text)`
   - `oidc_subject (text)`
   - `email (text)`
   - `display_name (text)`
   - `is_active (bool)`
   - `created_at`, `updated_at`
   - unique: `(oidc_issuer, oidc_subject)`

2. `auth_roles`
   - `id (uuid, pk)`
   - `name (text, unique)` 例: `viewer`, `operator`, `tenant_admin`, `super_admin`
   - `description`
   - `is_system (bool)`

3. `auth_permissions`
   - `id (uuid, pk)`
   - `action (text, unique)` 例: `telemetry.read`, `device.control`, `acl.manage`
   - `description`

4. `auth_role_permissions`
   - `role_id (fk)`
   - `permission_id (fk)`
   - PK: `(role_id, permission_id)`

5. `auth_resources`
   - `id (uuid, pk)`
   - `tenant_id (text)`
   - `resource_type (text)` enum相当: `tenant|site|building|floor|room|device`
   - `resource_key (text)` 既存RDF/GraphノードID等を格納
   - `parent_id (uuid, nullable fk auth_resources.id)`
   - `path (ltree or text)` 階層探索高速化用
   - unique: `(tenant_id, resource_type, resource_key)`

6. `auth_user_role_assignments`
   - `id (uuid, pk)`
   - `user_id (fk)`
   - `role_id (fk)`
   - `resource_id (fk, nullable)` ※ global ロール時は null
   - `scope_kind (text)` `global|resource`
   - `expires_at (timestamp, nullable)`
   - `created_by`, `created_at`

7. `auth_audit_logs`
   - `id (bigserial, pk)`
   - `actor_user_id`
   - `event_type` (`ASSIGN_ROLE`, `REVOKE_ROLE`, `CREATE_USER`, ...)
   - `target_user_id`
   - `payload_json`
   - `created_at`

## 4.2 インデックスと高速化

- `auth_user_role_assignments(user_id)`
- `auth_resources(tenant_id, resource_type, resource_key)`
- `auth_resources(parent_id)`
- `auth_resources(path)` (ltree GiST/GiN)

`path` を使うと「対象ノードの祖先集合」検索が高速。
初期は再帰CTEでも良いが、将来的な高負荷を考えると ltree 推奨。

---

## 5. 認可判定フロー

1. OIDC認証でトークン検証（既存JWT/OIDC設定を利用）。
2. `issuer + sub` で `auth_users` を引当。
3. 要求対象リソースを `auth_resources` に解決。
4. ユーザーの assignment と role->permission を評価。
5. 以下の順序で判定:
   - `super_admin@global` があれば即許可
   - 対象リソース自身または祖先リソースに対応する assignment があり、必要 action を満たせば許可
   - それ以外は拒否
6. 判定結果を監査ログへ（少なくとも拒否/制御系は記録）。

### 5.1 判定APIイメージ（内部）

- `IAuthorizationService.Can(user, action, resourceRef)`
- `resourceRef` は `tenantId + resourceType + resourceKey`
- API Controller, gRPC Service, Admin操作エンドポイントで共通利用

---

## 6. 管理者画面（Admin UI）設計方針

## 6.1 画面機能

1. **ユーザー管理**
   - OIDC連携済みユーザー一覧（検索/有効無効）
2. **権限割当管理**
   - ユーザー選択
   - ロール選択
   - スコープ選択（tenant/site/building/floor/room/device、またはglobal）
   - 有効期限設定（任意）
3. **リソースツリー表示**
   - 既存の空間ツリー（Site→Building→...）を再利用し選択可能に
4. **監査ログ**
   - いつ誰がどのユーザーに何を変更したか

## 6.2 権限制御

- Admin UIの操作自体も認可対象:
  - `manage_users` / `manage_acl` が必要
- SuperAdmin 以外は「自テナント内のみ編集可能」などの制約を推奨。

## 6.3 UX方針

- 「この権限によりアクセス可能な範囲」をプレビュー表示（誤設定防止）。
- 危険操作（super_admin付与）は確認ダイアログ + 監査必須。

---

## 7. 既存システムへの適用ポイント

- `ApiGateway`:
  - RESTエンドポイントで action を明示し認可判定。
- `gRPC services`:
  - interceptor か service 内ガードで同一ポリシーを適用。
- `AdminGateway`:
  - 画面表示/操作API双方で認可必須。
- `SiloHost / Grain`:
  - 可能ならアプリ層（Gateway）で一元判定。
  - ただし将来、直接呼び出し経路が増えるなら grain側防御も検討。

---

## 8. ロール初期定義（例）

- `viewer`
  - `telemetry.read`, `registry.read`
- `operator`
  - viewer + `device.control`
- `tenant_admin`
  - operator + `acl.manage`, `users.manage`（同一tenantのみ）
- `super_admin`
  - `*`（全action）

※ 本番相当では `*` は内部的に明示 action 群へ展開し、過剰権限を可視化するのが望ましい。

---

## 9. 導入ステップ（実装時の段階案）

1. PostgreSQL スキーマ導入（users/roles/permissions/resources/assignments/audit）。
2. OIDCユーザー同期（初回ログイン時 upsert）。
3. 認可サービス実装（global + 階層継承 allow 判定）。
4. ApiGateway/AdminGateway のエンドポイントへ適用。
5. Admin UIでユーザー/権限編集機能を追加。
6. 監査ログ表示と運用ルール整備（super_admin 管理手順）。

---

## 10. リスクと対策

- **権限漏れ（過剰許可）**
  - 対策: デフォルト拒否、Action定義の棚卸し、統合テスト。
- **権限反映遅延（キャッシュ）**
  - 対策: 短TTL + 明示無効化イベント。
- **リソース階層不整合**
  - 対策: リソース登録時の親存在チェック、夜間整合性検査。
- **運用事故（super_admin誤付与）**
  - 対策: 監査、期限付き付与、二重承認。

---

## 11. 受け入れ基準（設計レビュー観点）

- テナント/敷地/ビル/フロア/部屋/デバイスの各階層で権限割当・継承ルールが明確。
- 特権ユーザーの扱い（付与、判定優先順位、監査）が明確。
- Admin UIで必要な編集操作と、その操作自体の認可方針が定義済み。
- PostgreSQLスキーマと主要インデックス方針が定義済み。
- API/gRPC/Adminの適用ポイントが整理されている。

