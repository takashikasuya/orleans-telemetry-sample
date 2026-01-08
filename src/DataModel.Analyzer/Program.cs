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

sbco:Site_001 a sbco:Site ;
    sbco:name ""Main Site"" ;
    sbco:id ""SITE_001"" ;
    sbco:identifiers _:siteId1 ;
    sbco:hasPart sbco:Building_001 .

sbco:Building_001 a sbco:Building ;
    sbco:name ""Headquarters Building"" ;
    sbco:id ""BLD_001"" ;
    sbco:identifiers _:bldId1 ;
    sbco:isPartOf sbco:Site_001 ;
    sbco:hasPart sbco:Level_01 .

sbco:Level_01 a sbco:Level ;
    sbco:name ""Level 1"" ;
    sbco:id ""LVL_01"" ;
    sbco:identifiers _:lvlId1 ;
    sbco:levelNumber 1 ;
    sbco:isPartOf sbco:Building_001 ;
    sbco:hasPart sbco:Space_101 .

sbco:Space_101 a sbco:Space ;
    sbco:name ""Room 101"" ;
    sbco:id ""SPC_101"" ;
    sbco:identifiers _:spcId1 ;
    sbco:isPartOf sbco:Level_01 .

sbco:AHU_01 a sbco:EquipmentExt ;
    sbco:name ""AHU-1"" ;
    sbco:id ""EQP_AHU1"" ;
    sbco:identifiers _:eqpId1 ;
    sbco:assetTag ""AT-1001"" ;
    sbco:installationDate ""2023-05-01""^^xsd:date ;
    sbco:IPAddress ""10.0.1.10"" ;
    sbco:locatedIn sbco:Space_101 ;
    sbco:hasPoint sbco:Point_Temp_101 .

sbco:Point_Temp_101 a sbco:PointExt ;
    sbco:name ""Room 101 Temperature"" ;
    sbco:id ""PNT_TEMP_101"" ;
    sbco:identifiers _:pntId1 ;
    sbco:pointType ""TemperatureSensorProfile"" ;
    sbco:pointSpecification ""Measurement"" ;
    sbco:unit ""celsius"" ;
    brick:hasQuantity ""Temperature"" ;
    brick:isPointOf sbco:AHU_01 ;
    sbco:minPresValue ""0.0""^^xsd:float ;
    sbco:maxPresValue ""50.0""^^xsd:float ;
    sbco:scale ""1.0""^^xsd:float ;
    brick:aggregate ""average"" ;
    brick:hasSubstance ""Air"" .

_:siteId1 a sbco:KeyStringMapEntry ;
    sbco:key ""site_code"" ;
    sbco:value ""SITE-001"" .

_:bldId1 a sbco:KeyStringMapEntry ;
    sbco:key ""building_code"" ;
    sbco:value ""HQ-01"" .

_:lvlId1 a sbco:KeyStringMapEntry ;
    sbco:key ""level_code"" ;
    sbco:value ""L1"" .

_:spcId1 a sbco:KeyStringMapEntry ;
    sbco:key ""room_code"" ;
    sbco:value ""101"" .

_:eqpId1 a sbco:KeyStringMapEntry ;
    sbco:key ""equipment_code"" ;
    sbco:value ""AHU-1"" .

_:pntId1 a sbco:KeyStringMapEntry ;
    sbco:key ""point_code"" ;
    sbco:value ""TEMP-101"" .
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
