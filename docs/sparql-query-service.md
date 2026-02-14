# SPARQL Query Service

このドキュメントは、Embedded SPARQL Query Engine for RDF の現状実装を説明します。

## 概要

現状の SPARQL 機能は以下の構成です。

- `SiloHost` 側に `SparqlQueryGrain` を実装
- `ApiGateway` から REST 経由で SPARQL クエリを実行
- テナントは JWT の `tenant` claim で分離

実装上、SPARQL データは Grain ID `"sparql"` の `ISparqlQueryGrain` に保存され、
テナントごとに triple を分離して保持します。

## エンドポイント

認証必須（`Authorization: Bearer <token>`）です。

### 1. RDF ロード

`POST /api/sparql/load`

Request body:

```json
{
  "content": "@prefix brick: <https://brickschema.org/schema/Brick#> . <urn:building1> a brick:Building .",
  "format": "turtle"
}
```

- `content`: RDF 本文（必須）
- `format`: RDF フォーマット（必須）
  - 現在の Grain 実装は `turtle`, `ntriples`, `rdfxml` 系をサポート

### 2. SPARQL クエリ実行

`POST /api/sparql/query`

Request body:

```json
{
  "query": "SELECT ?s WHERE { ?s a <https://brickschema.org/schema/Brick#Building> }"
}
```

Response body（例）:

```json
{
  "isBooleanResult": false,
  "booleanResult": false,
  "variables": ["s"],
  "rows": [
    { "values": { "s": "urn:building1" } }
  ]
}
```

### 3. 統計情報

`GET /api/sparql/stats`

Response body（例）:

```json
{
  "tripleCount": 2
}
```

## cURL サンプル

```bash
# 1) RDF ロード
curl -X POST http://localhost:8080/api/sparql/load \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"content":"@prefix brick: <https://brickschema.org/schema/Brick#> . <urn:building1> a brick:Building .","format":"turtle"}'

# 2) クエリ実行
curl -X POST http://localhost:8080/api/sparql/query \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"query":"SELECT ?s WHERE { ?s a <https://brickschema.org/schema/Brick#Building> }"}'

# 3) テナント単位トリプル数
curl -X GET http://localhost:8080/api/sparql/stats \
  -H "Authorization: Bearer <token>"
```

## 実装・テスト対応状況

- Grain 単体テスト: `SparqlQueryGrainTests`
- API Gateway 統合テスト: `SparqlEndpointTests`

将来的には以下を拡張予定です。

- `Sparql:Enabled` などの機能フラグ
- 外部 SPARQL Endpoint 抽象化（HTTP 接続）
- E2E テストの追加
