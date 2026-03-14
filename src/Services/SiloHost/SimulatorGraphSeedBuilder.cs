using System;
using System.Text;
using Telemetry.Ingest.Simulator;

namespace SiloHost;

internal static class SimulatorGraphSeedBuilder
{
    public static string BuildTurtle(SimulatorIngestOptions options)
    {
        var deviceCount = Math.Max(1, options.DeviceCount);
        var pointsPerDevice = Math.Max(1, options.PointsPerDevice);

        var siteId = "sim-site";
        var buildingId = "sim-building";
        var levelId = "sim-level";
        var areaId = "sim-area";

        var sb = new StringBuilder();
        sb.AppendLine("@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .");
        sb.AppendLine("@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .");
        sb.AppendLine("@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .");
        sb.AppendLine("@prefix rec: <https://w3id.org/rec/> .");
        sb.AppendLine("@prefix brick: <https://brickschema.org/schema/Brick#> .");
        sb.AppendLine("@prefix sbco: <https://www.sbco.or.jp/ont/> .");
        sb.AppendLine();

        AppendSite(sb, siteId, buildingId);
        AppendBuilding(sb, buildingId, levelId);
        AppendLevel(sb, levelId, areaId);
        AppendArea(sb, areaId);

        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            var deviceId = $"{options.DeviceIdPrefix}{deviceIndex + 1}";
            var equipmentId = $"sim-equipment-{deviceId}";
            AppendEquipment(sb, equipmentId, deviceId, areaId);

            for (var pointIndex = 0; pointIndex < pointsPerDevice; pointIndex++)
            {
                var pointId = $"p{pointIndex + 1}";
                var pointNodeId = $"sim-point-{deviceId}-{pointId}";
                AppendPoint(sb, pointNodeId, pointId, equipmentId);
            }
        }

        return sb.ToString();
    }

    private static void AppendSite(StringBuilder sb, string siteId, string buildingId)
    {
        sb.AppendLine("# Simulator Site");
        sb.AppendLine($"<urn:{siteId}> a sbco:Site ;");
        sb.AppendLine($"    sbco:id \"{siteId}\" ;");
        sb.AppendLine("    sbco:name \"Simulator-Site\" ;");
        sb.AppendLine($"    rec:hasPart <urn:{buildingId}> .");
        sb.AppendLine();
    }

    private static void AppendBuilding(StringBuilder sb, string buildingId, string levelId)
    {
        sb.AppendLine("# Simulator Building");
        sb.AppendLine($"<urn:{buildingId}> a sbco:Building ;");
        sb.AppendLine($"    sbco:id \"{buildingId}\" ;");
        sb.AppendLine("    sbco:name \"Simulator-Building\" ;");
        sb.AppendLine($"    rec:hasPart <urn:{levelId}> .");
        sb.AppendLine();
    }

    private static void AppendLevel(StringBuilder sb, string levelId, string areaId)
    {
        sb.AppendLine("# Simulator Level");
        sb.AppendLine($"<urn:{levelId}> a sbco:Level ;");
        sb.AppendLine($"    sbco:id \"{levelId}\" ;");
        sb.AppendLine("    sbco:name \"Simulator-Level\" ;");
        sb.AppendLine("    rec:levelNumber 1 ;");
        sb.AppendLine($"    rec:hasPart <urn:{areaId}> .");
        sb.AppendLine();
    }

    private static void AppendArea(StringBuilder sb, string areaId)
    {
        sb.AppendLine("# Simulator Area");
        sb.AppendLine($"<urn:{areaId}> a sbco:Space ;");
        sb.AppendLine($"    sbco:id \"{areaId}\" ;");
        sb.AppendLine("    sbco:name \"Simulator-Area\" .");
        sb.AppendLine();
    }

    private static void AppendEquipment(StringBuilder sb, string equipmentId, string deviceId, string areaId)
    {
        sb.AppendLine("# Simulator Equipment");
        sb.AppendLine($"<urn:{equipmentId}> a sbco:EquipmentExt ;");
        sb.AppendLine($"    sbco:id \"{equipmentId}\" ;");
        sb.AppendLine($"    sbco:name \"Simulator-Equipment-{deviceId}\" ;");
        sb.AppendLine($"    rec:locatedIn <urn:{areaId}> ;");
        sb.AppendLine("    rec:identifiers (");
        sb.AppendLine("        [ a sbco:KeyStringMapEntry ;");
        sb.AppendLine("            sbco:key \"device_id\" ;");
        sb.AppendLine($"            sbco:value \"{deviceId}\" ]");
        sb.AppendLine("    ) .");
        sb.AppendLine();
    }

    private static void AppendPoint(StringBuilder sb, string pointNodeId, string pointId, string equipmentId)
    {
        sb.AppendLine("# Simulator Point");
        sb.AppendLine($"<urn:{pointNodeId}> a sbco:PointExt ;");
        sb.AppendLine($"    sbco:id \"{pointNodeId}\" ;");
        sb.AppendLine($"    sbco:name \"Simulator-Point-{pointId}\" ;");
        sb.AppendLine("    sbco:pointType \"Temperature\" ;");
        sb.AppendLine("    sbco:pointSpecification \"Measurement\" ;");
        sb.AppendLine("    sbco:unit \"celsius\" ;");
        sb.AppendLine("    sbco:minPresValue 0.0 ;");
        sb.AppendLine("    sbco:maxPresValue 100.0 ;");
        sb.AppendLine($"    brick:isPointOf <urn:{equipmentId}> ;");
        sb.AppendLine("    rec:identifiers (");
        sb.AppendLine("        [ a sbco:KeyStringMapEntry ;");
        sb.AppendLine("            sbco:key \"point_id\" ;");
        sb.AppendLine($"            sbco:value \"{pointId}\" ]");
        sb.AppendLine("    ) .");
        sb.AppendLine();
    }
}
