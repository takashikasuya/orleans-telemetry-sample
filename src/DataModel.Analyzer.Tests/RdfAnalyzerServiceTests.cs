using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class RdfAnalyzerServiceTests
{
    [Fact]
    public async Task AnalyzeRdfContent_Turtle_BuildsHierarchy()
    {
        var ttl = @"@prefix sbco: <https://www.sbco.or.jp/ont/> .
@prefix rec: <https://w3id.org/rec/> .
@prefix brick: <https://brickschema.org/schema/Brick#> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

<http://example.org/#site1> a sbco:Site ;
  sbco:name ""Site 1"" ;
  sbco:id ""SITE_1"" ;
  sbco:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""site_code"" ; sbco:value ""SITE-001"" ] ;
  sbco:documentation <http://example.org/#site-doc1> ;
  rec:customProperties [ a sbco:KeyMapOfStringMapEntry ; sbco:key ""site_notes"" ; sbco:entries [ a sbco:KeyStringMapEntry ; sbco:key ""summary"" ; sbco:value ""Main campus"" ] ] ;
  rec:customTags [ a sbco:KeyBoolMapEntry ; sbco:key ""primary"" ; sbco:flag ""true""^^xsd:boolean ] ;
  sbco:hasPart <http://example.org/#b1> .

<http://example.org/#b1> a sbco:Building ;
  sbco:name ""Bldg 1"" ;
  sbco:id ""BLD_1"" ;
  sbco:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""building_code"" ; sbco:value ""BLD-001"" ] ;
  sbco:hasPart <http://example.org/#l1> .

_:buildingId1 a sbco:KeyStringMapEntry ;
  sbco:key ""building_code"" ;
  sbco:value ""BLD-001"" .

<http://example.org/#l1> a sbco:Level ;
  sbco:name ""1F"" ;
  sbco:id ""LVL_1"" ;
  sbco:levelNumber 1 ;
  sbco:hasPart <http://example.org/#s1> .

<http://example.org/#s1> a sbco:Space ;
  sbco:name ""Space 1"" ;
  sbco:id ""SPC_1"" ;
  sbco:isLocationOf <http://example.org/#eq1> .

<http://example.org/#eq1> a sbco:Equipment ;
  sbco:name ""Device 1"" ;
  sbco:id ""EQP_1"" ;
  sbco:assetTag ""AT-1"" ;
  brick:feeds <http://example.org/#eq-feed> ;
  sbco:isFedBy <http://example.org/#eq-fedby> ;
  sbco:hasPoint <http://example.org/#p1> .

<http://example.org/#p1> a sbco:Point ;
  sbco:name ""Point 1"" ;
  sbco:id ""PNT_1"" ;
  sbco:pointType ""Temperature"" ;
  sbco:pointSpecification ""Measurement"" ;
  sbco:unit ""celsius"" ;
  sbco:isPointOf <http://example.org/#eq1> .

<http://example.org/#site-doc1> a sbco:Document ;
  sbco:name ""Site Plan"" ;
  sbco:url ""https://example.org/site-plan.pdf"" ;
  sbco:format ""application/pdf"" ;
  sbco:version ""1.0"" ;
  sbco:language ""en"" ;
  sbco:size 1024 ;
  sbco:checksum ""abc123"" ;
  sbco:identifiers [ a sbco:KeyStringMapEntry ; sbco:key ""doc_code"" ; sbco:value ""DOC-001"" ] .
";

        var svc = CreateService();
        var model = await svc.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "turtle-test");

        AssertStandardModel(model);
    }

    [Fact]
    public async Task AnalyzeRdfFile_NTriples_BuildsHierarchy()
    {
        var nt = @"<http://example.org/#site1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Site> .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/name> ""Site 1"" .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/id> ""SITE_1"" .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/identifiers> _:siteId1 .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/documentation> <http://example.org/#site-doc1> .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/customProperties> _:siteProps .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/customTags> _:siteTag .
<http://example.org/#site1> <https://www.sbco.or.jp/ont/hasPart> <http://example.org/#b1> .
<http://example.org/#b1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Building> .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/name> ""Bldg 1"" .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/id> ""BLD_1"" .
<http://example.org/#b1> <https://www.sbco.or.jp/ont/identifiers> _:buildingId1 .
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
<http://example.org/#eq1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Equipment> .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/name> ""Device 1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/id> ""EQP_1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/assetTag> ""AT-1"" .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/hasPoint> <http://example.org/#p1> .
<http://example.org/#eq1> <https://brickschema.org/schema/Brick#feeds> <http://example.org/#eq-feed> .
<http://example.org/#eq1> <https://www.sbco.or.jp/ont/isFedBy> <http://example.org/#eq-fedby> .
<http://example.org/#p1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Point> .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/name> ""Point 1"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/id> ""PNT_1"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/pointType> ""Temperature"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/pointSpecification> ""Measurement"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/unit> ""celsius"" .
<http://example.org/#p1> <https://www.sbco.or.jp/ont/isPointOf> <http://example.org/#eq1> .
<http://example.org/#site-doc1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/Document> .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/name> ""Site Plan"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/url> ""https://example.org/site-plan.pdf"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/format> ""application/pdf"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/version> ""1.0"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/language> ""en"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/size> ""1024"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/checksum> ""abc123"" .
<http://example.org/#site-doc1> <https://www.sbco.or.jp/ont/identifiers> _:siteDocId .
_:siteDocId <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyStringMapEntry> .
_:siteDocId <https://www.sbco.or.jp/ont/key> ""doc_code"" .
_:siteDocId <https://www.sbco.or.jp/ont/value> ""DOC-001"" .
_:siteProps <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyMapOfStringMapEntry> .
_:siteProps <https://www.sbco.or.jp/ont/key> ""site_notes"" .
_:siteProps <https://www.sbco.or.jp/ont/entries> _:sitePropsEntry .
_:sitePropsEntry <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyStringMapEntry> .
_:sitePropsEntry <https://www.sbco.or.jp/ont/key> ""summary"" .
_:sitePropsEntry <https://www.sbco.or.jp/ont/value> ""Main campus"" .
_:siteTag <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyBoolMapEntry> .
_:siteTag <https://www.sbco.or.jp/ont/key> ""primary"" .
_:siteTag <https://www.sbco.or.jp/ont/flag> ""true""^^<http://www.w3.org/2001/XMLSchema#boolean> .
_:buildingId1 <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyStringMapEntry> .
_:buildingId1 <https://www.sbco.or.jp/ont/key> ""building_code"" .
_:buildingId1 <https://www.sbco.or.jp/ont/value> ""BLD-001"" .
_:siteId1 <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.sbco.or.jp/ont/KeyStringMapEntry> .
_:siteId1 <https://www.sbco.or.jp/ont/key> ""site_code"" .
_:siteId1 <https://www.sbco.or.jp/ont/value> ""SITE-001"" .
";

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
    "xsd": "http://www.w3.org/2001/XMLSchema#",
    "rec": "https://w3id.org/rec/",
    "brick": "https://brickschema.org/schema/Brick#"
  },
  "@graph": [
    {
      "@id": "http://example.org/#site1",
      "@type": "sbco:Site",
      "sbco:name": "Site 1",
      "sbco:id": "SITE_1",
      "sbco:identifiers": { "@id": "_:siteId1" },
      "sbco:documentation": { "@id": "http://example.org/#site-doc1" },
      "rec:customProperties": { "@id": "_:siteProps" },
      "rec:customTags": { "@id": "_:siteTag" },
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
      "brick:feeds": { "@id": "http://example.org/#eq-feed" },
      "sbco:isFedBy": { "@id": "http://example.org/#eq-fedby" },
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
      "@id": "http://example.org/#site-doc1",
      "@type": "sbco:Document",
      "sbco:name": "Site Plan",
      "sbco:url": "https://example.org/site-plan.pdf",
      "sbco:format": "application/pdf",
      "sbco:version": "1.0",
      "sbco:language": "en",
      "sbco:size": 1024,
      "sbco:checksum": "abc123",
      "rec:name": "Site Plan",
      "rec:url": "https://example.org/site-plan.pdf",
      "rec:format": "application/pdf",
      "rec:version": "1.0",
      "rec:language": "en",
      "rec:size": 1024,
      "rec:checksum": "abc123",
      "rec:identifiers": { "@id": "_:siteDocId" }
    },
    {
      "@id": "_:siteDocId",
      "@type": "sbco:KeyStringMapEntry",
      "sbco:key": "doc_code",
      "sbco:value": "DOC-001"
    },
    {
      "@id": "_:siteProps",
      "@type": "sbco:KeyMapOfStringMapEntry",
      "sbco:key": "site_notes",
      "sbco:entries": { "@id": "_:sitePropsEntry" }
    },
    {
      "@id": "_:sitePropsEntry",
      "@type": "sbco:KeyStringMapEntry",
      "sbco:key": "summary",
      "sbco:value": "Main campus"
    },
    {
      "@id": "_:siteTag",
      "@type": "sbco:KeyBoolMapEntry",
      "sbco:key": "primary",
      "sbco:flag": true
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

    [Fact]
    public async Task AnalyzeRdfContent_UsesSchemaIdsAndBrickPointLinks()
    {
        var ttl = @"@prefix sbco: <https://www.sbco.or.jp/ont/> .
@prefix rec: <https://w3id.org/rec/> .
@prefix brick: <https://brickschema.org/schema/Brick#> .

<urn:room-1> a sbco:Room ;
  sbco:name ""Room 1"" ;
  sbco:id ""room-1"" ;
  sbco:isLocationOf <urn:eq-1> .

<urn:eq-1> a sbco:EquipmentExt ;
  sbco:name ""Equip 1"" ;
  sbco:id ""equip-1"" ;
  sbco:deviceType ""HVAC"" ;
  sbco:installationArea ""Floor1"" ;
  sbco:targetArea ""ZoneA"" ;
  sbco:panel ""Panel-1"" .

<urn:p-1> a brick:Point ;
  sbco:name ""Point 1"" ;
  sbco:id ""point-1"" ;
  sbco:pointType ""Temperature"" ;
  sbco:pointSpecification ""Measurement"" ;
  sbco:unit ""celsius"" ;
  brick:isPointOf <urn:eq-1> .
";

        var svc = CreateService();
        var model = await svc.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "schema-id-test");

        model.Areas.Should().ContainSingle();
        model.Equipment.Should().ContainSingle();
        model.Points.Should().ContainSingle();

        var area = model.Areas[0];
        area.Name.Should().Be("Room 1");

        var equipment = model.Equipment[0];
        equipment.Name.Should().Be("Equip 1");
        equipment.DeviceId.Should().Be("equip-1");
        equipment.DeviceType.Should().Be("HVAC");
        equipment.InstallationArea.Should().Be("Floor1");
        equipment.TargetArea.Should().Be("ZoneA");
        equipment.Panel.Should().Be("Panel-1");

        var point = model.Points[0];
        point.PointId.Should().Be("point-1");
        point.EquipmentUri.Should().Be("urn:eq-1");
        equipment.Points.Should().Contain(point);
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
        site.Documentation.Should().ContainSingle(d => d.Name == "Site Plan" && d.Url == "https://example.org/site-plan.pdf");
        site.CustomProperties.Should().ContainKey("site_notes");
        var siteNotesJson = site.CustomProperties["site_notes"];
        var siteNotes = JsonSerializer.Deserialize<Dictionary<string, string>>(siteNotesJson);
        siteNotes.Should().NotBeNull();
        siteNotes!["summary"].Should().Be("Main campus");
        site.CustomTags.Should().ContainKey("primary");
        site.CustomTags["primary"].Should().BeTrue();
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
        equipment.Feeds.Should().ContainSingle().Which.Should().Be("http://example.org/#eq-feed");
        equipment.IsFedBy.Should().ContainSingle().Which.Should().Be("http://example.org/#eq-fedby");
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
