using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
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