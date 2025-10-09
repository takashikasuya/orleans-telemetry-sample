# Orleans Telemetry Sample - DataModel.Analyzer

このプロジェクトは、Orleans Telemetry Sampleソリューションの一部として、TurtleやN-Triples、JSON-LDなど複数形式のRDFデータを解析し、建物データモデルとして構造化するライブラリです。

## プロジェクト概要

**目的**: GUTPプロトコルに基づく建物・設備・センサーデータのRDFファイルを解析し、Orleansクラスターで利用可能な形式に変換する。

**主要機能**:
- Turtle / N-Triples / JSON-LD / RDF/XML などのRDFファイル解析
- 建物データモデルの構造化
- Orleansデバイスコントラクトの生成
- JSON形式でのデータエクスポート
- Orleans統合サポート

## アーキテクチャ

```
DataModel.Analyzer/
├── Models/
│   └── BuildingDataModel.cs         # データモデルクラス
├── Services/
│   ├── RdfAnalyzerService.cs        # RDF解析サービス
│   └── DataModelExportService.cs    # データエクスポートサービス
├── Integration/
│   └── OrleansIntegrationService.cs # Orleans統合サービス
├── Extensions/
│   └── ServiceCollectionExtensions.cs # DI拡張
├── DataModelAnalyzer.cs             # ファサードクラス
└── Program.cs                       # サンプル実行プログラム
```

## 他プロジェクトとの連携

### ApiGateway
- デバイス情報のメタデータ提供
- gRPCサービスでの型情報として利用

### SiloHost
- デバイスGrainの初期化データ提供
- テレメトリルーティング設定

### Grains.Abstractions
- 共通のデバイスコントラクト定義参照

## 使用例

```csharp
// 基本的な解析
var analyzer = serviceProvider.GetRequiredService<DataModelAnalyzer>();
var model = await analyzer.AnalyzeFromFileAsync("building-data.ttl");

// Orleans統合
var orleansService = serviceProvider.GetRequiredService<OrleansIntegrationService>();
var deviceData = await orleansService.ExtractDeviceDataAsync("building-data.ttl");

// デバイス初期化データ生成
foreach (var device in deviceData.Devices)
{
    var initData = orleansService.CreateInitializationData(device);
    var grainKey = orleansService.GenerateDeviceGrainKey(device.DeviceId, device.GatewayId);
    // Orleans Grainに送信...
}
```

## 対応データ形式

### 入力
- Turtle / N-Triples / JSON-LD / RDF/XML / TriG / TriX / N-Quads 形式のRDFファイル
- GUTPプロトコル準拠のデータ構造
- REC (Real Estate Core) オントロジー

### 出力
- 構造化されたC#オブジェクト
- JSON形式
- Orleans用デバイスコントラクト
- 統計サマリー情報

## 設定とカスタマイズ

### ロギング
```csharp
services.AddDataModelAnalyzer(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
```

### 拡張性
- カスタムRDFプロパティマッピング
- 独自のエクスポート形式追加
- バリデーションルールのカスタマイズ

## 今後の拡張予定

1. **リアルタイム解析**: ファイル監視によるライブ更新
2. **スキーマ検証**: RDFスキーマベースの検証機能
3. **キャッシュ機能**: 解析結果のメモリ/Redis キャッシュ
4. **API化**: RESTful API としての提供
5. **フォーマット自動判別**: 拡張子以外のヒューリスティクスによる形式検出の改善
