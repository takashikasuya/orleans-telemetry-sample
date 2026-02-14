# MQTTコネクタ設計（Telemetry.Ingest向け）

## 1. 目的

MQTTブローカーからのテレメトリを `Telemetry.Ingest` に取り込み、既存の Router/Sink パイプラインへ統合する。
特に以下を満たす。

- 受け入れ topic を外部設定で変更可能
- topic/payload から tenant/device/point を解決可能
- 高負荷時でも backpressure が機能し、OOM や無制限バッファを避ける

## 2. スコープ

- 新規 `MqttIngestConnector` の設計（実装前提の仕様）
- 設定スキーマ（外部化パラメータ）
- テスト設計（unit/integration/load）
- backpressure 検証計画

## 3. アーキテクチャ方針

### 3.1 配置

既存コネクタ構造に合わせて配置する。

- `src/Telemetry.Ingest/Connectors/Mqtt/MqttIngestConnector.cs`
- `src/Telemetry.Ingest/Connectors/Mqtt/MqttIngestOptions.cs`
- `src/Telemetry.Ingest/Connectors/Mqtt/MqttServiceCollectionExtensions.cs`
- テスト: `src/Telemetry.Ingest.Tests/Connectors/Mqtt/MqttIngestConnectorTests.cs`

### 3.2 依存関係

MQTT クライアントは .NET 標準的な選択として `MQTTnet` を想定（実装時に NuGet 追加）。

### 3.3 データフロー

1. MQTT subscribe（複数 topic filter）
2. `MqttApplicationMessageReceived` で受信
3. topic/payload を `TelemetryPointMsg` に正規化
4. `ChannelWriter<TelemetryPointMsg>.WriteAsync` へ投入
5. coordinator 側の batch 処理へ流す

## 4. 受け入れ topic 設計（外部化）

### 4.1 TopicBindings

複数 topic を受け入れ可能にするため、`TopicBindings` を配列で持つ。

```json
{
  "TelemetryIngest": {
    "Enabled": ["Mqtt"],
    "Mqtt": {
      "Host": "mqtt",
      "Port": 1883,
      "ClientId": "silo-ingest-1",
      "TopicBindings": [
        {
          "Filter": "tenants/+/devices/+/points/+",
          "Qos": 1,
          "TenantSource": "Topic",
          "TenantTopicIndex": 1,
          "DeviceSource": "Topic",
          "DeviceTopicIndex": 3,
          "PointSource": "Topic",
          "PointTopicIndex": 5
        },
        {
          "Filter": "telemetry/+/+/+",
          "Qos": 0,
          "TenantSource": "Payload",
          "TenantJsonPath": "$.tenantId",
          "DeviceSource": "Payload",
          "DeviceJsonPath": "$.deviceId",
          "PointSource": "Payload",
          "PointJsonPath": "$.pointId"
        }
      ]
    }
  }
}
```

### 4.2 Source解決ルール

各キー（tenant/device/point）は `Topic` または `Payload` から解決。

- `Topic`: topic split（`/` 区切り）した index を参照
- `Payload`: JSONPath で抽出

実装ルール（推奨）:

1. 抽出失敗時は message drop（warn log）
2. 空文字・null は invalid 扱い
3. `ValueSourceJsonPath` 指定時は payload から value を抽出、未指定なら payload 全体を value として保持

## 5. 必要パラメータ（外部化）

## 5.1 接続設定

- `Host` (required)
- `Port` (default 1883)
- `UseTls` (default false)
- `Username` / `Password`
- `ClientId` (required)
- `KeepAliveSeconds` (default 30)
- `CleanSession` (default true)

## 5.2 再接続・信頼性

- `ReconnectDelayMs` (default 2000)
- `MaxReconnectDelayMs` (default 30000)
- `MaxReconnectAttempts` (default -1: 無制限)
- `SessionExpirySeconds`（MQTT5 利用時）

## 5.3 購読設定

- `TopicBindings[]`
  - `Filter`
  - `Qos` (0/1/2)
  - `TenantSource`, `TenantTopicIndex`, `TenantJsonPath`
  - `DeviceSource`, `DeviceTopicIndex`, `DeviceJsonPath`
  - `PointSource`, `PointTopicIndex`, `PointJsonPath`
  - `TimestampJsonPath`（任意）
  - `SequenceJsonPath`（任意）
  - `ValueSourceJsonPath`（任意）

## 5.4 backpressure関連

- `MaxInFlightMessages`（クライアント側同時処理上限）
- `WriteTimeoutMs`（`ChannelWriter.WriteAsync` の待機上限）
- `DropPolicy`
  - `Block`（既定）: channel に空きが出るまで待つ
  - `DropNewest`
  - `DropOldest`（実装で ring buffer 併用時）
  - `FailFast`（即エラー）
- `MaxPendingAck`（QoS1/2 ack 遅延許容）

## 6. backpressure設計

## 6.1 基本戦略

既存 ingest channel (`TelemetryIngest:ChannelCapacity`) を唯一のバッファ基準にし、MQTT コネクタ内部に無制限キューを持たない。

## 6.2 期待動作

- channel が空きあり: 即時 `WriteAsync` 成功
- channel 満杯:
  - `Block`: `WriteAsync` が待機し、受信処理が自然に遅延（ブローカー側配信ペース抑制）
  - `Drop*`: drop counter を増やし metrics/log に記録

## 6.3 観測指標

最低限以下をメトリクス化。

- `mqtt_messages_received_total`
- `mqtt_messages_parsed_total`
- `mqtt_messages_dropped_total{reason=*}`
- `mqtt_write_wait_ms_histogram`
- `mqtt_backpressure_events_total`
- `mqtt_reconnect_total`

## 6.4 バックプレッシャーの意図（なぜ必要か）

このコネクタでのバックプレッシャーの意図は、**「取り込み速度 > 消費速度」の状態で、壊れずに遅くなる**ことです。

- MQTT ブローカーは高スループットで配信できるため、connector が受信し続けるとメモリが増え続けるリスクがある。
- 一方、下流（coordinator / router / sink）は瞬間的に遅くなる可能性がある。
- そのため ingest の境界である `Channel<TelemetryPointMsg>` を水位調整ポイントにし、満杯時は「待つ or 捨てる」を明示的に選ぶ。

設計上の優先順位:

1. まずプロセスの安定性（OOM 回避、ハング回避）
2. 次に可観測性（何件待った/捨てたかを数値で追える）
3. 最後に業務要件に応じた損失許容（Block か Drop 系か）

## 6.5 実装イメージ（どこで効かせるか）

### 6.5.1 受信ハンドラ内での書き込み制御

MQTT 受信イベントごとに `TelemetryPointMsg` を生成し、`WriteAsync` 前後で待機時間を計測する。

```csharp
var started = Stopwatch.GetTimestamp();
try
{
    await writer.WriteAsync(message, ct); // channel が満杯ならここで待機
    metrics.ObserveWriteWait(started);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // shutdown
}
```

### 6.5.2 ポリシー別の挙動

- `Block`:
  - `WriteAsync` をそのまま await し、空きが出るまで待つ。
  - 待機時間をメトリクス化し、遅延が常態化したら `ChannelCapacity` や下流性能を見直す。
- `DropNewest`:
  - `WaitToWriteAsync` が timeout したら現在メッセージを捨てる。
  - `dropped_total{reason="backpressure_timeout"}` を増やす。
- `FailFast`:
  - 一定時間書けなければ例外化し、connector 健全性を明確に落とす（運用側に即通知）。

### 6.5.3 QoS との関係（実装時の注意）

- QoS0 は配送保証が弱いため、Drop 系ポリシーと組み合わせると欠損しやすい。
- QoS1/2 は再送や ack 管理が入るため、`Block` で長時間詰まると in-flight が増えうる。
- したがって `MaxInFlightMessages` / `MaxPendingAck` とセットで上限を定義し、無制限化しない。

### 6.5.4 終了時の扱い

- stop 時は新規受信を止める → in-flight 処理を drain（上限時間付き）→ client disconnect の順で停止する。
- drain timeout 超過時は未処理件数をログ・メトリクスへ出し、次回改善の材料にする。

## 7. テスト設計

## 7.1 Unit tests（純粋ロジック）

対象: topic/payload 変換ロジック

1. **Topic index 抽出成功**
   - filter と topic が一致、index 指定で tenant/device/point を抽出
2. **Payload JSONPath 抽出成功**
   - `$.tenantId` などで抽出
3. **抽出失敗時 drop**
   - index 範囲外 / JSONPath 不在
4. **値型の正規化**
   - number/bool/string/object の正規化が既存コネクタと整合
5. **timestamp/sequence fallback**
   - payload 未指定時は受信時刻/内部シーケンス採用

## 7.2 Integration tests（コネクタ単体 + 埋め込み broker）

テスト基盤候補:

- Testcontainers で Mosquitto コンテナ起動
- 実 MQTT client で publish

検証項目:

1. 単一 topic binding で取り込み成功
2. 複数 binding 混在時に期待どおりルーティング
3. QoS0/QoS1 で重複・欠損が許容範囲内
4. 再接続時に購読復旧

## 7.3 Backpressure tests（重点）

### A. Channel満杯時のBlock動作

- 条件:
  - `ChannelCapacity=32`
  - coordinator 側 sink を意図的に遅くする（例: 50ms/msg）
  - publisher は高速で連投（例: 5,000 msg burst）
- 期待:
  - コネクタが無制限メモリ増加しない
  - `mqtt_write_wait_ms_histogram` が増加
  - drop 0（Block ポリシー時）

### B. DropNewest動作

- 条件: `DropPolicy=DropNewest`
- 期待:
  - channel 満杯時に drop counter 増加
  - プロセス継続、例外停止しない

### C. Reconnect + Backpressure同時

- 条件:
  - 高負荷中に broker 再起動
  - reconnect 後に再購読
- 期待:
  - reconnect 成功
  - 過負荷状態でも connector がハングしない

### D. Soak test（長時間）

- 10〜30 分の継続 publish
- 観測:
  - メモリ使用量が発散しない
  - `received - parsed - dropped` の整合が取れる

## 8. 受け入れ基準（実装時のDoD）

1. 設定のみで topic 追加/変更できる（コード変更不要）
2. tenant/device/point の抽出失敗は安全に drop される
3. backpressure 条件で OOM/スレッド飽和を起こさない
4. unit/integration/backpressure テストが CI で再現可能
5. `dotnet build` / `dotnet test` が成功

## 9. 実装順序（推奨）

1. `MqttIngestOptions` + validation
2. topic/payload parser（unit test 先行）
3. `MqttIngestConnector`（接続・購読・write）
4. reconnect と metrics
5. integration/backpressure テスト追加

