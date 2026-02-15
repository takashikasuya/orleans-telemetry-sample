# Multi-Silo Cluster Validation Guide

## Overview

本ガイドは、Docker Composeを使用してOrleans複数クラスタの構成と動作を検証するための手順書です。

**基本構成**:
- **サイロ**: 3つのSilo (silo-a, silo-b, silo-c) がAdoNetクラスタリングで1つのクラスターを形成
- **DB**: PostgreSQL の OrleansMembershipTable により、Siloの自動発見と群集管理
- **ゲートウェイ**: api / admin は silo-a:30000 に接続（Gateway Discovery 経由で他のSiloへもアクセス可能）
- **テレメトリ**: publisher → RabbitMQ → silo-a/b/c（負荷分散）

---

## Prerequisites

1. **Docker & Docker Compose**
   ```bash
   docker --version
   docker compose version
   ```

2. **.NET 8 SDK** (ローカルビルド・テスト用)
   ```bash
   dotnet --version
   ```

3. **既に動作確認済みのプロジェクト**
   ```bash
   cd /home/takashi/projects/dotnet/orleans-telemetry-sample
   ```

---

## Quick Start: 単一 vs 複数 Silo

### 1. 単一 Silo 構成（ベースライン）

```bash
# 単一 silo での起動（既存の docker-compose.yml のみ）
docker compose up --build
```

**特徴**:
- silo が 1 つだけ起動
- api / admin は silo:30000 に直接接続
- ローカルテスト・開発用に最適

**ログ確認**:
```bash
# 別ターミナルで
docker compose logs -f silo | grep -i "membership\|started"
```

**期待される出力例**:
```
silo    | [*] Started silo 'telemetry-service' in version '0' with id '...'.
silo    | [*] Joined cluster as <...>
```

### 2. 複数 Silo 構成（スケーラビリティ検証）

```bash
# 複数 silo での起動（docker-compose.silo-multi.yml でオーバーライド）
docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml up --build
```

**特徴**:
- silo-a, silo-b, silo-c が同一クラスターを形成
- PostgreSQL の MembershipTable で相互発見
- api / admin は silo-a:30000 経由で全Siloにアクセス可能
- スケーリング・負荷分散検証用

**ログ確認**:
```bash
# 3つのSilo全てのログを監視
docker compose logs -f
```

**期待される出力例** (各Siloで):
```
silo-a | [*] Started silo 'telemetry-service' in version '0' with id '...'.
silo-a | [*] Joined cluster as <...>
silo-b | [*] Started silo 'telemetry-service' in version '0' with id '...'.
silo-b | [*] Joined cluster as <...>
silo-c | [*] Started silo 'telemetry-service' in version '0' with id '...'.
silo-c | [*] Joined cluster as <...>
```

---

## Verification Steps

### Step 1: Compose が正常起動していることを確認

```bash
docker compose ps
```

**期待される出力**（複数Silo構成の場合）:
```
NAME          COMMAND                  SERVICE      STATUS      PORTS
silo-a        "dotnet SiloHost.dll"   silo-a       Up          0.0.0.0:11111->11111/tcp, 0.0.0.0:30000->30000/tcp
silo-b        "dotnet SiloHost.dll"   silo-b       Up          
silo-c        "dotnet SiloHost.dll"   silo-c       Up          
orleans-db    "docker-entrypoint..."  orleans-db   Up          0.0.0.0:5432->5432/tcp
mq            "docker-entrypoint..."  mq           Up          0.0.0.0:5672->5672/tcp, 0.0.0.0:15672:management
api           "dotnet ApiGateway...."  api         Up          0.0.0.0:8080->80/tcp
admin         "dotnet AdminGateway..." admin       Up          0.0.0.0:8082->80/tcp
publisher     "dotnet Publisher.dll"  publisher    Up
```

### Step 2: PostgreSQL MembershipTable を確認

```bash
# postgres コンテナに接続
docker compose exec orleans-db psql -U orleans -d orleans

# MembershipTable の内容を確認
SELECT DeploymentId, Address, Port, Status, SiloName, HostName, IAmAliveTime
FROM OrleansMembershipTable
ORDER BY IAmAliveTime DESC
LIMIT 10;
```

**期待される出力**（複数Silo構成の場合）:
```
DeploymentId      | Address  | Port  | Status | SiloName     | HostName | IAmAliveTime
------------------+----------+-------+--------+--------------+----------+---------------------
telemetry-cluster | silo-a   | 11111 |    0   | silo-a       | silo-a   | 2026-02-16 12:34:56
telemetry-cluster | silo-b   | 11111 |    0   | silo-b       | silo-b   | 2026-02-16 12:34:55
telemetry-cluster | silo-c   | 11111 |    0   | silo-c       | silo-c   | 2026-02-16 12:34:54
```

**ステータスの意味**:
- `0`: Active（アクティブ）
- `1`: Dead（停止）
- `2`: Joining（参加中）

### Step 3: API Gateway で Gateway List を確認

```bash
curl -s http://localhost:8080/api/health | jq '.'
```

**期待される出力**:
```json
{
  "status": "Healthy",
  "timestamp": "2026-02-16T12:34:56Z",
  "grainFactory": "Initialized",
  "siloConnection": "Connected"
}
```

### Step 4: グラフ・デバイス初期化を確認

```bash
# RDF グラフが正常にシードされたか確認
curl -s http://localhost:8080/api/graph/summary | jq '.details'
```

**期待される出力例**:
```json
{
  "totalSites": 1,
  "totalBuildings": 1,
  "totalLevels": 3,
  "totalAreas": 4,
  "totalEquipment": 8,
  "totalPoints": 24
}
```

### Step 5: テレメトリ受信を確認

```bash
# publisher が telemetry を発射しているか確認（RabbitMQ ログ）
docker compose logs publisher | head -20
```

**期待される出力例**:
```
publisher | [*] Publishing telemetry for: Simulator-Building / Simulator-Area / Temperature Sensor
publisher | [*] Published 100 messages to queue 'telemetry'
```

### Step 6: Point Grain が telemetry を受け取っているか確認

```bash
# Silo が telemetry を処理しているログ
docker compose logs silo-a | grep -i "telemetry\|upsert" | head -10
```

---

## Troubleshooting

### 問題: silo-b/c が起動しない

**症状**:
```
silo-b | System.InvalidOperationException: Orleans Clustering is required
```

**原因**: `SiloHost__ClusteringMode` 環境変数が AdoNet になっていない

**解決策**:
```bash
# docker-compose.silo-multi.yml で以下を確認
grep "SiloHost__ClusteringMode" docker-compose.silo-multi.yml
# 出力: SiloHost__ClusteringMode: AdoNet
```

### 問題: API が Gateway に接続できない

**症状**:
```
api | Orleans.Runtime.Messaging.ConnectionFailedException: 
Unable to connect to endpoint S172.18.0.4:30000:0. Error: ConnectionRefused
```

**原因**: silo-a が正常起動していない

**解決策**:
```bash
docker compose logs silo-a | grep -i "error\|exception"
```

### 問題: PostgreSQL 接続エラー

**症状**:
```
silo-a | Npgsql.NpgsqlException: Connection refused
```

**原因**: PostgreSQL が起動していない、または接続文字列が誤っている

**確認**:
```bash
docker compose ps orleans-db
# Status が Up になっているか確認

docker compose logs orleans-db | grep -i "ready\|initialized"
```

### 問題: MembershipTable が空

**症状**:
```
postgres=# SELECT COUNT(*) FROM OrleansMembershipTable;
 count
-------
     0
(1 row)
```

**原因**: 初期化スクリプトが実行されていない

**解決策**:
```bash
# 初期化スクリプトをリセット・再実行
docker compose down -v  # volume も削除
docker compose up --build  # 再起動
```

---

## Performance Comparison: Single vs Multi Silo

### テスト方法

#### 準備：単一 Silo での測定

```bash
# 単一 silo で 5 分間実行
docker compose up --build &
sleep 30  # 起動待機
```

#### テレメトリ送信レート測定

```bash
# Silo ログから処理スループットを抽出
docker compose logs silo | grep -c "Upsert"
```

#### 複数 Silo での測定

```bash
# 既存の単一 silo を停止
docker compose down

# 複数 silo で再起動
docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml up --build &
sleep 45  # 初期化待機
```

#### メトリクス比較

| メトリクス | 単一Silo | 複数Silo (3x) | 期待改善 |
|-----------|---------|-------------|--------|
| Throughput (msg/sec) | ~500 | ~1200 | 2.4x |
| P95 Latency (ms) | ~50 | ~30 | 40% 削減 |
| メモリ使用量 (MB) | 200 | 450 | 3アプリ分 |
| CPU使用率 (%) | ~40 | ~35 | 分散効果 |

---

## Clean Up

### 一時停止（再開可能）

```bash
docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml stop
```

### 完全削除（リセット）

```bash
docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml down -v
```

### 全サービス削除（他の組成も含む）

```bash
docker compose down -v
```

---

## Related Documentation

- [clustering-and-scalability.md](docs/clustering-and-scalability.md) - Orleans Clustering 戦略と設定ガイド
- [local-setup-and-operations.md](docs/local-setup-and-operations.md) - ローカル開発環境セットアップ
- [rdf-loading-and-grains.md](docs/rdf-loading-and-grains.md) - グラフ読み込みと Grain 初期化
- [telemetry-routing-binding.md](docs/telemetry-routing-binding.md) - テレメトリ ルーティング
- [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) - アーキテクチャ概要
- [plans.md](plans.md) - 進行中のタスク・計画

---

## Summary

| 環境 | コマンド | 用途 | 起動時間 |
|------|--------|------|--------|
| **単一 Silo** | `docker compose up --build` | 開発・テスト | ~30秒 |
| **複数 Silo** | `docker compose -f docker-compose.yml -f docker-compose.silo-multi.yml up --build` | スケーリング検証 | ~45秒 |

**推奨ワークフロー**:
1. 単一 Silo で機能検証
2. 複数 Silo でスケーリング・クラスタリング検証
3. ログと MembershipTable で成功を確認
4. 負荷テストで性能差分を計測

---

## Next Steps

1. [docker-compose.silo-multi.yml](#) で 複数 Silo 起動
2. [verification steps](#step-1-compose-が正常起動していることを確認) で検証実行
3. [troubleshooting](#troubleshooting) で問題対応
4. [performance comparison](#performance-comparison-single-vs-multi-silo) でメトリクス計測
5. [plans.md](plans.md) の検証結果記録

---

**Document Version**: 2026-02-16  
**Target Project**: orleans-telemetry-sample  
**Orleans Version**: 8.x  
**PostgreSQL Version**: 15
