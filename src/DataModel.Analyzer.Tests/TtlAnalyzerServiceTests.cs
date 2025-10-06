using System.Threading.Tasks;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class TtlAnalyzerServiceTests
{
  [Fact]
  public async Task AnalyzeTtlContent_BuildsHierarchy()
  {
    var ttl = @"@prefix rec: <https://w3id.org/rec#> .
@prefix gutp: <https://www.gutp.jp/bim-wg#> .
@prefix dct: <http://purl.org/dc/terms/> .

<http://example.org/#site1> a rec:Site ;
  rec:name ""Site 1"" ;
  rec:hasPart <http://example.org/#b1> .

<http://example.org/#b1> a rec:Building ;
  rec:name ""Bldg 1"" ;
  rec:hasPart <http://example.org/#l1> .

<http://example.org/#l1> a rec:Level ;
  rec:name ""1F"" ;
  gutp:levelNumber ""1"" ;
  rec:hasPart <http://example.org/#a1> .

<http://example.org/#a1> a rec:Area ;
  rec:name ""Area 1"" ;
  rec:isLocationOf <http://example.org/#eq1> .

<http://example.org/#eq1> a gutp:GUTPEquipment ;
  rec:name ""Device 1"" ;
  gutp:gateway_id ""gw-1"" ;
  gutp:device_id ""dev-1"" ;
  gutp:device_type ""sensor"" ;
  rec:hasPoint <http://example.org/#p1> .

<http://example.org/#p1> a gutp:GUTPPoint ;
  rec:name ""Point 1"" ;
  gutp:point_id ""pt-1"" ;
  gutp:point_type ""Temperature"" ;
  gutp:point_specification ""Measurement"" ;
  gutp:writable ""false"" ;
  gutp:local_id ""loc-1"" ;
  rec:isPointOf <http://example.org/#eq1> .";

    var logger = NullLogger<TtlAnalyzerService>.Instance;
    var svc = new TtlAnalyzerService(logger);

    var model = await svc.AnalyzeTtlContentAsync(ttl, "test");

    model.Sites.Should().HaveCount(1);
    model.Buildings.Should().HaveCount(1);
    model.Levels.Should().HaveCount(1);
    model.Areas.Should().HaveCount(1);
    model.Equipment.Should().HaveCount(1);
    model.Points.Should().HaveCount(1);

    var site = model.Sites[0];
    site.Buildings.Should().ContainSingle();
    var bldg = site.Buildings[0];
    bldg.Levels.Should().ContainSingle();
    var lvl = bldg.Levels[0];
    lvl.Areas.Should().ContainSingle();
    var area = lvl.Areas[0];
    area.Equipment.Should().ContainSingle();
    var eq = area.Equipment[0];
    eq.Points.Should().ContainSingle();

    // メタデータ / リレーション検証追加
    site.Name.Should().Be("Site 1");
    bldg.Name.Should().Be("Bldg 1");
    lvl.Name.Should().Be("1F");
    area.Name.Should().Be("Area 1");
    eq.Name.Should().Be("Device 1");
    var point = eq.Points[0];
    point.Name.Should().Be("Point 1");

    // Equipment メタデータ (プロパティ名は実装に合わせて調整)
    // 例: GatewayId / DeviceId / DeviceType が異なる場合は修正
    eq.GatewayId.Should().Be("gw-1");
    eq.DeviceId.Should().Be("dev-1");
    eq.DeviceType.Should().Be("sensor");

    // Point メタデータ
    point.PointId.Should().Be("pt-1");
    point.PointType.Should().Be("Temperature");
    point.PointSpecification.Should().Be("Measurement");
    point.Writable.Should().BeFalse();
    point.LocalId.Should().Be("loc-1");



    // 親子コレクション整合性
    site.Buildings.Should().Contain(b => b == bldg);
    bldg.Levels.Should().Contain(l => l == lvl);
    lvl.Areas.Should().Contain(a => a == area);
    area.Equipment.Should().Contain(e => e == eq);
    eq.Points.Should().Contain(p => p == point);
  }
}
