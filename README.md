# Orleans Telemetry Sample

This repository contains a minimal sample that demonstrates how to ingest
telemetry messages into an Orleans cluster (via RabbitMQ or a simulator),
map them to device‑scoped grains, and expose the latest state through a REST
and gRPC gateway. A small publisher service publishes random
temperature/humidity telemetry to RabbitMQ to exercise the pipeline.

## Quick Start

Run the stack with Docker Compose:

```bash
docker compose up --build
```

Once running:
- REST Swagger: `http://localhost:8080/swagger`
- REST base: `http://localhost:8080`

Optional: seed the graph from an RDF file:

```bash
export RDF_SEED_PATH=/path/to/building-data.ttl
export TENANT_ID=default
docker compose up --build
```

## Startup Reference

### Docker Compose (recommended)

```bash
docker compose up --build
```

### Local (without Docker)

If using RabbitMQ, start it first. Then run each project:

```bash
dotnet run --project src/SiloHost
dotnet run --project src/ApiGateway
dotnet run --project src/Publisher
```

Environment variables:
- `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USER`, `RABBITMQ_PASS`
- `RDF_SEED_PATH`: path to RDF file to seed graph nodes/edges on startup
- `TENANT_ID`: tenant key used by graph seeding (default: `default`)

## Graph Model (Nodes, Edges, Values)

The project supports a graph representation of spaces/devices/points based on
`BuildingDataModel`. Each RDF resource becomes a graph node with edges between
them. Any node can also have bound values (not just devices).

Key endpoints (authorized):
- `GET /api/nodes/{nodeId}`: node metadata and edges
- `GET /api/nodes/{nodeId}/value`: latest bound values for a node
- `GET /api/graph/traverse/{nodeId}?depth=2&predicate=hasArea`: graph traversal

## Telemetry Flow

Telemetry messages are routed to device grains and persisted as the latest
state. The graph layer sits alongside this so you can traverse spaces/devices
and bind values to any node.

## Control Flow (Draft Interfaces)

This sample now defines draft interfaces for issuing control commands to
writable points and routing them through egress connectors. The implementation
is intentionally scoped to interfaces so each connector can define its own
acknowledgement and confirmation behavior.

### REST API (planned)

Control requests are submitted per point and return a command status handle:

```
POST /api/points/{pointId}/control
{
  "commandId": "cmd-123",
  "buildingName": "building",
  "spaceId": "space",
  "deviceId": "device-1",
  "pointId": "setpoint-1",
  "desiredValue": 22.5,
  "metadata": { "source": "operator" }
}
```

Control status is queried by command id:

```
GET /api/points/{pointId}/control/{commandId}
```

### Grain Interface

The control grain accepts a request and returns a status snapshot.

```
Task<PointControlSnapshot> SubmitAsync(PointControlRequest request);
Task<PointControlSnapshot?> GetAsync(string commandId);
```

### Egress Connector Interface

Connectors expose a send API and report their confirmation mode:

```
Task<ControlEgressResult> SendAsync(ControlEgressRequest request, CancellationToken ct);
```

### Control Sequence

```mermaid
sequenceDiagram
    autonumber
    actor Client as REST Client
    participant API as API Gateway
    participant Ctrl as PointControlGrain
    participant Egress as ControlEgressCoordinator
    participant Conn as Egress Connector
    participant Point as PointGrain

    Client->>API: POST /api/points/{pointId}/control
    API->>Ctrl: SubmitAsync(request)
    Ctrl-->>API: Snapshot (Pending)
    API-->>Client: 202 Accepted (commandId)

    Ctrl->>Egress: Enqueue request
    Egress->>Conn: SendAsync(request)
    Conn-->>Egress: Accepted (ACK + correlation)
    Egress->>Ctrl: Mark Accepted

    alt TelemetryConfirm
        Point-->>Ctrl: New value observed (Applied)
    else ReadBackConfirm
        Conn-->>Ctrl: Read-back ok (Applied)
    else AckOnly
        Ctrl-->>Ctrl: Finalize as Accepted
    end
```

## Services

The solution is composed of these Docker services:

| Service        | Description                                                       |
|---------------|-------------------------------------------------------------------|
| `mq`           | RabbitMQ broker used as the message queue for incoming telemetry (optional).|
| `silo`         | Orleans host containing grains and telemetry ingest connectors.  |
| `api`          | ASP.NET Core application that exposes REST and gRPC endpoints.   |
| `publisher`    | .NET console app that publishes random telemetry to the queue (optional).|

To run the stack locally you can use Docker Compose:

```bash
docker compose up --build
```

Once running you can navigate to `http://localhost:8080/swagger` to inspect
the REST API and `http://localhost:8080` to call the gRPC service via a client.

This sample is intentionally simple and is not hardened for production use.

## Telemetry ingest connectors

Telemetry ingestion is handled by `Telemetry.Ingest`. Connectors are registered in code
(currently in `src/SiloHost/Program.cs`), and the configuration selects which ones run.
To run with the built-in simulator:

```json
{
  "TelemetryIngest": {
    "Enabled": [ "Simulator" ],
    "BatchSize": 100,
    "ChannelCapacity": 10000,
    "Simulator": {
      "TenantId": "tenant",
      "BuildingName": "building",
      "SpaceId": "space",
      "DeviceIdPrefix": "sim-",
      "DeviceCount": 2,
      "PointsPerDevice": 3,
      "IntervalMilliseconds": 2000
    }
  }
}
```


```mermaid
sequenceDiagram
    autonumber
    actor Seed as Seeder
    actor Pub as Publisher
    participant MQ as RabbitMQ (queue: telemetry)
    participant Sim as Simulator Connector
    participant Ingest as SiloHost: TelemetryIngestCoordinator
    participant Router as TelemetryRouterGrain (Stateless)
    participant Dev as DeviceGrain("{tenant}:{deviceId}")
    participant Node as GraphNodeGrain("{tenant}:{nodeId}")
    participant Val as ValueBindingGrain("{tenant}:{nodeId}")
    participant Stream as Orleans Stream("DeviceUpdates")
    participant API as API Gateway (REST/gRPC/GraphQL)
    actor Rest as REST Client
    actor Grpc as gRPC Client
    actor GQL as GraphQL Client

    %% --- RDF Seed (Graph) ---
    Seed->>Node: UpsertAsync(node metadata)
    Seed->>Node: AddOutgoingEdgeAsync(edge)
    Seed->>Node: AddIncomingEdgeAsync(edge)

    %% --- 発行 ---
    alt Simulator enabled
        Sim->>Ingest: TelemetryPointMsg (periodic)
    else RabbitMQ enabled
        Pub->>MQ: JSON {deviceId, sequence, properties}
        note right of Pub: 任意周期で発行（デモのPublisher）
    end

    %% --- 取り込み & ルーティング ---
    opt RabbitMQ enabled
        Ingest-->>MQ: BasicConsume(prefetch=N)
        MQ-->>Ingest: Deliver message
        Ingest->>Ingest: JSONパース/バリデーション
    end
    Ingest->>Router: RouteAsync(msg)
    Router->>Dev: UpsertAsync(msg)\n(key = "{tenant}:{deviceId}")

    %% --- 状態更新 & 重複/逆順排除 ---
    Dev->>Dev: if (msg.Sequence <= LastSequence) ignore
    Dev->>Dev: LatestProps を upsert\nLastSequence を更新
    Dev->>Stream: OnNextAsync(DeviceSnapshot)
    Dev-->>Router: OK
    Router-->>Ingest: OK
    Ingest-->>MQ: ACK (成功後)

    %% --- API 経由の参照（REST） ---
    Rest->>API: GET /api/devices/{deviceId}\nAuthorization: Bearer <JWT>
    API->>API: OIDC検証\n(Authority/Audience, tenant claim)
    API->>Dev: GetAsync()
    Dev-->>API: DeviceSnapshot(LastSequence, Props, UpdatedAt)
    API-->>Rest: 200 OK + JSON (+ ETag: W/"LastSequence")

    %% --- Graph/Value 参照（REST） ---
    Rest->>API: GET /api/nodes/{nodeId}\nAuthorization: Bearer <JWT>
    API->>Node: GetAsync()
    Node-->>API: GraphNodeSnapshot
    API-->>Rest: 200 OK + JSON

    Rest->>API: GET /api/nodes/{nodeId}/value\nAuthorization: Bearer <JWT>
    API->>Val: GetAsync()
    Val-->>API: NodeValueSnapshot
    API-->>Rest: 200 OK + JSON

    Rest->>API: GET /api/graph/traverse/{nodeId}?depth=2
    API->>Node: GetAsync() x N
    API-->>Rest: 200 OK + JSON (nodes + edges)

    %% --- ストリーム配信（gRPC / GraphQL） ---
    Grpc->>API: DeviceService/StreamUpdates\nAuthorization: Bearer <JWT>
    API->>API: OIDC検証
    API->>Stream: Subscribe("{tenant}:{deviceId}")
    Stream-->>API: 初回スナップショット
    API-->>Grpc: Snapshot (server-side streaming)

    GQL->>API: GraphQL Subscription (deviceUpdates)\nAuthorization: Bearer <JWT>
    API->>API: OIDC検証
    API->>Stream: Subscribe("{tenant}:{deviceId}")
    Stream-->>API: 初回スナップショット
    API-->>GQL: Snapshot (WebSocket)

    %% --- 以後の更新（ブロードキャスト） ---
    loop 新着テレメトリー
        Ingest->>Router: RouteAsync(msg)
        Router->>Dev: UpsertAsync(msg)
        Dev->>Stream: OnNextAsync(DeviceSnapshot)
        Stream-->>API: Snapshot
        par push
            API-->>Grpc: Snapshot
            API-->>GQL: Snapshot
        and REST polling/ETag
            Rest->>API: GET (If-None-Match: W/"X")
            API->>Dev: GetAsync()
            Dev-->>API: Snapshot
            API-->>Rest: 200/304
        end
    end
```

```mermaid
classDiagram
    class IGraphNodeGrain {
      +UpsertAsync(GraphNodeDefinition)
      +AddOutgoingEdgeAsync(GraphEdge)
      +AddIncomingEdgeAsync(GraphEdge)
      +GetAsync() GraphNodeSnapshot
    }
    class IValueBindingGrain {
      +UpsertAsync(NodeValueUpdate)
      +GetAsync() NodeValueSnapshot
    }
    class IGraphIndexGrain {
      +AddNodeAsync(GraphNodeDefinition)
      +RemoveNodeAsync(nodeId, nodeType)
      +GetByTypeAsync(nodeType) List~string~
    }
    class GraphNodeDefinition {
      +NodeId
      +NodeType
      +DisplayName
      +Attributes
    }
    class GraphEdge {
      +Predicate
      +TargetNodeId
    }
    class GraphNodeSnapshot {
      +Node
      +OutgoingEdges
      +IncomingEdges
    }
    class NodeValueUpdate {
      +Sequence
      +Timestamp
      +Values
    }
    class NodeValueSnapshot {
      +LastSequence
      +Values
      +UpdatedAt
    }
    class IDeviceGrain {
      +UpsertAsync(TelemetryMsg)
      +GetAsync() DeviceSnapshot
    }
    class ITelemetryRouterGrain {
      +RouteAsync(TelemetryPointMsg)
      +RouteBatchAsync(TelemetryPointMsg[])
    }
    IGraphNodeGrain --> GraphNodeDefinition
    IGraphNodeGrain --> GraphEdge
    IGraphNodeGrain --> GraphNodeSnapshot
    IValueBindingGrain --> NodeValueUpdate
    IValueBindingGrain --> NodeValueSnapshot
    ITelemetryRouterGrain --> IDeviceGrain
```

```mermaid
flowchart LR
    subgraph Ingest
        IngestSvc[TelemetryIngestCoordinator]
        Sim[Simulator Connector]
        MQ[(RabbitMQ)]
        RMQ[RabbitMQ Connector]
        RouterGrain[TelemetryRouterGrain]
    end
    subgraph Orleans["Orleans Silo"]
        DevGrain[DeviceGrain]
        NodeGrain[GraphNodeGrain]
        ValGrain[ValueBindingGrain]
        IndexGrain[GraphIndexGrain]
        Stream[DeviceUpdates Stream]
    end
    subgraph API["API Gateway"]
        RestApi[REST endpoints]
        GrpcApi[gRPC endpoints]
        Traversal[GraphTraversal]
    end
    subgraph Tools
        SeedSvc[GraphSeedService]
        Analyzer[DataModel.Analyzer]
    end

    Sim --> IngestSvc --> RouterGrain --> DevGrain --> Stream
    MQ --> RMQ --> IngestSvc
    RestApi --> DevGrain
    RestApi --> NodeGrain
    RestApi --> ValGrain
    RestApi --> Traversal
    Traversal --> NodeGrain
    GrpcApi --> Stream
    SeedSvc --> Analyzer --> NodeGrain
    SeedSvc --> IndexGrain
```
