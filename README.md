# Orleans Telemetry Sample

This repository contains a minimal sample that demonstrates how to ingest
telemetry messages from RabbitMQ into an Orleans cluster, map them to
device‑scoped grains, and expose the latest state through a REST and gRPC
gateway.  A small publisher service publishes random temperature/humidity
telemetry to RabbitMQ to exercise the pipeline.

## Services

The solution is composed of four Docker services:

| Service        | Description                                                       |
|---------------|-------------------------------------------------------------------|
| `mq`           | RabbitMQ broker used as the message queue for incoming telemetry.|
| `silo`         | Orleans host containing grains and the RabbitMQ consumer.        |
| `api`          | ASP.NET Core application that exposes REST and gRPC endpoints.   |
| `publisher`    | .NET console app that publishes random telemetry to the queue.   |

To run the stack locally you can use Docker Compose:

```bash
docker compose up --build
```

Once running you can navigate to `http://localhost:8080/swagger` to inspect
the REST API and `http://localhost:8080` to call the gRPC service via a client.

This sample is intentionally simple and is not hardened for production use.


```mermaid
sequenceDiagram
    autonumber
    actor Pub as Publisher
    participant MQ as RabbitMQ (queue: telemetry)
    participant Ingest as SiloHost: MqIngestService
    participant Router as TelemetryRouterGrain (Stateless)
    participant Dev as DeviceGrain("{tenant}:{deviceId}")
    participant Stream as Orleans Stream("DeviceUpdates")
    participant API as API Gateway (REST/gRPC/GraphQL)
    actor Rest as REST Client
    actor Grpc as gRPC Client
    actor GQL as GraphQL Client

    %% --- 発行 ---
    Pub->>MQ: JSON {deviceId, sequence, properties}
    note right of Pub: 任意周期で発行（デモのPublisher）

    %% --- 取り込み & ルーティング ---
    Ingest-->>MQ: BasicConsume(prefetch=N)
    MQ-->>Ingest: Deliver message
    Ingest->>Ingest: JSONパース/バリデーション
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

