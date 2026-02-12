# RDF 読み込みと Orleans Grain 化 (RDF Loading → Orleans Grains)

このドキュメントは、RDF から建物データモデルへ変換し、Orleans の Graph/Device/Point Grains に初期化・バインディングするまでの流れをまとめます。
This document explains how RDF is parsed into the building data model and seeded/bound into Orleans grains.

関連資料:
- [README.md](README.md)
- [docs/telemetry-routing-binding.md](docs/telemetry-routing-binding.md)
- [docs/telemetry-connector-ingest.md](docs/telemetry-connector-ingest.md)
- [docs/telemetry-storage.md](docs/telemetry-storage.md)

---

## 全体像 (Overview)

- RDF を解析して `BuildingDataModel` を生成: [src/DataModel.Analyzer/DataModelAnalyzer.cs](src/DataModel.Analyzer/DataModelAnalyzer.cs), [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- SHACL による検証 (必要に応じて): [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- Orleans 連携用の契約・グラフシード生成: [src/DataModel.Analyzer/Services/DataModelExportService.cs](src/DataModel.Analyzer/Services/DataModelExportService.cs), [src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs](src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs)
- サイロ起動時のグラフ初期化 (RDF_SEED_PATH): [src/SiloHost/GraphSeedService.cs](src/SiloHost/GraphSeedService.cs), [src/SiloHost/Program.cs](src/SiloHost/Program.cs)

---

## RDF 読み込み (RDF Parsing)

- サポート形式: [src/DataModel.Analyzer/Services/RdfSerializationFormat.cs](src/DataModel.Analyzer/Services/RdfSerializationFormat.cs)  
  Turtle / N-Triples / JSON-LD / RDF/XML / TriG / TriX / N-Quads
- 主要 API:
  - コンテンツから解析: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
  - ファイルから解析: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
  - 検証付き解析: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs), [src/DataModel.Analyzer/DataModelAnalyzer.cs](src/DataModel.Analyzer/DataModelAnalyzer.cs)
- 実装の要点:
  - フォーマットに応じて dotNetRDF パーサーを選択し Graph/Store を読み込み
  - 読み込んだ Graph からサイト/建物/レベル/エリア/機器/ポイントを抽出し [src/DataModel.Analyzer/Models/BuildingDataModel.cs](src/DataModel.Analyzer/Models/BuildingDataModel.cs) に構造化

---

## データモデル (Building Data Model)

階層構造: Site → Building → Level → Area → Equipment → Point  
Classes:
- [src/DataModel.Analyzer/Models/BuildingDataModel.cs](src/DataModel.Analyzer/Models/BuildingDataModel.cs)（`Site`, `Building`, `Level`, `Area`, `Equipment`, `Point` を含む）

ポイントの主プロパティ（SBCO準拠）:
- PointId / PointType / PointSpecification / Writable / Interval / Unit / MinPresValue / MaxPresValue / Scale
- 実装抽出: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)

共通プロパティ抽出や識別子/ドキュメント/アドレスなども対応:  
[src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)

---

## オントロジーと名前空間 (Ontology & Namespaces)

RDF 解析時の主な名前空間定義は以下のとおりです（実体はコード内定数）。
Defined URIs in code:
- REC: `https://w3id.org/rec/`
- SBCO: `https://www.sbco.or.jp/ont/`
- Brick: `https://brickschema.org/schema/Brick#`
- DCT: `http://purl.org/dc/terms/`

SHACL/OWL スキーマの検索パス・ファイル名はオプションで制御:  
[src/DataModel.Analyzer/Services/RdfAnalyzerOptions.cs](src/DataModel.Analyzer/Services/RdfAnalyzerOptions.cs)  
- OntologyFile: `building_model.owl.ttl`  
- ShapesFile: `building_model.shacl.ttl`  
- SchemaFolder: `Schema`

---

## SHACL バリデーション (SHACL Validation)

- 実行箇所: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- Shapes 読み込み: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- 成果物: [src/DataModel.Analyzer/Services/RdfAnalysisResult.cs](src/DataModel.Analyzer/Services/RdfAnalysisResult.cs) に `Validation` を付与  
- レポート出力（ファイル処理時）: [src/DataModel.Analyzer/DataModelAnalyzer.cs](src/DataModel.Analyzer/DataModelAnalyzer.cs)

---

## Orleans 連携 (Export & Grain Seeding)

- デバイス/ポイント契約生成:
  - Export API: [src/DataModel.Analyzer/DataModelAnalyzer.cs](src/DataModel.Analyzer/DataModelAnalyzer.cs)
  - 契約モデル: [src/DataModel.Analyzer/Services/DataModelExportService.cs](src/DataModel.Analyzer/Services/DataModelExportService.cs)
- グラフシード生成:
  - 抽出 API: [src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs](src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs)
  - サイロ起動時の適用: [src/SiloHost/GraphSeedService.cs](src/SiloHost/GraphSeedService.cs)
  - 環境変数: `RDF_SEED_PATH`, `TENANT_ID`（参照: [README.md](README.md)）

Graph ノード属性 → PointGrain バインディングの流れは [docs/telemetry-routing-binding.md](docs/telemetry-routing-binding.md) を参照。  
Graph ノードは `Attributes` に `PointId`（表示用に `DeviceId` など）を持ち、API が `PointId` から PointGrainKey を組み立てて最新値を取得します。

---

## API/実行例 (Usage)

- DI 登録（SiloHost）: [src/SiloHost/Program.cs](src/SiloHost/Program.cs) の `services.AddDataModelAnalyzer();`
- RDF シード（Docker/ローカル）: [README.md](README.md) の「Seeding from RDF」を参照
- コンソールサンプル: [src/DataModel.Analyzer/Program.cs](src/DataModel.Analyzer/Program.cs)
- テスト: `dotnet test`, まず [src/DataModel.Analyzer.Tests/RdfAnalyzerServiceTests.cs](src/DataModel.Analyzer.Tests/RdfAnalyzerServiceTests.cs)

---

## 実装ポインタ (Implementation Pointers)

- RDF → Model 組み立て: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- 階層構築（親子関連のリンク）: [src/DataModel.Analyzer/Services/RdfAnalyzerService.cs](src/DataModel.Analyzer/Services/RdfAnalyzerService.cs)
- JSON エクスポート（System.Text.Json）: [src/DataModel.Analyzer/Services/DataModelExportService.cs](src/DataModel.Analyzer/Services/DataModelExportService.cs)  
  DataModelExportService の `JsonSerializerOptions` を使用（外部ライブラリは導入しない方針）。

---

## 補足 (Notes)

- Orleans はメモリストレージ/ストリームを使用（サンプル構成、永続化は Parquet に別途保存）  
  See [README.md](README.md) and [docs/telemetry-storage.md](docs/telemetry-storage.md)
- Point の種類拡張は [src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs](src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs) のマッピング箇所に追加
- レベル番号推定は [src/DataModel.Analyzer/Models/BuildingDataModel.cs](src/DataModel.Analyzer/Models/BuildingDataModel.cs) を使用し、独自パーサは導入しない方針
