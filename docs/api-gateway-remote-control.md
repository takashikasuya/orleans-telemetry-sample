# ApiGateway からの遠隔制御（Remote Control）実装メモ

このドキュメントは、`ApiGateway` からの遠隔制御で **どのコネクタへ制御要求をルーティングするか** を含めた現行実装を整理したものです。

## 要点

- `POST /api/devices/{deviceId}/control` は制御要求を受け付け、Orleans の `PointControlGrain` に記録します。
- 受理時に、Graph 上の `DeviceId + PointId` から `GatewayId` を解決し、設定ファイルの正規表現ルールで `ConnectorName` を決定します。
- ルールに一致しない場合は `400 BadRequest` を返し、曖昧な制御要求を拒否します。
- ApiGateway から `telemetry-control` キューへ直接 publish する処理はまだなく、配送実装は別途必要です。

## ルーティングの仕組み

1. `PointGatewayResolver` が Graph（Equipment → hasPoint → Point）を参照し、対象ポイントの `GatewayId` を解決。
2. `ControlConnectorRouter` が `config/control-routing.json` のルールを上から評価。
3. `GatewayPattern` / `DevicePattern` / `PointPattern`（正規表現）に一致した最初のルールを採用。
4. `metadata` に `ConnectorName`, `GatewayId`, `RoutingRule` を付与して `PointControlGrain.SubmitAsync` へ渡す。

## 設定ファイル

`config/control-routing.json` を参照します（`ControlRouting:ConfigPath` で上書き可能）。

```json
{
  "ControlRouting": {
    "DefaultConnector": "RabbitMq",
    "Rules": [
      { "Name": "mqtt-by-gateway", "Connector": "Mqtt", "GatewayPattern": "^mqtt-" },
      { "Name": "control-only-gateway", "Connector": "RabbitMq", "GatewayPattern": "^ctrl-" },
      { "Name": "point-fallback", "Connector": "RabbitMq", "PointPattern": "^(setpoint|command|override).*" }
    ]
  }
}
```

> `control-only-gateway` のような制御専用ゲートウェイは、`GatewayPattern` で明示的にルーティングします。

## API 挙動（制御）

`POST /api/devices/{deviceId}/control`

- 必須:
  - パス `deviceId` と body `deviceId` が一致
  - `pointId` が空でない
- 追加バリデーション:
  - ルーティング結果の `ConnectorName` が解決できない場合は `400`
- 正常系:
  - `202 Accepted`
  - `Location: /api/devices/{deviceId}/control/{commandId}`
  - `PointControlResponse.connectorName` に解決されたコネクタ名を返却

## 現時点の制約

- `Location` 先（`GET /api/devices/{deviceId}/control/{commandId}`）は未実装です。
- `ControlRequestStatus` は現状 `Accepted` までで、`Applied/Failed` などの更新フローは未接続です。
- ApiGateway→Publisher control queue への直接配送機能は未実装です。

## 今後の実装候補

- `ConnectorName` に応じた egress（RabbitMQ/MQTT/Kafka など）を ApiGateway 側に実装
- 制御結果（ack/nack）を受けて `PointControlSnapshot` を `Applied/Failed` へ更新
- 制御履歴照会 API（`GET /api/devices/{deviceId}/control/{commandId}`）を追加
