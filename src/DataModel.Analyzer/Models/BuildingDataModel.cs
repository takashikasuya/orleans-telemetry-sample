using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace DataModel.Analyzer.Models;

/// <summary>
/// RDFリソースの基底クラス
/// </summary>
public abstract class RdfResource
{
    public string Uri { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Identifiers { get; set; } = new();
    public Dictionary<string, object> CustomProperties { get; set; } = new();
    public Dictionary<string, bool> CustomTags { get; set; } = new();
}

/// <summary>
/// 幾何情報
/// </summary>
public class Geometry
{
    public string? CoordinateSystem { get; set; }
}

/// <summary>
/// ジオリファレンス/ジオ変換
/// </summary>
public class Georeference
{
    public double? HeightScaleFactor { get; set; }
    public double? OriginX { get; set; }
    public double? OriginY { get; set; }
    public double? WidthScaleFactor { get; set; }
    public double? XRotationalScaleFactor { get; set; }
    public double? YRotationalScaleFactor { get; set; }
}

public class Geotransformation : Georeference { }

public class ArchitectureArea
{
    public double? GrossArea { get; set; }
    public double? NetArea { get; set; }
    public double? RentableArea { get; set; }
}

public class ArchitectureCapacity
{
    public int? MaxOccupancy { get; set; }
    public int? SeatingCapacity { get; set; }
}

public class Document : RdfResource
{
    public string? DocumentTopic { get; set; }
    public string? Url { get; set; }
}

public class PostalAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string? Region { get; set; }
}

/// <summary>
/// エージェント情報
/// </summary>
public class Agent
{
    public Dictionary<string, Dictionary<string, string>> CustomProperties { get; set; } = new();
    public Dictionary<string, bool> CustomTags { get; set; } = new();
    public Dictionary<string, string> Identifiers { get; set; } = new();
    public string? Name { get; set; }
    public List<Organization> MemberOf { get; set; } = new();
}

public class Organization : Agent
{
    public List<Agent> HasMember { get; set; } = new();
    public List<Organization> HasPart { get; set; } = new();
    public Organization? IsPartOf { get; set; }
    public string? Logo { get; set; }
}

public class Person : Agent
{
    public string? FamilyName { get; set; }
    public string? Gender { get; set; }
    public string? GivenName { get; set; }
    public string? Image { get; set; }
}

/// <summary>
/// サイト情報を表すクラス
/// </summary>
public class Site : RdfResource
{
    public List<Building> Buildings { get; set; } = new();
    public Geometry? Geometry { get; set; }
    public Georeference? Georeference { get; set; }
    public ArchitectureArea? Area { get; set; }
    public ArchitectureCapacity? Capacity { get; set; }
    public List<PostalAddress>? Address { get; set; }
    public List<Document>? Documentation { get; set; }
}

/// <summary>
/// 建物情報を表すクラス
/// </summary>
public class Building : RdfResource
{
    public string SiteUri { get; set; } = string.Empty;
    public List<Level> Levels { get; set; } = new();
    public Geometry? Geometry { get; set; }
    public Georeference? Georeference { get; set; }
    public ArchitectureArea? Area { get; set; }
    public ArchitectureCapacity? Capacity { get; set; }
    public List<PostalAddress>? Address { get; set; }
    public List<Document>? Documentation { get; set; }
}

/// <summary>
/// フロア/レベル情報を表すクラス
/// </summary>
public class Level : RdfResource
{
    public string BuildingUri { get; set; } = string.Empty;
    public int? LevelNumber { get; set; }
    public List<Area> Areas { get; set; } = new();
    public Geometry? Geometry { get; set; }
    public Georeference? Georeference { get; set; }
    public List<Document>? Documentation { get; set; }

    /// <summary>
    /// 名前から階数を推定（B1F など地下は負、12F/12階/１２階 などに対応）。
    /// 既に LevelNumber が設定済みの場合はそちらを優先。
    /// </summary>
    public int? EffectiveLevelNumber => LevelNumber ?? ExtractLevelNumberFromName(Name);

    public static int? ExtractLevelNumberFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // 全角数字を半角に正規化
        var normalized = name
            .Replace('０', '0').Replace('１', '1').Replace('２', '2')
            .Replace('３', '3').Replace('４', '4').Replace('５', '5')
            .Replace('６', '6').Replace('７', '7').Replace('８', '8')
            .Replace('９', '9');

        // 地下 B1F, B2F など
        var basement = Regex.Match(normalized, "B(\\d+)F", RegexOptions.IgnoreCase);
        if (basement.Success && int.TryParse(basement.Groups[1].Value, out var b))
        {
            return -b;
        }

        // 1F, 1FL, 1階 など
        var floor = Regex.Match(normalized, "(\\d+)\\s*(?:F|FL|階)", RegexOptions.IgnoreCase);
        if (floor.Success && int.TryParse(floor.Groups[1].Value, out var f))
        {
            return f;
        }

        // 数字のみ
        var num = Regex.Match(normalized, "(\\d+)");
        if (num.Success && int.TryParse(num.Groups[1].Value, out var n))
        {
            return n;
        }

        return null;
    }
}

/// <summary>
/// エリア情報を表すクラス
/// </summary>
public class Area : RdfResource
{
    public string LevelUri { get; set; } = string.Empty;
    public List<Equipment> Equipment { get; set; } = new();
    public Geometry? Geometry { get; set; }
    public Georeference? Georeference { get; set; }
    public List<Document>? Documentation { get; set; }
}

/// <summary>
/// アセット基底
/// </summary>
public class Asset : RdfResource
{
    public string? AssetTag { get; set; }
    public DateTime? CommissioningDate { get; set; }
    public string? InitialCost { get; set; }
    public DateTime? InstallationDate { get; set; }
    public string? IPAddress { get; set; }
    public string? MACAddress { get; set; }
    public string? MaintenanceInterval { get; set; }
    public string? ModelNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime? TurnoverDate { get; set; }
    public string? Weight { get; set; }
    public List<Agent> CommissionedBy { get; set; } = new();
    public List<Document> Documentation { get; set; } = new();
    public Geometry? Geometry { get; set; }
    public List<Asset> HasPart { get; set; } = new();
    public List<Point> HasPoint { get; set; } = new();
    public List<Agent> InstalledBy { get; set; } = new();
    public List<Asset> IsPartOf { get; set; } = new();
    public List<RdfResource> LocatedIn { get; set; } = new();
    public List<Agent> ManufacturedBy { get; set; } = new();
    public List<Agent> ServicedBy { get; set; } = new();
}

/// <summary>
/// 機器情報を表すクラス
/// </summary>
public class Equipment : Asset
{
    public string AreaUri { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public string? Supplier { get; set; }
    public string? Owner { get; set; }
    public List<Point> Points { get; set; } = new();
    public string? OperationalStageCount { get; set; }
    public List<string> Feeds { get; set; } = new();
    public List<string> IsFedBy { get; set; } = new();
}

/// <summary>
/// 物質（Substance）と量（Quantity）の列挙（値は TTL からは文字列として入る想定）
/// </summary>
public enum SubstanceEnum
{
    ACElec, Air, BlowdownWater, ChilledWater, ColdDomesticWater, Condensate, CondenserWater, DCElec,
    Diesel, DriveElec, Ethernet, ExhaustAir, Freight, FuelOil, Gasoline, GreaseExhaustAir, HotDomesticWater,
    HotWater, IrrigationWater, Light, MakeupWater, NaturalGas, NonPotableDomesticWater, OutsideAir, People,
    Propane, RecircHotDomesticWater, Refrig, ReturnAir, SprinklerWater, Steam, StormDrainage, SupplyAir,
    TransferAir, WasteVentDrainage, Water
}

public enum QuantityEnum
{
    Temperature, Humidity, CO2, Illuminance, Pressure, Flow, Power, Energy, Current, Voltage,
    Frequency, Speed, Position, Status, Count, Other
}

/// <summary>
/// データポイント情報を表すクラス
/// </summary>
public class Point : RdfResource
{
    public string EquipmentUri { get; set; } = string.Empty;
    public string PointId { get; set; } = string.Empty;
    public string PointType { get; set; } = string.Empty;
    public string PointSpecification { get; set; } = string.Empty;
    public string LocalId { get; set; } = string.Empty;
    public bool Writable { get; set; }
    public int? Interval { get; set; }
    public string? Unit { get; set; }
    public double? MaxPresValue { get; set; }
    public double? MinPresValue { get; set; }
    public double? Scale { get; set; }
    public List<string> Labels { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? HasQuantity { get; set; } // QuantityEnum 文字列想定
    public string? HasSubstance { get; set; } // SubstanceEnum 文字列想定

    // BACnet関連のプロパティ
    public string? DeviceIdBacnet { get; set; }
    public string? InstanceNoBacnet { get; set; }
    public string? ObjectTypeBacnet { get; set; }
}

// 派生ポイント（将来の拡張用）
public class Alarm : Point { }
public class Command : Point { }
public class Parameter : Point { }
public class Sensor : Point { }
public class Setpoint : Point { }
public class State : Point { }

/// <summary>
/// 建物データモデル全体を表すルートクラス
/// </summary>
public class BuildingDataModel
{
    public List<Site> Sites { get; set; } = new();
    public List<Building> Buildings { get; set; } = new();
    public List<Level> Levels { get; set; } = new();
    public List<Area> Areas { get; set; } = new();
    public List<Equipment> Equipment { get; set; } = new();
    public List<Point> Points { get; set; } = new();
    public List<Agent> Agents { get; set; } = new();
    public List<Organization> Organizations { get; set; } = new();
    public List<Person> People { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
}