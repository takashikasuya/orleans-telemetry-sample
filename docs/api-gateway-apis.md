# ApiGateway API説明

ApiGateway は Orleans の最新状態・グラフ・履歴データを REST API で公開します。認証は JWT Bearer を前提とし、`tenant` クレームでテナントを分離します。`tenant` が無い場合は `t1` が既定になります。

## ベースURL

- ローカル Docker: `http://localhost:8080`
- Swagger UI (Development 時のみ): `http://localhost:8080/swagger`

## 認証・テナント

- すべての REST エンドポイントは `Authorization: Bearer <token>` が必須です。
- `tenant` クレームが無い場合は `t1` として扱われます。
- OIDC 設定は `OIDC_AUTHORITY` と `OIDC_AUDIENCE` で指定します。

## REST API 一覧

### デバイス最新状態

`GET /api/devices/{deviceId}`

- 説明: 指定デバイスの最新スナップショットを返します。
- パス: `deviceId` (必須)
- レスポンス: `200 OK`
  - `deviceId` (string)
  - `lastSequence` (long)
  - `updatedAt` (ISO 8601)
  - `properties` (key/value JSON)
  - `points` (object)
    - `pointType` をキーに `{ value, updatedAt }` を返します
    - `value` と `updatedAt` のみ返却し、他のメタデータは別 API で取得します
- 備考: `ETag` ヘッダに `W/"{lastSequence}"` を返します。

### デバイス制御要求

`POST /api/devices/{deviceId}/control`

- 説明: 書き込み可能なポイントに対する制御リクエストを受け付けます。
- パス: `deviceId` (必須)
- ボディ: `PointControlRequest`
  - `commandId` (string, 任意。空の場合はサーバ側で生成)
  - `buildingName` (string, 任意)
  - `spaceId` (string, 任意)
  - `deviceId` (string, 必須。パスと一致必須)
  - `pointId` (string, 必須)
  - `desiredValue` (任意)
  - `metadata` (dictionary<string,string>, 任意)
- レスポンス: `202 Accepted`
  - `Location` ヘッダ: `/api/devices/{deviceId}/control/{commandId}`
  - ボディ: `PointControlResponse`
    - `commandId`
    - `status`
    - `requestedAt`
    - `acceptedAt`
    - `appliedAt`
    - `connectorName`
    - `correlationId`
    - `lastError`
- エラー:
  - `400 BadRequest`: `deviceId` 不一致、`pointId` 不足など
- 備考: 現状は制御要求の受付と状態記録までで、実際の機器書き込みは Publisher 側の対応に依存します。

### ノード情報

`GET /api/nodes/{nodeId}`

- 説明: グラフノードの定義と入出エッジを返します。
- パス: `nodeId` (必須)
- レスポンス: `200 OK`
  - `node` (GraphNodeDefinition)
    - `nodeId`, `nodeType`, `displayName`, `attributes`
  - `outgoingEdges` (配列)
  - `incomingEdges` (配列)
  - `points` (object)
    - `pointType` をキーに `{ value, updatedAt }` を返します
    - `value` と `updatedAt` のみ返却し、他のメタデータは別 API で取得します

### ノードに紐づく最新ポイント値

`GET /api/nodes/{nodeId}/value`

- 説明: ノード属性から `PointId` / `DeviceId` を解決し、ポイント最新値を返します。
- パス: `nodeId` (必須)
- レスポンス: `200 OK` (`PointSnapshot`)
  - `lastSequence`, `latestValue`, `updatedAt`
- エラー:
  - `404 NotFound`: `PointId` または `DeviceId` 属性が不足

### グラフ探索

`GET /api/graph/traverse/{nodeId}?depth={n}&predicate={p}`

- 説明: 指定ノードから BFS で探索します。
- クエリ:
  - `depth` (任意, 0〜5。範囲外は 0/5 に丸め)
  - `predicate` (任意, フィルタ)
- レスポンス: `200 OK`
  - `startNodeId`, `depth`, `nodes` (GraphNodeSnapshot 配列)

### レジストリ（ノード一覧）

`GET /api/registry/devices`
`GET /api/registry/spaces`
`GET /api/registry/points`
`GET /api/registry/buildings`
`GET /api/registry/sites`

- 説明: 指定タイプのノード一覧を返します。
- クエリ:
  - `limit` (任意, 件数上限)
- レスポンス: `200 OK` (`RegistryQueryResponse`)
  - `mode` (`inline` | `url`)
  - `count` (返却件数)
  - `totalCount` (総件数)
  - `items` (inline 時のみ)
  - `url`, `expiresAt` (url 時のみ)
- 備考:
  - `limit` が未指定かつ件数が多い場合、`mode=url` でエクスポート URL を返します。
  - `limit <= 0` の場合は空配列を返します。

### レジストリエクスポート取得

`GET /api/registry/exports/{exportId}`

- 説明: 生成済みのレジストリエクスポートをダウンロードします。
- レスポンス:
  - `200 OK` (JSONL, `application/x-ndjson`)
  - `404 NotFound` (存在しない)
  - `410 Gone` (期限切れ)

### テレメトリ履歴クエリ

`GET /api/telemetry/{deviceId}?from={iso}&to={iso}&pointId={id}&limit={n}`

- 説明: Parquet から指定期間の履歴を検索します。
- パス: `deviceId` (必須)
- クエリ:
  - `from` (任意, ISO 8601)
  - `to` (任意, ISO 8601)
  - `pointId` (任意)
  - `limit` (任意)
- 既定値:
  - `to` 未指定: 現在時刻
  - `from` 未指定: `to - 15分`
- レスポンス: `200 OK` (`TelemetryQueryResponse`)
  - `mode` (`inline` | `url`)
  - `count`
  - `items` (inline 時のみ)
  - `url`, `expiresAt` (url 時のみ)
- `items` の構造 (`TelemetryQueryResult`)
  - `tenantId`, `deviceId`, `pointId`
  - `occurredAt`, `sequence`
  - `valueJson`, `payloadJson`, `tags`

### テレメトリエクスポート取得

`GET /api/telemetry/exports/{exportId}`

- 説明: 生成済みのテレメトリエクスポートをダウンロードします。
- レスポンス:
  - `200 OK` (JSONL)
  - `404 NotFound`
  - `410 Gone`

### SPARQL クエリ

`POST /api/sparql/load`
`POST /api/sparql/query`
`GET /api/sparql/stats`

- 説明: RDF ロード、SPARQL 実行、テナント単位 triple 数取得を提供します。
- 認証: 必須（JWT）
- 備考: 返却フォーマット詳細は `docs/sparql-query-service.md` を参照してください。

## gRPC API（現状と拡張計画）

gRPC は `devices.proto` ベースで一部実装済みです。現状の提供 API と、将来的な拡張計画を以下に整理します。

### gRPC 認証・テナント

- 認証は REST と同様に JWT Bearer を使用します。
- gRPC メタデータの `authorization: Bearer <token>` を必須とします。
- `tenant` クレームでテナント分離し、未指定の場合は `t1` を既定値とします。

### REST ↔ gRPC 対応表

- `GET /api/devices/{deviceId}` → `DeviceService.GetSnapshot`
- `POST /api/devices/{deviceId}/control` → `ControlService.SubmitControl`
- `GET /api/nodes/{nodeId}` → `GraphService.GetNode`
- `GET /api/nodes/{nodeId}/value` → `GraphService.GetNodeValue`
- `GET /api/graph/traverse/{nodeId}` → `GraphService.Traverse`
- `GET /api/registry/*` → `RegistryService.ListNodes`
- `GET /api/registry/exports/{exportId}` → `RegistryService.DownloadExport` (server streaming)
- `GET /api/telemetry/{deviceId}` → `TelemetryService.Query`
- `GET /api/telemetry/exports/{exportId}` → `TelemetryService.DownloadExport` (server streaming)

### proto ファイル（公開想定）

以下は REST 等価の gRPC を提供するための proto 定義案です。現時点では設計案であり、実装と合わせて更新が必要です。

```proto
syntax = "proto3";

option csharp_namespace = "ApiGateway.Grpc.V1";

package apigateway.v1;

import "google/protobuf/timestamp.proto";

message DeviceKey {
  string device_id = 1;
}

message PointKey {
  string device_id = 1;
  string point_id = 2;
}

message Snapshot {
  string device_id = 1;
  int64 last_sequence = 2;
  google.protobuf.Timestamp updated_at = 3;
  map<string, string> properties_json = 4;
}

message PointSnapshot {
  int64 last_sequence = 1;
  string latest_value_json = 2;
  google.protobuf.Timestamp updated_at = 3;
}

message PointControlRequest {
  string command_id = 1;
  string building_name = 2;
  string space_id = 3;
  string device_id = 4;
  string point_id = 5;
  string desired_value_json = 6;
  map<string, string> metadata = 7;
}

message PointControlResponse {
  string command_id = 1;
  string status = 2;
  google.protobuf.Timestamp requested_at = 3;
  google.protobuf.Timestamp accepted_at = 4;
  google.protobuf.Timestamp applied_at = 5;
  string connector_name = 6;
  string correlation_id = 7;
  string last_error = 8;
}

message GraphNodeDefinition {
  string node_id = 1;
  string node_type = 2;
  string display_name = 3;
  map<string, string> attributes = 4;
}

message GraphEdge {
  string predicate = 1;
  string target_node_id = 2;
}

message GraphNodeSnapshot {
  GraphNodeDefinition node = 1;
  repeated GraphEdge outgoing_edges = 2;
  repeated GraphEdge incoming_edges = 3;
}

message GraphTraversalRequest {
  string node_id = 1;
  int32 depth = 2;
  string predicate = 3;
}

message GraphTraversalResponse {
  string start_node_id = 1;
  int32 depth = 2;
  repeated GraphNodeSnapshot nodes = 3;
}

message RegistryQueryRequest {
  string node_type = 1;
  int32 limit = 2;
}

message RegistryNodeSummary {
  string node_id = 1;
  string node_type = 2;
  string display_name = 3;
  map<string, string> attributes = 4;
}

message RegistryInlineResult {
  repeated RegistryNodeSummary items = 1;
  int32 count = 2;
  int32 total_count = 3;
}

message RegistryExportRef {
  string url = 1;
  google.protobuf.Timestamp expires_at = 2;
  int32 count = 3;
  int32 total_count = 4;
}

message RegistryQueryResponse {
  oneof result {
    RegistryInlineResult inline = 1;
    RegistryExportRef export = 2;
  }
}

message TelemetryQueryRequest {
  string device_id = 1;
  google.protobuf.Timestamp from = 2;
  google.protobuf.Timestamp to = 3;
  string point_id = 4;
  int32 limit = 5;
}

message TelemetryQueryResult {
  string tenant_id = 1;
  string device_id = 2;
  string point_id = 3;
  google.protobuf.Timestamp occurred_at = 4;
  int64 sequence = 5;
  string value_json = 6;
  string payload_json = 7;
  map<string, string> tags = 8;
}

message TelemetryInlineResult {
  repeated TelemetryQueryResult items = 1;
  int32 count = 2;
}

message TelemetryExportRef {
  string url = 1;
  google.protobuf.Timestamp expires_at = 2;
  int32 count = 3;
}

message TelemetryQueryResponse {
  oneof result {
    TelemetryInlineResult inline = 1;
    TelemetryExportRef export = 2;
  }
}

message ExportDownloadRequest {
  string export_id = 1;
}

message ExportChunk {
  bytes data = 1;
}

service DeviceService {
  rpc GetSnapshot(DeviceKey) returns (Snapshot);
  rpc StreamUpdates(DeviceKey) returns (stream Snapshot);
}

service ControlService {
  rpc SubmitControl(PointControlRequest) returns (PointControlResponse);
}

service GraphService {
  rpc GetNode(GraphTraversalRequest) returns (GraphNodeSnapshot);
  rpc GetNodeValue(GraphTraversalRequest) returns (PointSnapshot);
  rpc Traverse(GraphTraversalRequest) returns (GraphTraversalResponse);
}

service RegistryService {
  rpc ListNodes(RegistryQueryRequest) returns (RegistryQueryResponse);
  rpc DownloadExport(ExportDownloadRequest) returns (stream ExportChunk);
}

service TelemetryService {
  rpc Query(TelemetryQueryRequest) returns (TelemetryQueryResponse);
  rpc DownloadExport(ExportDownloadRequest) returns (stream ExportChunk);
}
```

### 実装メモ

- 現行コードでは `Program.cs` で `DeviceService` と `RegistryGrpcService` を `MapGrpcService` 済みです（`Grpc:Enabled=true` の場合）。
- `src/ApiGateway/Protos/devices.proto` が実装済み契約であり、上記の大きな proto は将来拡張の設計案です。

## OpenAPI/Swagger 出力

ApiGateway は Swashbuckle を導入済みです。`ASPNETCORE_ENVIRONMENT=Development` のときに Swagger が有効になります。

### 開発環境での取得例

```bash
dotnet run --project src/ApiGateway
```

Swagger UI:
```
http://localhost:8080/swagger
```

OpenAPI JSON:
```
http://localhost:8080/swagger/v1/swagger.json
```

### Docker Compose での取得

`docker-compose.yml` の `api` サービスに `ASPNETCORE_ENVIRONMENT=Development` を追加すると Swagger が有効になります。既存の開発用フローでは、`scripts/start-system.sh` が `ASPNETCORE_ENVIRONMENT=Development` を指定する運用を想定しています。

## CustomTags ベース検索 API

RDF の `CustomTags` は Graph ノード属性へ `tag:<name>=true` で保存され、seed時にタグ逆引きインデックス Grain（`tag -> nodeIds`）へ登録されます。検索時はこのインデックスを使って候補ノードを絞り込みます。

### REST

- `GET /api/registry/search/nodes?tags=hot&tags=zone-a&limit=50`
  - 指定タグをすべて持つノードを返す。
- `GET /api/registry/search/grains?tags=hot&limit=50`
  - 指定タグを持つノードから導出できる Grain キー（Device/Point）を返す。

`tags` は `tags=hot,zone-a` のようなカンマ区切りも受け付けます。

### gRPC (`devices.v1.RegistryService`)

- `rpc SearchByTags(TagSearchRequest) returns (TagNodeSearchResponse)`
- `rpc SearchGrainsByTags(TagSearchRequest) returns (TagGrainSearchResponse)`

`TagSearchRequest.tags` にタグ一覧、`limit` に最大件数を指定します。
