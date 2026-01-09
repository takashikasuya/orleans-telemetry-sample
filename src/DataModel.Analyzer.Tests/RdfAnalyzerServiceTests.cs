using System;
using System.IO;
using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class RdfAnalyzerServiceTests
{
    [Fact]
    public async Task AnalyzeRdfContent_Turtle_BuildsHierarchy()
    {
        var ttl = @"@prefix sbco: <https://www.sbco.or.jp/ont/> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

<http://example.org/#site1> a sbco:Site ;
  sbco:name ""Site 1"" ;
  sbco:id ""SITE_1"" ;
  sbco:identifiers _:siteId1 ;
  sbco:hasPart <http://example.org/#b1> .

<http://example.org/#b1> a sbco:Building ;
  sbco:name ""Bldg 1"" ;
  sbco:id ""BLD_1"" ;
  sbco:hasPart <http://example.org/#l1> .

<http://example.org/#l1> a sbco:Level ;
  sbco:name ""1F"" ;
  sbco:id ""LVL_1"" ;
  sbco:levelNumber 1 ;
  sbco:hasPart <http://example.org/#s1> .

<http://example.org/#s1> a sbco:Space ;
  sbco:name ""Space 1"" ;
  sbco:id ""SPC_1"" ;
  sbco:isLocationOf <http://example.org/#eq1> .

<http://example.org/#eq1> a sbco:EquipmentExt ;
  sbco:name ""Device 1"" ;
  sbco:id ""EQP_1"" ;
  sbco:assetTag ""AT-1"" ;
  sbco:hasPoint <http://example.org/#p1> .

<http://example.org/#p1> a sbco:PointExt ;
  sbco:name ""Point 1"" ;
  sbco:id ""PNT_1"" ;
  sbco:pointType ""Temperature"" ;
  sbco:pointSpecification ""Measurement"" ;
  sbco:unit ""celsius"" ;
  sbco:isPointOf <http://example.org/#eq1> .

_:siteId1 a sbco:KeyStringMapEntry ;
  sbco:key ""site_code"" ;
  sbco:value ""SITE-001"" .
";

        var svc = CreateService();

        var shaclPath = Path.Combine(AppContext.BaseDirectory, "Schema", "building_model.shacl.ttl");
        if (!File.Exists(shaclPath))
        {
            shaclPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DataModel.Analyzer", "Schema", "building_model.shacl.ttl"));
        }

        var analysis = await svc.AnalyzeRdfContentWithValidationAsync(ttl, RdfSerializationFormat.Turtle, "turtle-test", shaclPath);

        analysis.Validation.Should().NotBeNull();
        analysis.Validation!.Conforms.Should().BeTrue();

        AssertStandardModel(analysis.Model);
    }

    [Fact]
    public async Task AnalyzeRdfFile_NTriples_BuildsHierarchy()
    {
        var nt = @"<http://example.org/#site1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Site> .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/name> ""Site 1"" .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/id> ""SITE_1"" .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/identifiers> _:siteId1 .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/hasPart> <http://example.org/#b1> .
<http://example.org/#b1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Building> .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/name> ""Bldg 1"" .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/id> ""BLD_1"" .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/hasPart> <http://example.org/#l1> .
<http://example.org/#l1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Level> .
<http://example.org/#l1> <https://www.sbco.or.jp/ont/name> ""1F"" .
<http://example.org/#l1> <https://www.sbco.or.jp/ont/id> ""LVL_1"" .
<http://example.org/#l1> <https://www.sbco.or.jp/ont/levelNumber> ""1"" .
<http://example.org/#l1> <https://www.sbco.or.jp/ont/hasPart> <http://example.org/#s1> .
<http://example.org/#s1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Space> .
<http://example.org/#s1> <https://www.sbco.or.jp/ont/name> ""Space 1"" .
<http://example.org/#s1> <https://www.sbco.or.jp/ont/id> ""SPC_1"" .
<http://example.org/#s1> <https://www.sbco.or.jp/ont/isLocationOf> <http://example.org/#eq1> .
<http://example.org/#eq1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/EquipmentExt> .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/name> ""Device 1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/id> ""EQP_1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/assetTag> ""AT-1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/hasPoint> <http://example.org/#p1> .
<http://example.org/#p1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/PointExt> .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/name> ""Point 1"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/id> ""PNT_1"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/pointType> ""Temperature"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/pointSpecification> ""Measurement"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/unit> ""celsius"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/isPointOf> <http://example.org/#eq1> .
_:siteId1 <https://www.sbco.or.jp/ont/key> ""site_code"" .
_:siteId1 <https://www.sbco.or.jp/ont/value> ""SITE-001"" .";

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.nt");
        await File.WriteAllTextAsync(tempPath, nt);

        try
        {
            var svc = CreateService();
            var model = await svc.AnalyzeRdfFileAsync(tempPath);

            AssertStandardModel(model);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeRdfContent_JsonLd_BuildsHierarchy()
    {
        var jsonLd = """
{
  "@context": {
    "sbco": "https://www.sbco.or.jp/ont/",
    "xsd": "http://www.w3.org/2001/XMLSchema#"
  },
  "@graph": [
    {
      "@id": "http://example.org/#site1",
      "@type": "sbco:Site",
      "sbco:name": "Site 1",
      "sbco:id": "SITE_1",
      "sbco:identifiers": { "@id": "_:siteId1" },
      "sbco:hasPart": { "@id": "http://example.org/#b1" }
    },
    {
      "@id": "http://example.org/#b1",
      "@type": "sbco:Building",
      "sbco:name": "Bldg 1",
      "sbco:id": "BLD_1",
      "sbco:hasPart": { "@id": "http://example.org/#l1" },
      "sbco:isPartOf": { "@id": "http://example.org/#site1" }
    },
    {
      "@id": "http://example.org/#l1",
      "@type": "sbco:Level",
      "sbco:name": "1F",
      "sbco:id": "LVL_1",
      "sbco:levelNumber": 1,
      "sbco:hasPart": { "@id": "http://example.org/#s1" },
      "sbco:isPartOf": { "@id": "http://example.org/#b1" }
    },
    {
      "@id": "http://example.org/#s1",
      "@type": "sbco:Space",
      "sbco:name": "Space 1",
      "sbco:id": "SPC_1",
      "sbco:isLocationOf": { "@id": "http://example.org/#eq1" },
      "sbco:isPartOf": { "@id": "http://example.org/#l1" }
    },
    {
      "@id": "http://example.org/#eq1",
      "@type": "sbco:EquipmentExt",
      "sbco:name": "Device 1",
      "sbco:id": "EQP_1",
      "sbco:assetTag": "AT-1",
      "sbco:hasPoint": { "@id": "http://example.org/#p1" }
    },
    {
      "@id": "http://example.org/#p1",
      "@type": "sbco:PointExt",
      "sbco:name": "Point 1",
      "sbco:id": "PNT_1",
      "sbco:pointType": "Temperature",
      "sbco:pointSpecification": "Measurement",
      "sbco:unit": "celsius",
      "sbco:isPointOf": { "@id": "http://example.org/#eq1" }
    },
    {
      "@id": "_:siteId1",
      "@type": "sbco:KeyStringMapEntry",
      "sbco:key": "site_code",
      "sbco:value": "SITE-001"
    }
  ]
}
""";

        var svc = CreateService();

        var model = await svc.AnalyzeRdfContentAsync(jsonLd, RdfSerializationFormat.JsonLd, "jsonld-test");

        AssertStandardModel(model);
    }

    private static RdfAnalyzerService CreateService() => new(NullLogger<RdfAnalyzerService>.Instance);

    private static void AssertStandardModel(BuildingDataModel model)
    {
        model.Sites.Should().HaveCount(1);
        model.Buildings.Should().HaveCount(1);
        model.Levels.Should().HaveCount(1);
        model.Areas.Should().HaveCount(1);
        model.Equipment.Should().HaveCount(1);
        model.Points.Should().HaveCount(1);

        var site = model.Sites[0];
        site.Name.Should().Be("Site 1");
        site.Identifiers.Should().ContainKey("site_code");
        site.Buildings.Should().ContainSingle();

        var building = site.Buildings[0];
        building.Name.Should().Be("Bldg 1");
        building.Levels.Should().ContainSingle();

        var level = building.Levels[0];
        level.Name.Should().Be("1F");
        level.LevelNumber.Should().Be(1);
        level.Areas.Should().ContainSingle();

        var area = level.Areas[0];
        area.Name.Should().Be("Space 1");
        area.Equipment.Should().ContainSingle();

        var equipment = area.Equipment[0];
        equipment.Name.Should().Be("Device 1");
        equipment.AssetTag.Should().Be("AT-1");
        equipment.Points.Should().ContainSingle();

        var point = equipment.Points[0];
        point.Name.Should().Be("Point 1");
        point.PointType.Should().Be("Temperature");
        point.PointSpecification.Should().Be("Measurement");
        point.Unit.Should().Be("celsius");

        site.Buildings.Should().Contain(building);
        building.Levels.Should().Contain(level);
        level.Areas.Should().Contain(area);
        area.Equipment.Should().Contain(equipment);
        equipment.Points.Should().Contain(point);
    }
}
