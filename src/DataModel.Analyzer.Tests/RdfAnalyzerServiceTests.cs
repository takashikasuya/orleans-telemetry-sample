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
    var ttl = @"@prefix rec: <https://w3id.org/rec#> .
@prefix gutp: <https://www.gutp.jp/bim-wg#> .
@prefix dct: <http://purl.org/dc/terms/> .

<http://example.org/#site1> a rec:Site ;
  rec:name \"Site 1\" ;
  rec:hasPart <http://example.org/#b1> .

<http://example.org/#b1> a rec:Building ;
  rec:name \"Bldg 1\" ;
  rec:hasPart <http://example.org/#l1> .

<http://example.org/#l1> a rec:Level ;
  rec:name \"1F\" ;
  gutp:levelNumber \"1\" ;
  rec:hasPart <http://example.org/#a1> .

<http://example.org/#a1> a rec:Area ;
  rec:name \"Area 1\" ;
  rec:isLocationOf <http://example.org/#eq1> .

<http://example.org/#eq1> a gutp:GUTPEquipment ;
  rec:name \"Device 1\" ;
  gutp:gateway_id \"gw-1\" ;
  gutp:device_id \"dev-1\" ;
  gutp:device_type \"sensor\" ;
  rec:hasPoint <http://example.org/#p1> .

<http://example.org/#p1> a gutp:GUTPPoint ;
  rec:name \"Point 1\" ;
  gutp:point_id \"pt-1\" ;
  gutp:point_type \"Temperature\" ;
  gutp:point_specification \"Measurement\" ;
  gutp:writable \"false\" ;
  gutp:local_id \"loc-1\" ;
  rec:isPointOf <http://example.org/#eq1> .";

    var svc = CreateService();

    var model = await svc.AnalyzeRdfContentAsync(ttl, RdfSerializationFormat.Turtle, "turtle-test");

    AssertStandardModel(model);
  }

  [Fact]
  public async Task AnalyzeRdfFile_NTriples_BuildsHierarchy()
  {
    var nt = @"<http://example.org/#site1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://w3id.org/rec#Site> .
<http://example.org/#site1> <https://w3id.org/rec#name> \"Site 1\" .
<http://example.org/#site1> <https://w3id.org/rec#hasPart> <http://example.org/#b1> .
<http://example.org/#b1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://w3id.org/rec#Building> .
<http://example.org/#b1> <https://w3id.org/rec#name> \"Bldg 1\" .
<http://example.org/#b1> <https://w3id.org/rec#hasPart> <http://example.org/#l1> .
<http://example.org/#l1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://w3id.org/rec#Level> .
<http://example.org/#l1> <https://w3id.org/rec#name> \"1F\" .
<http://example.org/#l1> <https://www.gutp.jp/bim-wg#levelNumber> \"1\" .
<http://example.org/#l1> <https://w3id.org/rec#hasPart> <http://example.org/#a1> .
<http://example.org/#a1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://w3id.org/rec#Area> .
<http://example.org/#a1> <https://w3id.org/rec#name> \"Area 1\" .
<http://example.org/#a1> <https://w3id.org/rec#isLocationOf> <http://example.org/#eq1> .
<http://example.org/#eq1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.gutp.jp/bim-wg#GUTPEquipment> .
<http://example.org/#eq1> <https://w3id.org/rec#name> \"Device 1\" .
<http://example.org/#eq1> <https://www.gutp.jp/bim-wg#gateway_id> \"gw-1\" .
<http://example.org/#eq1> <https://www.gutp.jp/bim-wg#device_id> \"dev-1\" .
<http://example.org/#eq1> <https://www.gutp.jp/bim-wg#device_type> \"sensor\" .
<http://example.org/#eq1> <https://w3id.org/rec#hasPoint> <http://example.org/#p1> .
<http://example.org/#p1> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <https://www.gutp.jp/bim-wg#GUTPPoint> .
<http://example.org/#p1> <https://w3id.org/rec#name> \"Point 1\" .
<http://example.org/#p1> <https://www.gutp.jp/bim-wg#point_id> \"pt-1\" .
<http://example.org/#p1> <https://www.gutp.jp/bim-wg#point_type> \"Temperature\" .
<http://example.org/#p1> <https://www.gutp.jp/bim-wg#point_specification> \"Measurement\" .
<http://example.org/#p1> <https://www.gutp.jp/bim-wg#writable> \"false\" .
<http://example.org/#p1> <https://www.gutp.jp/bim-wg#local_id> \"loc-1\" .
<http://example.org/#p1> <https://w3id.org/rec#isPointOf> <http://example.org/#eq1> .";

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
    var jsonLd = @"{
  \"@context\": {
    \"rec\": \"https://w3id.org/rec#\",
    \"gutp\": \"https://www.gutp.jp/bim-wg#\",
    \"dct\": \"http://purl.org/dc/terms/\"
  },
  \"@graph\": [
    {
      \"@id\": \"http://example.org/#site1\",
      \"@type\": \"rec:Site\",
      \"rec:name\": \"Site 1\",
      \"rec:hasPart\": { \"@id\": \"http://example.org/#b1\" }
    },
    {
      \"@id\": \"http://example.org/#b1\",
      \"@type\": \"rec:Building\",
      \"rec:name\": \"Bldg 1\",
      \"rec:hasPart\": { \"@id\": \"http://example.org/#l1\" },
      \"rec:isPartOf\": { \"@id\": \"http://example.org/#site1\" }
    },
    {
      \"@id\": \"http://example.org/#l1\",
      \"@type\": \"rec:Level\",
      \"rec:name\": \"1F\",
      \"gutp:levelNumber\": \"1\",
      \"rec:hasPart\": { \"@id\": \"http://example.org/#a1\" },
      \"rec:isPartOf\": { \"@id\": \"http://example.org/#b1\" }
    },
    {
      \"@id\": \"http://example.org/#a1\",
      \"@type\": \"rec:Area\",
      \"rec:name\": \"Area 1\",
      \"rec:isLocationOf\": { \"@id\": \"http://example.org/#eq1\" },
      \"rec:isPartOf\": { \"@id\": \"http://example.org/#l1\" }
    },
    {
      \"@id\": \"http://example.org/#eq1\",
      \"@type\": \"gutp:GUTPEquipment\",
      \"rec:name\": \"Device 1\",
      \"gutp:gateway_id\": \"gw-1\",
      \"gutp:device_id\": \"dev-1\",
      \"gutp:device_type\": \"sensor\",
      \"rec:hasPoint\": { \"@id\": \"http://example.org/#p1\" }
    },
    {
      \"@id\": \"http://example.org/#p1\",
      \"@type\": \"gutp:GUTPPoint\",
      \"rec:name\": \"Point 1\",
      \"gutp:point_id\": \"pt-1\",
      \"gutp:point_type\": \"Temperature\",
      \"gutp:point_specification\": \"Measurement\",
      \"gutp:writable\": \"false\",
      \"gutp:local_id\": \"loc-1\",
      \"rec:isPointOf\": { \"@id\": \"http://example.org/#eq1\" }
    }
  ]
}";

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
    site.Buildings.Should().ContainSingle();

    var building = site.Buildings[0];
    building.Name.Should().Be("Bldg 1");
    building.Levels.Should().ContainSingle();

    var level = building.Levels[0];
    level.Name.Should().Be("1F");
    level.LevelNumber.Should().Be("1");
    level.Areas.Should().ContainSingle();

    var area = level.Areas[0];
    area.Name.Should().Be("Area 1");
    area.Equipment.Should().ContainSingle();

    var equipment = area.Equipment[0];
    equipment.Name.Should().Be("Device 1");
    equipment.GatewayId.Should().Be("gw-1");
    equipment.DeviceId.Should().Be("dev-1");
    equipment.DeviceType.Should().Be("sensor");
    equipment.Points.Should().ContainSingle();

    var point = equipment.Points[0];
    point.Name.Should().Be("Point 1");
    point.PointId.Should().Be("pt-1");
    point.PointType.Should().Be("Temperature");
    point.PointSpecification.Should().Be("Measurement");
    point.Writable.Should().BeFalse();
    point.LocalId.Should().Be("loc-1");

    // 親子コレクション整合性
    site.Buildings.Should().Contain(building);
    building.Levels.Should().Contain(level);
    level.Areas.Should().Contain(area);
    area.Equipment.Should().Contain(equipment);
    equipment.Points.Should().Contain(point);
  }
}
