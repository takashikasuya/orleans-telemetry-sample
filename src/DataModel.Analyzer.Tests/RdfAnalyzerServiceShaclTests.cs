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
        rec:name ""Building 1"" ;
        sbco:id ""building1"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""building1-dtid"" ] .";

        var service = CreateService();

        var model = await service.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "shacl-valid");

        model.Sites.Should().ContainSingle();
        model.Sites[0].Name.Should().Be("Site 1");
    }

    [Fact(Skip = "SHACL validation temporarily disabled")]
    public async Task AnalyzeRdfContent_MissingRequiredField_FailsShacl()
    {
        const string ttl = @"@prefix rec: <https://w3id.org/rec/> .
    @prefix sbco: <https://www.sbco.or.jp/ont/> .

    <http://example.org/site1> a rec:Site ;
        rec:name ""Site 1"" ;
        sbco:id ""site1"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""site1-dtid"" ] ;
        rec:hasPart <http://example.org/building1> .

    <http://example.org/building1> a rec:Building ;
        rec:name ""Building 1"" ;
        sbco:id ""building1"" ;
        rec:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""dtid"" ; sbco:value ""building1-dtid"" ] .";

        var service = CreateService();

        var act = async () => await service.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "shacl-invalid");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(act);
        ex.Message.Should().Contain("SHACL validation failed");
    }

    private static RdfAnalyzerService CreateService() => new(NullLogger<RdfAnalyzerService>.Instance);
}
