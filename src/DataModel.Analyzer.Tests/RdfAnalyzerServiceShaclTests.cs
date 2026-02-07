using System.IO;
using System.Threading.Tasks;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class RdfAnalyzerServiceShaclTests
{
    [Fact]
    public async Task AnalyzeRdfContent_WithRequiredFields_PassesShacl()
    {
        const string ttl = @"@prefix rec: <https://w3id.org/rec/> .
    @prefix sbco: <https://www.sbco.or.jp/ont/> .

    <http://example.org/site1> a rec:Site ;
        rec:name ""Site 1"" ;
        sbco:id ""site1"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""site1-dtid"" ] ;
        rec:hasPart <http://example.org/building1> .

    <http://example.org/building1> a rec:Building ;
        sbco:id ""building1"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""building1-dtid"" ] .";

        var service = CreateService();

        var model = await service.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "shacl-valid");

        model.Sites.Should().ContainSingle();
        model.Sites[0].Name.Should().Be("Site 1");
    }

    [Fact]
    public async Task AnalyzeRdfContent_MissingRequiredField_FailsShacl()
    {
        // sbco:Building に sbco:name が不足（検証エラーになる）
        const string ttl = @"@prefix sbco: <https://www.sbco.or.jp/ont/> .
        @prefix rec: <https://w3id.org/rec/> .
    @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

    <http://example.org/site1> a rec:Site ;
        rec:name ""Site 1"" ;
        sbco:id ""site1"" .

    <http://example.org/building1> a rec:Building ;
        sbco:id ""building1"" .";

        var service = CreateService();

        var result = await service.AnalyzeRdfContentWithValidationAsync(ttl, RdfSerializationFormat.Turtle, "shacl-invalid");

        result.Validation.Should().NotBeNull();
        result.Validation!.Conforms.Should().BeFalse("Building が sbco:name（必須）を持たないため検証が失敗");
        result.Validation!.Messages.Should().NotBeEmpty("検証失敗メッセージが生成される");
    }

    [Fact]
    public async Task AnalyzeRdfContent_InvalidPattern_FailsShacl()
    {
        // sbco:id が pattern を満たさない（数字のみ）→ 検証失敗
        const string ttl = @"@prefix rec: <https://w3id.org/rec/> .
@prefix sbco: <https://www.sbco.or.jp/ont/> .

    <http://example.org/site1> a rec:Site ;
        rec:name ""Site 1"" ;
        sbco:id ""123"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""site1-dtid"" ] .";

        var service = CreateService();

        var result = await service.AnalyzeRdfContentWithValidationAsync(ttl, RdfSerializationFormat.Turtle, "shacl-invalid");

        result.Validation.Should().NotBeNull();
        result.Validation!.Conforms.Should().BeFalse("sbco:id が正規表現パターンを満たさないため検証が失敗");
        result.Validation!.Messages.Should().NotBeEmpty("検証失敗メッセージが生成される");
    }

    [Fact]
    public async Task AnalyzeRdfContent_WithComplexHierarchy_ParsesSuccessfully()
    {
        // seed-complex.ttl を読み込んでデータが正しく解析されることを検証
        var testFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "data",
            "seed-complex.ttl"
        );
        
        testFilePath = Path.GetFullPath(testFilePath);
        File.Exists(testFilePath).Should().BeTrue($"seed-complex.ttl ファイルが存在すること (path: {testFilePath})");

        var ttlContent = await File.ReadAllTextAsync(testFilePath);
        var service = CreateService();

        var model = await service.AnalyzeRdfContentAsync(ttlContent, RdfSerializationFormat.Turtle, "seed-complex");

        // サイト検証：Tokyo Technology Center が解析されること
        model.Sites.Should().NotBeEmpty("少なくとも1つのサイトが解析されること");
        model.Sites.Should().Contain(s => s.Name == "Tokyo Technology Center");

        // 建物検証：Main Office Building と Research Lab Building が解析されること
        model.Buildings.Should().NotBeEmpty("複数の建物が解析されること");
        model.Buildings.Select(b => b.Name).Should().Contain("Main Office Building");
        model.Buildings.Select(b => b.Name).Should().Contain("Research Lab Building");

        // レベル検証：複数のレベルが解析されること
        model.Levels.Should().NotBeEmpty("複数のレベルが解析されること");
        model.Levels.Select(l => l.Name).Should().Contain("Ground Floor");
        model.Levels.Select(l => l.Name).Should().Contain("Second Floor");
        model.Levels.Select(l => l.Name).Should().Contain("Lab Floor 1");

        // ポイント検証：複数のポイントが正しいユニットで解析されること
        model.Points.Should().NotBeEmpty("複数のポイントが解析されること");
        model.Points.Should().Satisfy(
            p => p.Name == "HVAC Supply Temperature" && p.Unit == "celsius",
            p => p.Name == "HVAC Supply Humidity" && p.Unit == "percent",
            p => p.Name == "Server Rack Power Consumption",
            p => p.Name == "Incubator Chamber Temperature",
            p => p.Name == "Chiller Discharge Temperature"
        );

        var site = model.Sites.Single(s => s.Name == "Tokyo Technology Center");
        var mainBuilding = model.Buildings.Single(b => b.Name == "Main Office Building");
        var labBuilding = model.Buildings.Single(b => b.Name == "Research Lab Building");
        mainBuilding.SiteUri.Should().Be(site.Uri);
        labBuilding.SiteUri.Should().Be(site.Uri);

        var groundFloor = model.Levels.Single(l => l.Name == "Ground Floor");
        var secondFloor = model.Levels.Single(l => l.Name == "Second Floor");
        var labFloor = model.Levels.Single(l => l.Name == "Lab Floor 1");
        groundFloor.BuildingUri.Should().Be(mainBuilding.Uri);
        secondFloor.BuildingUri.Should().Be(mainBuilding.Uri);
        labFloor.BuildingUri.Should().Be(labBuilding.Uri);

        var lobby = model.Areas.Single(a => a.Name == "Main Lobby");
        var serverRoom = model.Areas.Single(a => a.Name == "Server Room");
        var labRoom = model.Areas.Single(a => a.Name == "Experiment Room A");
        lobby.LevelUri.Should().Be(groundFloor.Uri);
        serverRoom.LevelUri.Should().Be(secondFloor.Uri);
        labRoom.LevelUri.Should().Be(labFloor.Uri);

        var hvac = model.Equipment.Single(e => e.Name == "HVAC Unit F1");
        var serverRack = model.Equipment.Single(e => e.Name == "Server Rack A");
        var incubator = model.Equipment.Single(e => e.Name == "Temperature Incubator");
        var chiller = model.Equipment.Single(e => e.Name == "Lab Chiller Unit");
        hvac.AreaUri.Should().Be(lobby.Uri);
        serverRack.AreaUri.Should().Be(serverRoom.Uri);
        incubator.AreaUri.Should().Be(labRoom.Uri);
        chiller.AreaUri.Should().Be(labRoom.Uri);
    }

    private static RdfAnalyzerService CreateService() => new(NullLogger<RdfAnalyzerService>.Instance);
}
