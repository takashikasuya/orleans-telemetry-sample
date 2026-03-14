using System;
using Grains.Abstractions;

namespace AdminGateway.Models;

/// <summary>
/// Represents activation count summary for a grain type.
/// </summary>
/// <param name="GrainType">Grain type name.</param>
/// <param name="ActivationCount">Activation count.</param>
/// <param name="Silos">Silo addresses hosting activations.</param>
public sealed record GrainActivationSummary(string GrainType, int ActivationCount, string[] Silos);

/// <summary>
/// Represents summary information for a silo.
/// </summary>
/// <param name="SiloAddress">Silo address.</param>
/// <param name="Status">Silo status.</param>
/// <param name="ClientCount">Connected client count.</param>
/// <param name="ActivationCount">Activation count on the silo.</param>
/// <param name="LastUpdated">Last update timestamp.</param>
/// <param name="CpuUsagePercentage">CPU usage percentage when available.</param>
/// <param name="MemoryUsageBytes">Memory usage in bytes when available.</param>
/// <param name="MaximumAvailableMemoryBytes">Maximum available memory in bytes when available.</param>
public sealed record SiloSummary(
    string SiloAddress,
    string Status,
    long ClientCount,
    int ActivationCount,
    DateTime LastUpdated,
    double? CpuUsagePercentage,
    double? MemoryUsageBytes,
    double? MaximumAvailableMemoryBytes);

/// <summary>
/// Represents storage summary for a tenant.
/// </summary>
/// <param name="TenantId">Tenant identifier.</param>
/// <param name="FileCount">File count.</param>
/// <param name="TotalBytes">Total bytes.</param>
/// <param name="LatestModified">Latest modification timestamp.</param>
public sealed record StorageTenantSummary(string TenantId, long FileCount, long TotalBytes, DateTime? LatestModified);

/// <summary>
/// Represents storage summary for a storage tier.
/// </summary>
/// <param name="Tier">Tier name.</param>
/// <param name="RootPath">Root path.</param>
/// <param name="FileCount">File count.</param>
/// <param name="TotalBytes">Total bytes.</param>
/// <param name="LatestFile">Latest file timestamp.</param>
/// <param name="Tenants">Per-tenant summaries.</param>
public sealed record StorageTierSummary(
    string Tier,
    string RootPath,
    long FileCount,
    long TotalBytes,
    DateTime? LatestFile,
    IReadOnlyList<StorageTenantSummary> Tenants);

/// <summary>
/// Represents storage overview across stage, parquet, and index tiers.
/// </summary>
/// <param name="Stage">Stage tier summary.</param>
/// <param name="Parquet">Parquet tier summary.</param>
/// <param name="Index">Index tier summary.</param>
public sealed record StorageOverview(
    StorageTierSummary Stage,
    StorageTierSummary Parquet,
    StorageTierSummary Index);

/// <summary>
/// Represents telemetry ingest configuration summary.
/// </summary>
/// <param name="EnabledConnectors">Enabled connector names.</param>
/// <param name="EnabledSinks">Enabled sink names.</param>
/// <param name="BatchSize">Ingest batch size.</param>
/// <param name="ChannelCapacity">Ingest channel capacity.</param>
public sealed record IngestSummary(string[] EnabledConnectors, string[] EnabledSinks, int BatchSize, int ChannelCapacity);

/// <summary>
/// Represents lightweight graph node details.
/// </summary>
/// <param name="NodeId">Node identifier.</param>
/// <param name="DisplayName">Node display name.</param>
/// <param name="NodeType">Node type.</param>
public sealed record GraphNodeDetail(string NodeId, string DisplayName, GraphNodeType NodeType);

/// <summary>
/// Represents graph statistics and sampled nodes by type.
/// </summary>
/// <param name="SiteCount">Site node count.</param>
/// <param name="BuildingCount">Building node count.</param>
/// <param name="LevelCount">Level node count.</param>
/// <param name="AreaCount">Area node count.</param>
/// <param name="EquipmentCount">Equipment node count.</param>
/// <param name="PointCount">Point node count.</param>
/// <param name="TenantId">Tenant identifier.</param>
/// <param name="NodeSamples">Sampled nodes grouped by type.</param>
public sealed record GraphStatisticsSummary(
    int SiteCount,
    int BuildingCount,
    int LevelCount,
    int AreaCount,
    int EquipmentCount,
    int PointCount,
    string TenantId,
    IReadOnlyDictionary<GraphNodeType, IReadOnlyList<GraphNodeDetail>> NodeSamples);

/// <summary>
/// Represents node snapshots that form a tenant graph hierarchy.
/// </summary>
/// <param name="Nodes">Node snapshots.</param>
/// <param name="TenantId">Tenant identifier.</param>
public sealed record GraphNodeHierarchy(
    IReadOnlyList<GraphNodeSnapshot> Nodes,
    string TenantId);

/// <summary>
/// Represents a tree node view for graph rendering.
/// </summary>
/// <param name="NodeId">Node identifier.</param>
/// <param name="DisplayName">Node display name.</param>
/// <param name="NodeType">Node type.</param>
/// <param name="Children">Child tree nodes.</param>
public sealed record GraphTreeNode(
    string NodeId,
    string DisplayName,
    GraphNodeType NodeType,
    IReadOnlyList<GraphTreeNode> Children);

/// <summary>
/// Represents hierarchy node kinds in grain activation views.
/// </summary>
public enum GrainHierarchyNodeKind
{
    Silo,
    GrainType,
    GrainId,
    Info
}

/// <summary>
/// Represents one node in the grain hierarchy tree.
/// </summary>
/// <param name="NodeId">Hierarchy node identifier.</param>
/// <param name="Label">Display label.</param>
/// <param name="ActivationCount">Activation count for the node.</param>
/// <param name="Kind">Hierarchy node kind.</param>
/// <param name="Children">Child hierarchy nodes.</param>
public sealed record GrainHierarchyNode(
    string NodeId,
    string Label,
    int ActivationCount,
    GrainHierarchyNodeKind Kind,
    IReadOnlyList<GrainHierarchyNode> Children);

/// <summary>
/// Represents detailed graph node view with optional point snapshot data.
/// </summary>
/// <param name="Snapshot">Graph node snapshot.</param>
/// <param name="PointSnapshot">Optional point snapshot.</param>
/// <param name="PointGrainKey">Optional point grain key.</param>
/// <param name="DeviceId">Optional associated device identifier.</param>
public sealed record GraphNodeDetailView(
    GraphNodeSnapshot Snapshot,
    PointSnapshot? PointSnapshot,
    string? PointGrainKey,
    string? DeviceId = null);

/// <summary>
/// Represents one trend sample for point charting.
/// </summary>
/// <param name="Timestamp">Sample timestamp.</param>
/// <param name="Value">Numeric value when conversion succeeded.</param>
/// <param name="RawValue">Original raw value text.</param>
public sealed record PointTrendSample(
    DateTimeOffset Timestamp,
    double? Value,
    string? RawValue);


/// <summary>
/// Represents one telemetry trend line for multi-point charting.
/// </summary>
/// <param name="PointId">Point identifier.</param>
/// <param name="Label">Display label for legend.</param>
/// <param name="Samples">Samples for this point.</param>
public sealed record PointTrendSeries(
    string PointId,
    string Label,
    IReadOnlyList<PointTrendSample> Samples);

/// <summary>
/// Represents connector mapping details in control routing settings.
/// </summary>
/// <param name="Connector">Connector name.</param>
/// <param name="GatewayIds">Mapped gateway identifiers.</param>
/// <param name="IsEnabled">Whether connector is enabled.</param>
public sealed record ControlRoutingConnectorView(
    string Connector,
    IReadOnlyList<string> GatewayIds,
    bool IsEnabled);

/// <summary>
/// Represents control routing configuration view.
/// </summary>
/// <param name="ConfigPath">Configuration file path.</param>
/// <param name="DefaultConnector">Default connector name.</param>
/// <param name="ConnectorMappings">Connector mapping entries.</param>
/// <param name="RawJson">Raw JSON configuration content.</param>
public sealed record ControlRoutingView(
    string ConfigPath,
    string? DefaultConnector,
    IReadOnlyList<ControlRoutingConnectorView> ConnectorMappings,
    string RawJson);

/// <summary>
/// Represents one API request log row shown in Admin UI.
/// </summary>
/// <param name="OccurredAt">Event timestamp.</param>
/// <param name="TenantId">Tenant identifier.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Request path.</param>
/// <param name="StatusCode">Response status code.</param>
/// <param name="DurationMs">Elapsed duration in milliseconds.</param>
/// <param name="Severity">Log severity text.</param>
/// <param name="TraceId">Trace identifier when available.</param>
/// <param name="User">User identifier when available.</param>
/// <param name="RawPayload">Raw payload JSON.</param>
public sealed record ApiRequestLogEntry(
    DateTimeOffset OccurredAt,
    string TenantId,
    string Method,
    string Path,
    int StatusCode,
    double? DurationMs,
    string Severity,
    string? TraceId,
    string? User,
    string? RawPayload);

/// <summary>
/// Represents API request log query options.
/// </summary>
/// <param name="TenantId">Optional tenant filter.</param>
/// <param name="PathContains">Optional path text filter.</param>
/// <param name="StatusCode">Optional exact status code filter.</param>
/// <param name="From">Range start.</param>
/// <param name="To">Range end.</param>
/// <param name="Limit">Result row limit.</param>
public sealed record ApiRequestLogQuery(
    string? TenantId,
    string? PathContains,
    int? StatusCode,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit = 100);

/// <summary>
/// Represents API request ingest volume summary derived from log events.
/// </summary>
/// <param name="Last1MinuteCount">Count of events in the last 1 minute.</param>
/// <param name="Last5MinutesCount">Count of events in the last 5 minutes.</param>
/// <param name="CalculatedAt">Calculation timestamp (UTC).</param>
/// <param name="MayBeCapped">Whether the count may be truncated by query limit.</param>
/// <param name="QueryLimit">Applied query row limit.</param>
public sealed record ApiRequestLogVolumeSummary(
    int Last1MinuteCount,
    int Last5MinutesCount,
    DateTimeOffset CalculatedAt,
    bool MayBeCapped,
    int QueryLimit);
