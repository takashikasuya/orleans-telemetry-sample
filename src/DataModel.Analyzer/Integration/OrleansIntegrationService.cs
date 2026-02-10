using System.Collections.Generic;
using System.Linq;
using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;

namespace DataModel.Analyzer.Integration;

/// <summary>
/// Orleans統合用サービス
/// </summary>
public class OrleansIntegrationService
{
    private readonly DataModelAnalyzer _analyzer;
    private readonly ILogger<OrleansIntegrationService> _logger;

    public OrleansIntegrationService(
        DataModelAnalyzer analyzer,
        ILogger<OrleansIntegrationService> logger)
    {
        _analyzer = analyzer;
        _logger = logger;
    }

    /// <summary>
    /// RDFファイルからOrleansで使用可能なデバイス情報を抽出
    /// </summary>
    /// <param name="rdfFilePath">RDFファイルのパス</param>
    /// <returns>デバイス情報とテレメトリポイントの辞書</returns>
    public async Task<OrleansDeviceData> ExtractDeviceDataAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルからOrleansデバイスデータを抽出開始: {FilePath}", rdfFilePath);

        var model = await _analyzer.AnalyzeFromFileAsync(rdfFilePath);
        var deviceData = new OrleansDeviceData();

        foreach (var equipment in model.Equipment)
        {
            var deviceInfo = new OrleansDeviceInfo
            {
                DeviceId = equipment.DeviceId,
                GatewayId = equipment.GatewayId,
                DeviceName = equipment.Name,
                DeviceType = equipment.DeviceType,
                LocationPath = BuildLocationPath(model, equipment)
            };

            // テレメトリポイントを変換
            foreach (var point in equipment.Points)
            {
                var telemetryPoint = new OrleansTelemetryPoint
                {
                    PointId = point.PointId,
                    PointName = point.Name,
                    PointType = MapPointType(point.PointType),
                    Unit = point.Unit ?? string.Empty,
                    IsWritable = point.Writable,
                    MinValue = point.MinPresValue,
                    MaxValue = point.MaxPresValue,
                    SamplingInterval = point.Interval,
                    ValidationRules = CreateValidationRules(point)
                };

                deviceInfo.TelemetryPoints.Add(telemetryPoint);
            }

            deviceData.Devices.Add(deviceInfo);
        }

        _logger.LogInformation("Orleansデバイスデータ抽出完了。デバイス数: {DeviceCount}", deviceData.Devices.Count);
        return deviceData;
    }

    /// <summary>
    /// Orleans Grainキーを生成
    /// </summary>
    /// <param name="deviceId">デバイスID</param>
    /// <param name="gatewayId">ゲートウェイID</param>
    /// <returns>Grainキー</returns>
    public string GenerateDeviceGrainKey(string deviceId, string gatewayId)
    {
        return $"{gatewayId}:{deviceId}";
    }

    /// <summary>
    /// デバイスの初期化データを生成
    /// </summary>
    /// <param name="device">デバイス情報</param>
    /// <returns>初期化データ</returns>
    public DeviceInitializationData CreateInitializationData(OrleansDeviceInfo device)
    {
        return new DeviceInitializationData
        {
            DeviceId = device.DeviceId,
            GatewayId = device.GatewayId,
            DeviceName = device.DeviceName,
            DeviceType = device.DeviceType,
            LocationPath = device.LocationPath,
            TelemetryConfiguration = device.TelemetryPoints.ToDictionary(
                p => p.PointId,
                p => new TelemetryConfiguration
                {
                    PointName = p.PointName,
                    DataType = p.PointType,
                    Unit = p.Unit,
                    SamplingInterval = TimeSpan.FromSeconds(p.SamplingInterval ?? 60),
                    ValidationRules = p.ValidationRules
                })
        };
    }

    /// <summary>
    /// RDFファイルからグラフシードデータを抽出
    /// </summary>
    public async Task<GraphSeedData> ExtractGraphSeedDataAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルからグラフシードを抽出開始: {FilePath}", rdfFilePath);

        var model = await _analyzer.AnalyzeFromFileAsync(rdfFilePath);
        return CreateGraphSeedData(model);
    }

    /// <summary>
    /// RDFコンテンツからグラフシードデータを抽出
    /// </summary>
    public async Task<GraphSeedData> ExtractGraphSeedDataFromContentAsync(string content, string sourceName)
    {
        _logger.LogInformation("RDFコンテンツからグラフシードを抽出開始: {SourceName}", sourceName);

        var model = await _analyzer.AnalyzeFromContentAsync(content, sourceName);
        return CreateGraphSeedData(model);
    }

    /// <summary>
    /// BuildingDataModelからグラフシードデータを生成
    /// </summary>
    public GraphSeedData CreateGraphSeedData(BuildingDataModel model)
    {
        var seed = new GraphSeedData();
        var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var site in model.Sites)
        {
            seed.Nodes.Add(CreateNodeDefinition(site, GraphNodeType.Site));
        }

        foreach (var building in model.Buildings)
        {
            seed.Nodes.Add(CreateNodeDefinition(building, GraphNodeType.Building, attrs =>
            {
                if (!string.IsNullOrWhiteSpace(building.SiteUri))
                {
                    attrs["SiteUri"] = building.SiteUri;
                }
            }));

            if (!string.IsNullOrWhiteSpace(building.SiteUri))
            {
                var buildingId = ResolveNodeId(building, "building", building.Uri);
                AddEdge(seed, edgeKeys, building.SiteUri, "hasBuilding", buildingId);
                AddEdge(seed, edgeKeys, building.SiteUri, "hasPart", buildingId);
                AddEdge(seed, edgeKeys, buildingId, "isPartOf", building.SiteUri);
            }
        }

        foreach (var level in model.Levels)
        {
            seed.Nodes.Add(CreateNodeDefinition(level, GraphNodeType.Level, attrs =>
            {
                if (!string.IsNullOrWhiteSpace(level.BuildingUri))
                {
                    attrs["BuildingUri"] = level.BuildingUri;
                }
                if (level.LevelNumber.HasValue)
                {
                    attrs["LevelNumber"] = level.LevelNumber.Value.ToString();
                }
            }));

            if (!string.IsNullOrWhiteSpace(level.BuildingUri))
            {
                var levelId = ResolveNodeId(level, "level", level.Uri);
                AddEdge(seed, edgeKeys, level.BuildingUri, "hasLevel", levelId);
                AddEdge(seed, edgeKeys, level.BuildingUri, "hasPart", levelId);
                AddEdge(seed, edgeKeys, levelId, "isPartOf", level.BuildingUri);
            }
        }

        foreach (var area in model.Areas)
        {
            seed.Nodes.Add(CreateNodeDefinition(area, GraphNodeType.Area, attrs =>
            {
                if (!string.IsNullOrWhiteSpace(area.LevelUri))
                {
                    attrs["LevelUri"] = area.LevelUri;
                }
            }));

            if (!string.IsNullOrWhiteSpace(area.LevelUri))
            {
                var areaId = ResolveNodeId(area, "area", area.Uri);
                AddEdge(seed, edgeKeys, area.LevelUri, "hasArea", areaId);
                AddEdge(seed, edgeKeys, area.LevelUri, "hasPart", areaId);
                AddEdge(seed, edgeKeys, areaId, "isPartOf", area.LevelUri);
            }
        }

        foreach (var equipment in model.Equipment)
        {
            var equipmentId = ResolveEquipmentId(equipment);
            seed.Nodes.Add(CreateNodeDefinition(equipment, GraphNodeType.Equipment, attrs =>
            {
                var deviceId = !string.IsNullOrWhiteSpace(equipment.DeviceId) ? equipment.DeviceId : equipment.SchemaId;
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    attrs["DeviceId"] = deviceId;
                }
                attrs["GatewayId"] = equipment.GatewayId;
                attrs["DeviceType"] = equipment.DeviceType;
                if (!string.IsNullOrWhiteSpace(equipment.AreaUri))
                {
                    attrs["AreaUri"] = equipment.AreaUri;
                }
                if (!string.IsNullOrWhiteSpace(equipment.InstallationArea))
                {
                    attrs["InstallationArea"] = equipment.InstallationArea;
                }
                if (!string.IsNullOrWhiteSpace(equipment.TargetArea))
                {
                    attrs["TargetArea"] = equipment.TargetArea;
                }
                if (!string.IsNullOrWhiteSpace(equipment.Panel))
                {
                    attrs["Panel"] = equipment.Panel;
                }
                if (!string.IsNullOrWhiteSpace(equipment.Supplier))
                {
                    attrs["Supplier"] = equipment.Supplier;
                }
                if (!string.IsNullOrWhiteSpace(equipment.Owner))
                {
                    attrs["Owner"] = equipment.Owner;
                }
            }, equipmentId));

            if (!string.IsNullOrWhiteSpace(equipment.AreaUri))
            {
                AddEdge(seed, edgeKeys, equipment.AreaUri, "hasEquipment", equipmentId);
                AddEdge(seed, edgeKeys, equipmentId, "locatedIn", equipment.AreaUri);
                AddEdge(seed, edgeKeys, equipment.AreaUri, "isLocationOf", equipmentId);
            }

            foreach (var fed in equipment.Feeds)
            {
                if (!string.IsNullOrWhiteSpace(fed))
                {
                    AddEdge(seed, edgeKeys, equipmentId, "feeds", fed);
                }
            }

            foreach (var fedBy in equipment.IsFedBy)
            {
                if (!string.IsNullOrWhiteSpace(fedBy))
                {
                    AddEdge(seed, edgeKeys, equipmentId, "isFedBy", fedBy);
                }
            }
        }

        foreach (var point in model.Points)
        {
            var pointId = ResolvePointId(point);
            seed.Nodes.Add(CreateNodeDefinition(point, GraphNodeType.Point, attrs =>
            {
                var logicalPointId = !string.IsNullOrWhiteSpace(point.PointId) ? point.PointId : point.SchemaId;
                if (!string.IsNullOrWhiteSpace(logicalPointId))
                {
                    attrs["PointId"] = logicalPointId;
                }
                attrs["PointType"] = point.PointType;
                attrs["PointSpecification"] = point.PointSpecification;
                attrs["Writable"] = point.Writable.ToString();
                AddPointBindingAttributes(model, point, attrs);
                if (!string.IsNullOrWhiteSpace(point.Unit))
                {
                    attrs["Unit"] = point.Unit;
                }
                if (point.Interval.HasValue)
                {
                    attrs["Interval"] = point.Interval.Value.ToString();
                }
                if (point.MinPresValue.HasValue)
                {
                    attrs["MinPresValue"] = point.MinPresValue.Value.ToString();
                }
                if (point.MaxPresValue.HasValue)
                {
                    attrs["MaxPresValue"] = point.MaxPresValue.Value.ToString();
                }
            }, pointId));

            if (!string.IsNullOrWhiteSpace(point.EquipmentUri))
            {
                AddEdge(seed, edgeKeys, point.EquipmentUri, "hasPoint", pointId);
            }
        }

        return seed;
    }

    private static void AddEdge(GraphSeedData seed, HashSet<string> edgeKeys, string sourceNodeId, string predicate, string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeId) || string.IsNullOrWhiteSpace(targetNodeId))
        {
            return;
        }

        var key = $"{sourceNodeId}|{predicate}|{targetNodeId}";
        if (edgeKeys.Add(key))
        {
            seed.Edges.Add(new GraphSeedEdge
            {
                SourceNodeId = sourceNodeId,
                Predicate = predicate,
                TargetNodeId = targetNodeId
            });
        }
    }

    private void AddPointBindingAttributes(BuildingDataModel model, Point point, Dictionary<string, string> attributes)
    {
        var equipment = ResolveEquipmentForPoint(model, point);
        if (equipment is null)
        {
            return;
        }

        var deviceId = !string.IsNullOrWhiteSpace(equipment.DeviceId) ? equipment.DeviceId : equipment.SchemaId;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            attributes["DeviceId"] = deviceId;
        }

        var area = ResolveAreaForEquipment(model, equipment);
        if (area is not null && !string.IsNullOrWhiteSpace(area.Name))
        {
            attributes["SpaceId"] = area.Name;
        }

        var building = area is null ? null : ResolveBuildingForArea(model, area);
        if (building is not null && !string.IsNullOrWhiteSpace(building.Name))
        {
            attributes["BuildingName"] = building.Name;
        }
    }

    private static Equipment? ResolveEquipmentForPoint(BuildingDataModel model, Point point)
    {
        var equipment = model.Equipment.FirstOrDefault(e => e.Points.Contains(point));
        if (equipment is not null)
        {
            return equipment;
        }

        if (!string.IsNullOrWhiteSpace(point.EquipmentUri))
        {
            return model.Equipment.FirstOrDefault(e => e.Uri == point.EquipmentUri);
        }

        return null;
    }

    private static Area? ResolveAreaForEquipment(BuildingDataModel model, Equipment equipment)
    {
        var area = model.Areas.FirstOrDefault(a => a.Equipment.Contains(equipment));
        if (area is not null)
        {
            return area;
        }

        if (!string.IsNullOrWhiteSpace(equipment.AreaUri))
        {
            return model.Areas.FirstOrDefault(a => a.Uri == equipment.AreaUri);
        }

        return null;
    }

    private static Building? ResolveBuildingForArea(BuildingDataModel model, Area area)
    {
        var level = model.Levels.FirstOrDefault(l => l.Areas.Contains(area));
        if (level is null && !string.IsNullOrWhiteSpace(area.LevelUri))
        {
            level = model.Levels.FirstOrDefault(l => l.Uri == area.LevelUri);
        }

        if (level is null)
        {
            return null;
        }

        var building = model.Buildings.FirstOrDefault(b => b.Levels.Contains(level));
        if (building is null && !string.IsNullOrWhiteSpace(level.BuildingUri))
        {
            building = model.Buildings.FirstOrDefault(b => b.Uri == level.BuildingUri);
        }

        return building;
    }

    private GraphNodeDefinition CreateNodeDefinition(RdfResource resource, GraphNodeType nodeType, Action<Dictionary<string, string>>? extras = null, string? forcedId = null)
    {
        var nodeId = !string.IsNullOrWhiteSpace(forcedId) ? forcedId : ResolveNodeId(resource, nodeType.ToString().ToLowerInvariant(), resource.Uri);
        var attributes = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(resource.Uri))
        {
            attributes["Uri"] = resource.Uri;
        }
        if (!string.IsNullOrWhiteSpace(resource.SchemaId))
        {
            attributes["SchemaId"] = resource.SchemaId;
        }

        foreach (var kv in resource.Identifiers)
        {
            attributes[$"id:{kv.Key}"] = kv.Value;
        }

        foreach (var kv in resource.CustomProperties)
        {
            if (kv.Value is null)
            {
                continue;
            }
            attributes[$"prop:{kv.Key}"] = FormatAttributeValue(kv.Value);
        }

        foreach (var kv in resource.CustomTags)
        {
            if (!kv.Value || string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            attributes[$"tag:{kv.Key.Trim()}"] = bool.TrueString;
        }

        extras?.Invoke(attributes);

        return new GraphNodeDefinition
        {
            NodeId = nodeId,
            NodeType = nodeType,
            DisplayName = resource.Name,
            Attributes = attributes
        };
    }

    private static string FormatAttributeValue(string? value) => value ?? string.Empty;

    private string ResolveNodeId(RdfResource resource, string prefix, string fallbackId)
    {
        if (!string.IsNullOrWhiteSpace(resource.Uri))
        {
            return resource.Uri;
        }

        if (!string.IsNullOrWhiteSpace(fallbackId))
        {
            return $"{prefix}:{fallbackId}";
        }

        return $"{prefix}:{Guid.NewGuid()}";
    }

    private static string ResolveEquipmentId(Equipment equipment)
    {
        if (!string.IsNullOrWhiteSpace(equipment.Uri))
        {
            return equipment.Uri;
        }

        if (!string.IsNullOrWhiteSpace(equipment.DeviceId))
        {
            if (!string.IsNullOrWhiteSpace(equipment.GatewayId))
            {
                return $"device:{equipment.GatewayId}:{equipment.DeviceId}";
            }

            return $"device:{equipment.DeviceId}";
        }

        if (!string.IsNullOrWhiteSpace(equipment.SchemaId))
        {
            return $"device:{equipment.SchemaId}";
        }

        return $"equipment:{Guid.NewGuid()}";
    }

    private static string ResolvePointId(Point point)
    {
        if (!string.IsNullOrWhiteSpace(point.Uri))
        {
            return point.Uri;
        }

        if (!string.IsNullOrWhiteSpace(point.PointId))
        {
            return $"point:{point.PointId}";
        }

        if (!string.IsNullOrWhiteSpace(point.SchemaId))
        {
            return $"point:{point.SchemaId}";
        }

        return $"point:{Guid.NewGuid()}";
    }

    private string BuildLocationPath(BuildingDataModel model, Equipment equipment)
    {
        var parts = new List<string>();

        var area = model.Areas.FirstOrDefault(a => a.Equipment.Contains(equipment));
        if (area != null)
        {
            parts.Add(area.Name);

            var level = model.Levels.FirstOrDefault(l => l.Areas.Contains(area));
            if (level != null)
            {
                parts.Insert(0, level.Name);

                var building = model.Buildings.FirstOrDefault(b => b.Levels.Contains(level));
                if (building != null)
                {
                    parts.Insert(0, building.Name);

                    var site = model.Sites.FirstOrDefault(s => s.Buildings.Contains(building));
                    if (site != null)
                    {
                        parts.Insert(0, site.Name);
                    }
                }
            }
        }

        return string.Join("/", parts);
    }

    private string MapPointType(string pointType)
    {
        return pointType.ToLowerInvariant() switch
        {
            "temperature" => "Temperature",
            "humidity" => "Humidity",
            "co2" => "CO2",
            "illuminance" => "Illuminance",
            "electricity" => "Power",
            "peoplecount" => "Occupancy",
            _ => "Unknown"
        };
    }

    private Dictionary<string, object> CreateValidationRules(Point point)
    {
        var rules = new Dictionary<string, object>();

        if (point.MinPresValue.HasValue)
            rules["MinValue"] = point.MinPresValue.Value;

        if (point.MaxPresValue.HasValue)
            rules["MaxValue"] = point.MaxPresValue.Value;

        if (point.Scale.HasValue)
            rules["Scale"] = point.Scale.Value;

        return rules;
    }
}

/// <summary>
/// Orleans用のデバイスデータコンテナ
/// </summary>
public class OrleansDeviceData
{
    public List<OrleansDeviceInfo> Devices { get; set; } = new();
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Orleans用のデバイス情報
/// </summary>
public class OrleansDeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string LocationPath { get; set; } = string.Empty;
    public List<OrleansTelemetryPoint> TelemetryPoints { get; set; } = new();
}

/// <summary>
/// Orleans用のテレメトリポイント情報
/// </summary>
public class OrleansTelemetryPoint
{
    public string PointId { get; set; } = string.Empty;
    public string PointName { get; set; } = string.Empty;
    public string PointType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool IsWritable { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int? SamplingInterval { get; set; }
    public Dictionary<string, object> ValidationRules { get; set; } = new();
}

/// <summary>
/// デバイス初期化データ
/// </summary>
public class DeviceInitializationData
{
    public string DeviceId { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string LocationPath { get; set; } = string.Empty;
    public Dictionary<string, TelemetryConfiguration> TelemetryConfiguration { get; set; } = new();
}

/// <summary>
/// テレメトリ設定
/// </summary>
public class TelemetryConfiguration
{
    public string PointName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromMinutes(1);
    public Dictionary<string, object> ValidationRules { get; set; } = new();
}
