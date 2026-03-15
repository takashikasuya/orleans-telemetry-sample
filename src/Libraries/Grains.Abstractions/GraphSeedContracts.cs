using System;
using Orleans;
using Orleans.CodeGeneration;

namespace Grains.Abstractions;

/// <summary>
/// Request payload for RDF-based graph seeding.
/// </summary>
[GenerateSerializer]
public sealed record GraphSeedRequest
{
    /// <summary>
    /// Gets the RDF file path.
    /// </summary>
    [Id(0)]
    public string RdfPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    [Id(1)]
    public string TenantId { get; init; } = "default";

    /// <summary>
    /// Gets the optional tenant display name.
    /// </summary>
    [Id(2)]
    public string? TenantName { get; init; }

    /// <summary>
    /// Initializes a new empty request instance.
    /// </summary>
    public GraphSeedRequest() { }

    /// <summary>
    /// Initializes a new request with values.
    /// </summary>
    /// <param name="rdfPath">RDF file path.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="tenantName">Optional tenant display name.</param>
    public GraphSeedRequest(string rdfPath, string tenantId, string? tenantName = null)
    {
        RdfPath = rdfPath;
        TenantId = tenantId;
        TenantName = tenantName;
    }
}

/// <summary>
/// Result status returned after graph seeding.
/// </summary>
[GenerateSerializer]
public sealed record GraphSeedStatus
{
    /// <summary>
    /// Gets whether seeding completed successfully.
    /// </summary>
    [Id(0)]
    public bool Success { get; init; }

    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    [Id(1)]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the RDF path used for seeding.
    /// </summary>
    [Id(2)]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the processing start time.
    /// </summary>
    [Id(3)]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the processing completion time.
    /// </summary>
    [Id(4)]
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Gets the created node count.
    /// </summary>
    [Id(5)]
    public int NodeCount { get; init; }

    /// <summary>
    /// Gets the created edge count.
    /// </summary>
    [Id(6)]
    public int EdgeCount { get; init; }

    /// <summary>
    /// Gets the optional status message.
    /// </summary>
    [Id(7)]
    public string? Message { get; init; }

    /// <summary>
    /// Gets the optional tenant display name.
    /// </summary>
    [Id(8)]
    public string? TenantName { get; init; }

    /// <summary>
    /// Initializes a new empty status instance.
    /// </summary>
    public GraphSeedStatus() { }

    /// <summary>
    /// Initializes a new status instance.
    /// </summary>
    /// <param name="success">Whether seeding was successful.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="path">RDF path.</param>
    /// <param name="startedAt">Start time.</param>
    /// <param name="completedAt">Completion time.</param>
    /// <param name="nodeCount">Generated node count.</param>
    /// <param name="edgeCount">Generated edge count.</param>
    /// <param name="message">Optional message.</param>
    /// <param name="tenantName">Optional tenant display name.</param>
    public GraphSeedStatus(
        bool success,
        string tenantId,
        string path,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int nodeCount,
        int edgeCount,
        string? message,
        string? tenantName = null)
    {
        Success = success;
        TenantId = tenantId;
        Path = path;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        Message = message;
        TenantName = tenantName;
    }
}

/// <summary>
/// Grain contract for RDF graph seeding operations.
/// </summary>
public interface IGraphSeedGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Gets the latest seed result.
    /// </summary>
    /// <returns>Latest seed status or null when not available.</returns>
    Task<GraphSeedStatus?> GetLastResultAsync();

    /// <summary>
    /// Executes graph seeding.
    /// </summary>
    /// <param name="request">Seed request.</param>
    /// <returns>Seed execution status.</returns>
    Task<GraphSeedStatus> SeedAsync(GraphSeedRequest request);
}
