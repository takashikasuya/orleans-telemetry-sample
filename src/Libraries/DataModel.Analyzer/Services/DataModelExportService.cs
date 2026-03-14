using DataModel.Analyzer.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DataModel.Analyzer.Services;

/// <summary>
/// データモデルを他のプロジェクトで利用可能な形式にエクスポートするサービス
/// </summary>
public class DataModelExportService
{
    private readonly ILogger<DataModelExportService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DataModelExportService(ILogger<DataModelExportService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// データモデルをJSON文字列にエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>JSON文字列</returns>
    public string ExportToJson(BuildingDataModel model)
    {
        try
        {
            _logger.LogInformation("データモデルをJSONにエクスポート開始");
            var json = JsonSerializer.Serialize(model, _jsonOptions);
            _logger.LogInformation("JSONエクスポート完了。サイズ: {Size} bytes", json.Length);
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSONエクスポート中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// データモデルをJSONファイルにエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <param name="filePath">出力ファイルパス</param>
    public async Task ExportToJsonFileAsync(BuildingDataModel model, string filePath)
    {
        try
        {
            _logger.LogInformation("データモデルをJSONファイルにエクスポート開始: {FilePath}", filePath);

            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var json = ExportToJson(model);
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("JSONファイルエクスポート完了: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSONファイルエクスポート中にエラーが発生しました: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// サマリー情報をエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>サマリー情報</returns>
    public BuildingDataSummary ExportSummary(BuildingDataModel model)
    {
        _logger.LogInformation("データモデルサマリーを生成");

        return new BuildingDataSummary
        {
            Source = model.Source,
            LastUpdated = model.LastUpdated,
            SiteCount = model.Sites.Count,
            BuildingCount = model.Buildings.Count,
            LevelCount = model.Levels.Count,
            AreaCount = model.Areas.Count,
            EquipmentCount = model.Equipment.Count,
            PointCount = model.Points.Count,
            Sites = model.Sites.Select(s => new SiteSummary
            {
                Uri = s.Uri,
                Name = s.Name,
                BuildingCount = s.Buildings.Count
            }).ToList(),
            PointTypes = model.Points.GroupBy(p => p.PointType)
                .ToDictionary(g => g.Key, g => g.Count()),
            EquipmentTypes = model.Equipment.GroupBy(e => e.DeviceType)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Orleans向けのデバイスコントラクトをエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>デバイスコントラクトのリスト</returns>
    public List<DeviceContract> ExportToOrleansContracts(BuildingDataModel model)
    {
        _logger.LogInformation("Orleansデバイスコントラクトをエクスポート開始");

        var contracts = new List<DeviceContract>();

        foreach (var equipment in model.Equipment)
        {
            var deviceId = ResolveDeviceId(equipment);
            var contract = new DeviceContract
            {
                DeviceId = deviceId,
                GatewayId = equipment.GatewayId,
                DeviceName = equipment.Name,
                DeviceType = equipment.DeviceType,
                LocationPath = BuildLocationPath(model, equipment),
                Points = equipment.Points.Select(p => new PointContract
                {
                    PointId = ResolvePointId(p),
                    PointName = p.Name,
                    PointType = p.PointType,
                    PointSpecification = p.PointSpecification,
                    Unit = p.Unit,
                    Writable = p.Writable,
                    MinValue = p.MinPresValue,
                    MaxValue = p.MaxPresValue,
                    Interval = p.Interval
                }).ToList()
            };

            contracts.Add(contract);
        }

        _logger.LogInformation("Orleansデバイスコントラクトエクスポート完了。件数: {Count}", contracts.Count);
        return contracts;
    }

    private string BuildLocationPath(BuildingDataModel model, Equipment equipment)
    {
        var parts = new List<string>();

        // Area -> Level -> Building -> Site の順でパスを構築
        var area = model.Areas.FirstOrDefault(a => a.Uri == equipment.AreaUri);
        if (area != null)
        {
            parts.Add(area.Name);

            var level = model.Levels.FirstOrDefault(l => l.Uri == area.LevelUri);
            if (level != null)
            {
                parts.Insert(0, level.Name);

                var building = model.Buildings.FirstOrDefault(b => b.Uri == level.BuildingUri);
                if (building != null)
                {
                    parts.Insert(0, building.Name);

                    var site = model.Sites.FirstOrDefault(s => s.Uri == building.SiteUri);
                    if (site != null)
                    {
                        parts.Insert(0, site.Name);
                    }
                }
            }
        }

        return string.Join("/", parts);
    }

    private static string ResolveDeviceId(Equipment equipment)
    {
        if (!string.IsNullOrWhiteSpace(equipment.DeviceId))
        {
            return equipment.DeviceId;
        }

        if (!string.IsNullOrWhiteSpace(equipment.SchemaId))
        {
            return equipment.SchemaId;
        }

        return string.Empty;
    }

    private static string ResolvePointId(Point point)
    {
        if (!string.IsNullOrWhiteSpace(point.PointId))
        {
            return point.PointId;
        }

        if (!string.IsNullOrWhiteSpace(point.SchemaId))
        {
            return point.SchemaId;
        }

        return string.Empty;
    }
}

/// <summary>
/// データモデルのサマリー情報
/// </summary>
public class BuildingDataSummary
{
    public string Source { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public int SiteCount { get; set; }
    public int BuildingCount { get; set; }
    public int LevelCount { get; set; }
    public int AreaCount { get; set; }
    public int EquipmentCount { get; set; }
    public int PointCount { get; set; }
    public List<SiteSummary> Sites { get; set; } = new();
    public Dictionary<string, int> PointTypes { get; set; } = new();
    public Dictionary<string, int> EquipmentTypes { get; set; } = new();
}

/// <summary>
/// サイトのサマリー情報
/// </summary>
public class SiteSummary
{
    public string Uri { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int BuildingCount { get; set; }
}

/// <summary>
/// Orleans用のデバイスコントラクト
/// </summary>
public class DeviceContract
{
    public string DeviceId { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string LocationPath { get; set; } = string.Empty;
    public List<PointContract> Points { get; set; } = new();
}

/// <summary>
/// Orleans用のポイントコントラクト
/// </summary>
public class PointContract
{
    public string PointId { get; set; } = string.Empty;
    public string PointName { get; set; } = string.Empty;
    public string PointType { get; set; } = string.Empty;
    public string PointSpecification { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public bool Writable { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int? Interval { get; set; }
}
