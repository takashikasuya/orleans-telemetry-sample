using DataModel.Analyzer;
using DataModel.Analyzer.Extensions;
using DataModel.Analyzer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;

class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection()
            .AddDataModelAnalyzer(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

        var serviceProvider = services.BuildServiceProvider();
        var analyzer = serviceProvider.GetRequiredService<DataModelAnalyzer>();
        var sampleTurtleContent = @"
@prefix brick: <https://brickschema.org/schema/Brick#> .
@prefix rec:   <https://w3id.org/rec/> .
@prefix sbco:  <https://www.sbco.or.jp/ont/> .
@prefix xsd:   <http://www.w3.org/2001/XMLSchema#> .

sbco:Site_001 a rec:Site ;
    rec:name ""Main Site""^^xsd:string ;
    sbco:id ""SITE_001""^^xsd:string ;
    rec:identifiers _:siteId1 .

sbco:Building_001 a rec:Building ;
    rec:name ""Headquarters Building""^^xsd:string ;
    sbco:id ""BLD_001""^^xsd:string ;
    rec:identifiers _:bldId1 .

sbco:Level_01 a rec:Level ;
    rec:name ""Level 1""^^xsd:string ;
    sbco:id ""LVL_01""^^xsd:string ;
    rec:identifiers _:lvlId1 ;
    rec:levelNumber 1 .

sbco:Space_101 a rec:Space ;
    rec:name ""Room 101""^^xsd:string ;
    sbco:id ""SPC_101""^^xsd:string ;
    rec:identifiers _:spcId1 .

sbco:AHU_01 a sbco:EquipmentExt ;
    rec:name ""AHU-1""^^xsd:string ;
    sbco:id ""EQP_AHU1""^^xsd:string ;
    rec:identifiers _:eqpId1 ;
    rec:assetTag ""AT-1001""^^xsd:string ;
    rec:installationDate ""2023-05-01""^^xsd:date ;
    rec:locatedIn sbco:Space_101 ;
    rec:hasPoint sbco:Point_Temp_101 .

sbco:Point_Temp_101 a sbco:PointExt ;
    rec:name ""Room 101 Temperature""^^xsd:string ;
    sbco:id ""PNT_TEMP_101""^^xsd:string ;
    sbco:pointType ""TemperatureSensorProfile""^^xsd:string ;
    rec:identifiers _:pntId1 ;
    sbco:pointSpecification ""Measurement""^^xsd:string ;
    sbco:unit ""celsius""^^xsd:string ;
    brick:hasQuantity ""Temperature""^^xsd:string ;
    brick:isPointOf sbco:AHU_01 ;
    brick:aggregate ""average""^^xsd:string ;
    brick:hasSubstance ""Air""^^xsd:string .

_:siteId1 a sbco:KeyStringMapEntry ;
    sbco:key ""site_code""^^xsd:string ;
    sbco:value ""SITE-001""^^xsd:string .

_:bldId1 a sbco:KeyStringMapEntry ;
    sbco:key ""building_code""^^xsd:string ;
    sbco:value ""HQ-01""^^xsd:string .

_:lvlId1 a sbco:KeyStringMapEntry ;
    sbco:key ""level_code""^^xsd:string ;
    sbco:value ""L1""^^xsd:string .

_:spcId1 a sbco:KeyStringMapEntry ;
    sbco:key ""room_code""^^xsd:string ;
    sbco:value ""101""^^xsd:string .

_:eqpId1 a sbco:KeyStringMapEntry ;
    sbco:key ""equipment_code""^^xsd:string ;
    sbco:value ""AHU-1""^^xsd:string .

_:pntId1 a sbco:KeyStringMapEntry ;
    sbco:key ""point_code""^^xsd:string ;
    sbco:value ""TEMP-101""^^xsd:string .
";

        var shapesPath = Path.Combine(AppContext.BaseDirectory, "Schema", "building_model.shacl.ttl");

        try
        {
            Console.WriteLine("=== RDFデータモデル解析サンプル ===");
            Console.WriteLine();

            Console.WriteLine("1. Turtleコンテンツ解析中...");
            var analysis = await analyzer.AnalyzeFromContentWithValidationAsync(sampleTurtleContent, RdfSerializationFormat.Turtle, "sample-turtle", shapesPath);
            var model = analysis.Model;

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

            Console.WriteLine("3. SHACLバリデーション:");
            if (analysis.Validation != null)
            {
                Console.WriteLine($"   SHACL適合: {analysis.Validation.Conforms}");

                if (analysis.Validation.Messages.Any())
                {
                    Console.WriteLine("   バリデーションメッセージ:");
                    foreach (var msg in analysis.Validation.Messages)
                    {
                        Console.WriteLine($"     - {msg}");
                    }
                }
            }
            else
            {
                Console.WriteLine("   SHACLスキーマが見つかりません、または検証を実行できませんでした。");
            }

            Console.WriteLine();

            Console.WriteLine("4. JSONエクスポート");
            var json = analyzer.ExportToJson(model);
            Console.WriteLine($"   JSONサイズ: {json.Length} bytes");
            Console.WriteLine("   JSONの最初の200文字:");
            Console.WriteLine($"   {json.Substring(0, Math.Min(100000, json.Length))}...");
            Console.WriteLine();

            Console.WriteLine("5. Orleans用コントラクト生成");
            var contracts = analyzer.ExportToOrleansContracts(model);
            Console.WriteLine($"   生成されたコントラクト数: {contracts.Count}");
            
            // コントラクトの詳細を出力
            foreach (var contract in contracts)
            {
                Console.WriteLine($"   デバイス: {contract.DeviceName} (ID: {contract.DeviceId})");
                Console.WriteLine($"     - デバイスタイプ: {contract.DeviceType}");
                Console.WriteLine($"     - ゲートウェイID: {contract.GatewayId}");
                Console.WriteLine($"     - ロケーションパス: {contract.LocationPath}");
                Console.WriteLine($"     - ポイント数: {contract.Points.Count}");
                
                foreach (var point in contract.Points)
                {
                    Console.WriteLine($"       ポイント: {point.PointName} (ID: {point.PointId})");
                    Console.WriteLine($"         タイプ: {point.PointType}, 仕様: {point.PointSpecification}");
                    Console.WriteLine($"         ユニット: {point.Unit}, 書き込み可能: {point.Writable}");
                    if (point.MinValue.HasValue || point.MaxValue.HasValue)
                    {
                        Console.WriteLine($"         範囲: {point.MinValue} ～ {point.MaxValue}");
                    }
                    if (point.Interval.HasValue)
                    {
                        Console.WriteLine($"         間隔: {point.Interval}ms");
                    }
                }
            }
            Console.WriteLine();

            if (args.Length > 0)
            {
                var rdfFilePath = args[0];
                if (File.Exists(rdfFilePath))
                {
                    Console.WriteLine($"6. RDFファイルを処理: {rdfFilePath}");
                    var result = await analyzer.ProcessRdfFileAsync(rdfFilePath, "output", shapesPath);

                    if (result.IsSuccess)
                    {
                        Console.WriteLine("   処理が完了しました。");
                        Console.WriteLine($"   処理時間: {result.ProcessingTime.TotalMilliseconds:F2}ms");
                        if (result.ShaclConforms.HasValue)
                        {
                            Console.WriteLine($"   SHACL適合: {result.ShaclConforms}");
                            if (result.ShaclMessages.Any())
                            {
                                Console.WriteLine("   SHACLメッセージ:");
                                foreach (var msg in result.ShaclMessages)
                                {
                                    Console.WriteLine($"     - {msg}");
                                }
                            }
                        }
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
                    Console.WriteLine($"   ファイルが見つかりません: {rdfFilePath}");
                }
            }
            else
            {
                Console.WriteLine("6. RDFファイルのパスを引数に指定すると、ファイル処理が実行されます。");
                Console.WriteLine("   例: dotnet run path/to/file.ttl (または .jsonld / .nt など)");
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
