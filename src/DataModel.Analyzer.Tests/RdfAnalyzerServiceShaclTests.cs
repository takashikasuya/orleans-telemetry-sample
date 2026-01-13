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

    private static RdfAnalyzerService CreateService() => new(NullLogger<RdfAnalyzerService>.Instance);
}
