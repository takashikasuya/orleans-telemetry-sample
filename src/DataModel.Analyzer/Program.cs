using DataModel.Analyzer;
using DataModel.Analyzer.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// サンプルプログラムのエントリーポイント
class Program
{
    static async Task Main(string[] args)
    {
        // 依存性注入の設定
        var services = new ServiceCollection()
            .AddDataModelAnalyzer(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

        var serviceProvider = services.BuildServiceProvider();
        var analyzer = serviceProvider.GetRequiredService<DataModelAnalyzer>();

        // サンプルTTLコンテンツ（実際のファイルからの読み込みも可能）
        var sampleTtlContent = @"
@prefix rec: <https://w3id.org/rec#> .
@prefix gutp: <https://www.gutp.jp/bim-wg#> .
@prefix site: <http://example.org/site#> .
@prefix building: <http://example.org/building#> .
@prefix level: <http://example.org/level#> .
@prefix area: <http://example.org/area#> .
@prefix device: <http://example.org/equipment#> .
@prefix point: <http://example.org/point#> .

site:TestSite a rec:Site ;
    rec:name ""Test Site"" ;
    rec:hasPart building:TestBuilding .

building:TestBuilding a rec:Building ;
    rec:name ""Test Building"" ;
    rec:hasPart level:Floor1 .

level:Floor1 a rec:Level ;
    rec:name ""1F"" ;
    rec:hasPart area:Room101 .

area:Room101 a rec:Area ;
    rec:name ""Room 101"" ;
    rec:isLocationOf device:TempSensor001 .

device:TempSensor001 a gutp:GUTPEquipment ;
    rec:name ""Temperature Sensor 001"" ;
    gutp:device_id ""TempSensor001"" ;
    gutp:gateway_id ""GW001"" ;
    gutp:device_type ""TemperatureSensor"" ;
    rec:hasPoint point:Temp001 .

point:Temp001 a gutp:GUTPPoint ;
    rec:name ""Temperature Point"" ;
    gutp:point_id ""Temp001"" ;
    gutp:point_type ""Temperature"" ;
    gutp:point_specification ""Measurement"" ;
    gutp:local_id ""TempSensor001"" ;
    gutp:writable false ;
    gutp:interval 60 ;
    gutp:unit ""degC"" ;
    gutp:max_pres_value 50.0 ;
    gutp:min_pres_value -10.0 ;
    rec:isPointOf device:TempSensor001 .
";

        try
        {
            Console.WriteLine("=== TTLデータモデル解析サンプル ===");
            Console.WriteLine();

            // TTLコンテンツを解析
            Console.WriteLine("1. TTLコンテンツを解析中...");
            var model = await analyzer.AnalyzeFromContentAsync(sampleTtlContent, "sample-ttl");

            // サマリーを表示
            Console.WriteLine("2. 解析結果のサマリー:");
            var summary = analyzer.GetSummary(model);
            Console.WriteLine($"   - ソース: {summary.Source}");
            Console.WriteLine($"   - 最終更新: {summary.LastUpdated:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"   - サイト数: {summary.SiteCount}");
            Console.WriteLine($"   - 建物数: {summary.BuildingCount}");
            Console.WriteLine($"   - レベル数: {summary.LevelCount}");
            Console.WriteLine($"   - エリア数: {summary.AreaCount}");
            Console.WriteLine($"   - 機器数: {summary.EquipmentCount}");
            Console.WriteLine($"   - ポイント数: {summary.PointCount}");
            Console.WriteLine();

            // JSONエクスポート
            Console.WriteLine("3. JSONエクスポート:");
            var json = analyzer.ExportToJson(model);
            Console.WriteLine($"   JSONサイズ: {json.Length} bytes");
            Console.WriteLine("   JSON（最初の200文字）:");
            Console.WriteLine($"   {json.Substring(0, Math.Min(200, json.Length))}...");
            Console.WriteLine();

            // Orleans用コントラクト生成
            Console.WriteLine("4. Orleans用コントラクト生成:");
            var contracts = analyzer.ExportToOrleansContracts(model);
            Console.WriteLine($"   生成されたコントラクト数: {contracts.Count}");
            Console.WriteLine();

            // ファイルが指定されている場合の処理例
            if (args.Length > 0)
            {
                var ttlFilePath = args[0];
                if (File.Exists(ttlFilePath))
                {
                    Console.WriteLine($"5. TTLファイルを処理中: {ttlFilePath}");
                    var result = await analyzer.ProcessTtlFileAsync(ttlFilePath, "output");

                    if (result.IsSuccess)
                    {
                        Console.WriteLine("   処理が完了しました。");
                        Console.WriteLine($"   処理時間: {result.ProcessingTime.TotalMilliseconds:F2}ms");
                        Console.WriteLine("   出力ファイル:");
                        foreach (var outputFile in result.OutputFiles)
                        {
                            Console.WriteLine($"     {outputFile.Key}: {outputFile.Value}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"   エラーが発生しました: {result.ErrorMessage}");
                    }
                }
                else
                {
                    Console.WriteLine($"   ファイルが見つかりません: {ttlFilePath}");
                }
            }
            else
            {
                Console.WriteLine("5. TTLファイルのパスを引数として指定すると、ファイル処理も実行されます。");
                Console.WriteLine("   例: dotnet run path/to/file.ttl");
            }

            Console.WriteLine();
            Console.WriteLine("=== 処理完了 ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            Console.WriteLine($"スタックトレース: {ex.StackTrace}");
        }
    }
}