using System;
using Grains.Abstractions;

namespace AdminGateway.Models;

public sealed record GrainActivationSummary(string GrainType, int ActivationCount, string[] Silos);

public sealed record SiloSummary(
    string SiloAddress,
    string Status,
    long ClientCount,
    int ActivationCount,
    DateTime LastUpdated,
    double? CpuUsagePercentage,
    double? MemoryUsageBytes,
    double? MaximumAvailableMemoryBytes);

public sealed record StorageTenantSummary(string TenantId, long FileCount, long TotalBytes, DateTime? LatestModified);

public sealed record StorageTierSummary(
    string Tier,
    string RootPath,
    long FileCount,
    long TotalBytes,
    DateTime? LatestFile,
    IReadOnlyList<StorageTenantSummary> Tenants);

public sealed record StorageOverview(
    StorageTierSummary Stage,
    StorageTierSummary Parquet,
    StorageTierSummary Index);

public sealed record IngestSummary(string[] EnabledConnectors, string[] EnabledSinks, int BatchSize, int ChannelCapacity);

public sealed record GraphNodeDetail(string NodeId, string DisplayName, GraphNodeType NodeType);

public sealed record GraphStatisticsSummary(
    int SiteCount,
    int BuildingCount,
    int LevelCount,
    int AreaCount,
    int EquipmentCount,
    int PointCount,
    string TenantId,
    IReadOnlyDictionary<GraphNodeType, IReadOnlyList<GraphNodeDetail>> NodeSamples);

public sealed record GraphNodeHierarchy(
    IReadOnlyList<GraphNodeSnapshot> Nodes,
    string TenantId);

public sealed record GraphTreeNode(
    string NodeId,
    string DisplayName,
    GraphNodeType NodeType,
    IReadOnlyList<GraphTreeNode> Children);

public enum GrainHierarchyNodeKind
{
    Silo,
    GrainType,
    GrainId,
    Info
}

public sealed record GrainHierarchyNode(
    string NodeId,
    string Label,
    int ActivationCount,
    GrainHierarchyNodeKind Kind,
    IReadOnlyList<GrainHierarchyNode> Children);

public sealed record GraphNodeDetailView(
    GraphNodeSnapshot Snapshot,
    PointSnapshot? PointSnapshot,
    string? PointGrainKey,
    string? DeviceId = null);

public sealed record PointTrendSample(
    DateTimeOffset Timestamp,
    double? Value,
    string? RawValue);

public sealed record ControlRoutingConnectorView(
    string Connector,
    IReadOnlyList<string> GatewayIds,
    bool IsEnabled);

public sealed record ControlRoutingView(
    string ConfigPath,
    string? DefaultConnector,
    IReadOnlyList<ControlRoutingConnectorView> ConnectorMappings,
    string RawJson);
