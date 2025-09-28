# DataModel.Analyzer

TTL（Turtle）形式のRDFデータを解析し、建物データモデルとして構造化するライブラリです。GUTPプロトコルに基づく建物・設備・センサーデータの解析とエクスポート機能を提供します。

## 機能

- **TTL解析**: TTL形式のRDFファイルまたはコンテンツを解析
- **データモデル変換**: RDFトリプルを構造化されたC#オブジェクトに変換
- **階層構築**: Site → Building → Level → Area → Equipment → Point の階層関係を自動構築
- **多様なエクスポート形式**:
  - JSON形式
  - Orleans用デバイスコントラクト
  - サマリー情報
- **ログ機能**: 詳細な処理ログとエラーハンドリング

## データモデル構造

```
Site (サイト)
├── Building (建物)
    ├── Level (フロア/レベル)
        ├── Area (エリア)
            ├── Equipment (機器)
                └── Point (データポイント)
```

## 使用方法

### 基本的な使用例

```csharp
using DataModel.Analyzer;
using DataModel.Analyzer.Extensions;
using Microsoft.Extensions.DependencyInjection;

// 依存性注入の設定
var services = new ServiceCollection()
    .AddDataModelAnalyzer()
    .AddLogging();

var serviceProvider = services.BuildServiceProvider();
var analyzer = serviceProvider.GetRequiredService<DataModelAnalyzer>();

// TTLファイルを解析
var model = await analyzer.AnalyzeFromFileAsync("path/to/file.ttl");

// サマリー情報を取得
var summary = analyzer.GetSummary(model);
Console.WriteLine($"サイト数: {summary.SiteCount}, 機器数: {summary.EquipmentCount}");

// JSONとしてエクスポート
var json = analyzer.ExportToJson(model);
await analyzer.ExportToJsonFileAsync(model, "output.json");

// Orleans用のコントラクトを生成
var orleansContracts = analyzer.ExportToOrleansContracts(model);
```

### TTLコンテンツから直接解析

```csharp
var ttlContent = @"
@prefix rec: <https://w3id.org/rec#> .
@prefix gutp: <https://www.gutp.jp/bim-wg#> .
...
";

var model = await analyzer.AnalyzeFromContentAsync(ttlContent, "my-source");
```

### 完全な処理パイプライン

```csharp
// 解析からエクスポートまでの完全な処理
var result = await analyzer.ProcessTtlFileAsync("input.ttl", "output-directory");

if (result.IsSuccess)
{
    Console.WriteLine($"処理時間: {result.ProcessingTime.TotalMilliseconds}ms");
    Console.WriteLine($"出力ファイル: {string.Join(", ", result.OutputFiles.Values)}");
}
```

## サポートするRDFプロパティ

### 共通プロパティ
- `rec:name` - リソース名
- `rec:identifiers` - 識別子
- `dct:identifier` - DTIDなどの識別子

### GUTP機器プロパティ
- `gutp:gateway_id` - ゲートウェイID  
- `gutp:device_id` - デバイスID
- `gutp:device_type` - デバイスタイプ
- `gutp:supplier` - 供給者
- `gutp:owner` - 所有者

### GUTPポイントプロパティ
- `gutp:point_id` - ポイントID
- `gutp:point_type` - ポイントタイプ（Temperature, CO2, Humidityなど）
- `gutp:point_specification` - ポイント仕様（Measurement, Command, Alarmなど）
- `gutp:local_id` - ローカルID
- `gutp:writable` - 書き込み可能フラグ
- `gutp:interval` - 測定間隔
- `gutp:unit` - 単位
- `gutp:max_pres_value` - 最大値
- `gutp:min_pres_value` - 最小値
- `gutp:scale` - スケール値

## 出力形式

### BuildingDataSummary
解析結果の統計情報：
- リソース数（サイト、建物、レベル、エリア、機器、ポイント）
- ポイントタイプ別の分布
- 機器タイプ別の分布

### Orleans DeviceContract
Orleans向けのデバイス情報：
- デバイス基本情報（ID、名前、タイプ）
- 位置情報パス（Site/Building/Level/Area）
- 関連するポイント情報

## 依存関係

- .NET 8.0
- dotNetRDF 3.2.0 - RDF処理
- Microsoft.Extensions.DependencyInjection 8.0.0 - DI
- Microsoft.Extensions.Logging 8.0.0 - ログ
- System.Text.Json 8.0.4 - JSON処理

## サンプル実行

```bash
# プロジェクトをビルド
dotnet build

# サンプル実行（コンソールアプリ）
dotnet run

# TTLファイルを指定して実行
dotnet run path/to/your/file.ttl
```

## ログレベル

- `Information`: 処理の開始・終了、統計情報
- `Warning`: 非致命的な問題
- `Error`: エラー情報とスタックトレース

## エラーハンドリング

- ファイル読み込みエラー
- RDF解析エラー  
- データ変換エラー
- ファイル出力エラー

すべてのエラーは適切にログ出力され、例外として再スローされます。