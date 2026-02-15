# Publisher プロジェクト README

## 目的

`src/Publisher` は、RabbitMQ の `telemetry` キューへテレメトリーメッセージを送信する .NET コンソールアプリです。  
現状は以下の 2 モードを持っています。

1. **ランダム生成モード**: 温度・湿度を擬似生成して送信
2. **RDF 駆動モード**: `RDF_SEED_PATH` からデバイス/ポイント定義を読み取り、定義に沿って送信

加えて、`telemetry-control` キュー経由の制御コマンドで writable ポイントの値を上書きできます。

---

## 現状の使い方（最小）

```bash
dotnet run --project src/Publisher
```

### RDF シードを使って起動するコマンド

```bash
RDF_SEED_PATH=./data/sample.ttl \
RABBITMQ_HOST=localhost \
RABBITMQ_PORT=5672 \
RABBITMQ_USER=guest \
RABBITMQ_PASS=guest \
TENANT_ID=t1 \
dotnet run --project src/Publisher
```

Docker Compose 利用時（`publisher` サービスのみ起動）:

```bash
docker compose up --build publisher
```

### RDF 駆動時のテレメトリサンプル

Publisher は RabbitMQ `telemetry` キューに `TelemetryMsg` を JSON で publish します。例:

```json
{
  "DeviceId": "ahu-01",
  "Sequence": 42,
  "Properties": {
    "supply_air_temp": 22.384,
    "fan_status": true,
    "_pointMetadata": {
      "supply_air_temp": {
        "PointType": "TemperatureSensor",
        "Unit": "degC",
        "MinValue": 16.0,
        "MaxValue": 30.0,
        "Writable": true
      },
      "fan_status": {
        "PointType": "BinarySwitch",
        "Unit": null,
        "MinValue": 0.0,
        "MaxValue": 1.0,
        "Writable": false
      }
    }
  },
  "TenantId": "t1",
  "Timestamp": "2026-02-14T21:30:15.1234567+00:00",
  "BuildingName": "Building A",
  "SpaceId": "Building A/Level 1/Mechanical Room"
}
```

Publish 成功時の標準出力例:

```text
Published ahu-01 seq 42
```

主な環境変数:

- `RABBITMQ_HOST` (`localhost`)
- `RABBITMQ_PORT` (`5672`)
- `RABBITMQ_USER` (`user`)
- `RABBITMQ_PASS` (`password`)
- `RABBITMQ_RECONNECT_INITIAL_MS` (`1000`)
- `RABBITMQ_RECONNECT_MAX_MS` (`30000`)
- `TENANT_ID` (`t1`)
- `BUILDING_NAME` (`bldg-1`)
- `SPACE_ID` (`floor-1/room-1`)
- `RDF_SEED_PATH` (未指定時はランダム生成)
- `CONTROL_QUEUE` (`telemetry-control`)

主な CLI オプション:

- `--device-count`
- `--interval-ms`
- `--burst`
- `--burst-interval-ms`
- `--burst-duration-sec`
- `--burst-pause-sec`
- `--reconnect-initial-ms`
- `--reconnect-max-ms`
- `--profile <name>` (`profiles/<name>.json` を探索)
- `--profile-file <path>` (任意パスの profile JSON を直接指定)

プロファイル関連環境変数:

- `PUBLISH_PROFILE` (`--profile` 未指定時に使用)

選択優先順位は **`--profile-file` > `--profile` > `PUBLISH_PROFILE`** です。
どれも未指定の場合は、従来どおり `RDF_SEED_PATH` 有無で RDF/ランダム生成を切り替えます。

---

## このプロジェクトを「汎用テレメトリーエミュレータ」にする設計方針

要望: **プロファイルを指定すると、特定のスキーマ・感覚（生成特性）でテレメトリーを発行できること**。  
例: BACnet テレメトリーを JSON に正規化して送信。

### 設計ゴール

1. **プロファイル差し替えだけで挙動を変えられる**（コード再ビルド不要）
2. **出力契約は統一**（最終的には `TelemetryMsg` 形式）
3. **スキーマ差分と生成ロジック差分を分離**
4. **再現可能性**（seed、時刻スケール、ノイズ量を固定可能）
5. **運用容易性**（CLI/ENV で選択、失敗時は安全にフォールバック）

### 推奨アーキテクチャ

#### 1. Profile Layer（プロファイル定義）

`profiles/<name>.json` などに、以下を宣言的に定義します。

- 入力スキーマ種別（例: `bacnet-normalized-v1`）
- デバイステンプレート（台数、命名規則、タグ）
- ポイント定義（型、単位、範囲、更新周期）
- 生成モデル（ランダム、サイン波、ステップ、ドリフト、故障注入）
- 出力マッピング（内部モデル -> `TelemetryMsg.Properties`）

> ポイント: 「何を生成するか」は Profile に寄せ、「どう送るか」は Publisher 本体に寄せる。

#### 2. Schema Adapter Layer（スキーマ吸収）

スキーマごとの差異を吸収する `ISchemaAdapter` を用意。

- `BacnetNormalizedAdapter`
- `RdfAdapter`
- `RandomDefaultAdapter`

責務:

- Profile + コンテキストから内部中間モデル（例: `TelemetryFrame`）を生成
- 型正規化（bool/number/string）
- 単位・メタデータ付与
- バージョン互換チェック

#### 3. Behavior Engine（感覚/シナリオ生成）

時系列の「らしさ」を作る層。

- 基本波形（定常、サイクル、ノイズ）
- イベント注入（アラーム、センサー固着、欠損）
- 日内パターン（営業時間帯で変動）
- バースト送信（既存機能を拡張して profile 化）

#### 4. Output Contract（最終送信）

最終的に `TelemetryMsg` へ落とし込み、既存 `telemetry` キューへ publish。

- 必須: `TenantId`, `DeviceId`, `Sequence`, `Timestamp`, `Properties`
- 任意: `BuildingName`, `SpaceId`

これにより Silo/Api 側の既存 ingest 契約を維持できます。

### プロファイル選択インターフェース（提案）

- CLI: `--profile bacnet-office-v1`
- ENV: `PUBLISH_PROFILE=bacnet-office-v1`
- ファイル: `--profile-file ./profiles/bacnet-office-v1.json`

優先順位は `CLI > ENV > default`。

### BACnet JSON 正規化プロファイル例（イメージ）

```json
{
  "name": "bacnet-office-v1",
  "schema": "bacnet-normalized-v1",
  "tenantId": "t1",
  "site": {
    "buildingName": "bldg-a",
    "spaceId": "floor-2"
  },
  "devices": [
    {
      "deviceId": "ahu-01",
      "points": [
        { "id": "supply_air_temp", "type": "number", "unit": "degC", "min": 16, "max": 30, "generator": "sin" },
        { "id": "fan_status", "type": "bool", "generator": "step" }
      ]
    }
  ],
  "timing": {
    "intervalMs": 1000,
    "burst": { "enabled": false }
  }
}
```

### 実装ステップ（段階導入）

1. **Step 1: Profile Reader 導入**
   - JSON profile 読み込み
   - 現在のランダム生成を `default profile` 化
2. **Step 2: Adapter 抽象化**
   - `ISchemaAdapter` + `RandomDefaultAdapter`
3. **Step 3: BACnet Normalized Adapter 追加**
   - 数値・状態点の代表パターン実装
4. **Step 4: Behavior Engine 拡張**
   - ノイズ/ドリフト/故障注入
5. **Step 5: 検証性強化**
   - profile ごとの unit test + golden JSON

### 非機能方針

- **後方互換優先**: profile 指定なし時は現行動作を維持
- **可観測性**: 起動時に profile 名/バージョン/デバイス数を必ずログ出力
- **安全性**: 不正 profile は起動失敗 or 明示フォールバック（選択可能）
- **テスト容易性**: 乱数 seed 固定で deterministic な出力を可能にする

---

## 制御コマンド（既存機能）

`CONTROL_QUEUE` へ以下を送ることで writable ポイントを上書きできます。

```json
{ "deviceId": "ahu-01", "pointId": "supply_air_temp", "value": 22.3 }
```

解除:

```json
{ "deviceId": "ahu-01", "pointId": "supply_air_temp", "clear": true }
```

---

## まとめ

Publisher は現時点でも「RDF 定義に沿った送信器」として機能しています。  
今後は **Profile + Adapter + Behavior Engine** の 3 層に分けることで、BACnet 正規化 JSON を含む複数スキーマを同一実行基盤で扱える、汎用エミュレータへ拡張できます。
