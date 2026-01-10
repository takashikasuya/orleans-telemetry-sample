using DataModel.Analyzer.Integration;
using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataModel.Analyzer.Tests;

public class OrleansIntegrationServiceBindingTests
{
    [Fact]
    public void CreateGraphSeedData_AddsPointBindingAttributes()
    {
        var model = BuildModel();
        var service = CreateService();

        var seed = service.CreateGraphSeedData(model);

        var pointNode = seed.Nodes.Single(node => node.NodeType == GraphNodeType.Point && node.NodeId == "point:1");
        pointNode.Attributes["PointId"].Should().Be("temp-1");
        pointNode.Attributes["DeviceId"].Should().Be("dev-1");
        pointNode.Attributes["BuildingName"].Should().Be("BuildingA");
        pointNode.Attributes["SpaceId"].Should().Be("Room101");

        var grainKey = PointGrainKey.Create(
            "tenantA",
            pointNode.Attributes["BuildingName"],
            pointNode.Attributes["SpaceId"],
            pointNode.Attributes["DeviceId"],
            pointNode.Attributes["PointId"]);
        grainKey.Should().Be("tenantA:BuildingA:Room101:dev-1:temp-1");
    }

    private static BuildingDataModel BuildModel()
    {
        var site = new Site { Name = "SiteA", Uri = "site:1" };
        var building = new Building { Name = "BuildingA", Uri = "building:1", SiteUri = site.Uri };
        var level = new Level { Name = "L1", Uri = "level:1", BuildingUri = building.Uri };
        var area = new Area { Name = "Room101", Uri = "area:1", LevelUri = level.Uri };
        var equipment = new Equipment
        {
            Name = "AHU",
            Uri = "equip:1",
            DeviceId = "dev-1",
            DeviceType = "HVAC",
            GatewayId = "gw-1",
            AreaUri = area.Uri
        };
        var point = new Point
        {
            Name = "Temperature",
            Uri = "point:1",
            PointId = "temp-1",
            PointType = "temperature",
            EquipmentUri = equipment.Uri
        };

        site.Buildings.Add(building);
        building.Levels.Add(level);
        level.Areas.Add(area);
        area.Equipment.Add(equipment);
        equipment.Points.Add(point);

        return new BuildingDataModel
        {
            Sites = { site },
            Buildings = { building },
            Levels = { level },
            Areas = { area },
            Equipment = { equipment },
            Points = { point }
        };
    }

    private static OrleansIntegrationService CreateService()
    {
        var rdfAnalyzer = new RdfAnalyzerService(
            NullLogger<RdfAnalyzerService>.Instance,
            Options.Create(new RdfAnalyzerOptions()));
        var exportService = new DataModelExportService(NullLogger<DataModelExportService>.Instance);
        var analyzer = new DataModelAnalyzer(
            rdfAnalyzer,
            exportService,
            NullLogger<DataModelAnalyzer>.Instance);
        return new OrleansIntegrationService(analyzer, NullLogger<OrleansIntegrationService>.Instance);
    }
}
