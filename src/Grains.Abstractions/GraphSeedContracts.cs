using System;
using Orleans;
using Orleans.CodeGeneration;

namespace Grains.Abstractions;

[GenerateSerializer]
public sealed record GraphSeedRequest
{
    [Id(0)]
    public string RdfPath { get; init; } = string.Empty;

    [Id(1)]
    public string TenantId { get; init; } = "default";

    public GraphSeedRequest() { }

    public GraphSeedRequest(string rdfPath, string tenantId)
    {
        RdfPath = rdfPath;
        TenantId = tenantId;
    }
}

[GenerateSerializer]
public sealed record GraphSeedStatus
{
    [Id(0)]
    public bool Success { get; init; }

    [Id(1)]
    public string TenantId { get; init; } = string.Empty;

    [Id(2)]
    public string Path { get; init; } = string.Empty;

    [Id(3)]
    public DateTimeOffset StartedAt { get; init; }

    [Id(4)]
    public DateTimeOffset CompletedAt { get; init; }

    [Id(5)]
    public int NodeCount { get; init; }

    [Id(6)]
    public int EdgeCount { get; init; }

    [Id(7)]
    public string? Message { get; init; }

    public GraphSeedStatus() { }

    public GraphSeedStatus(
        bool success,
        string tenantId,
        string path,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int nodeCount,
        int edgeCount,
        string? message)
    {
        Success = success;
        TenantId = tenantId;
        Path = path;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        Message = message;
    }
}

public interface IGraphSeedGrain : IGrainWithGuidKey
{
    Task<GraphSeedStatus?> GetLastResultAsync();

    Task<GraphSeedStatus> SeedAsync(GraphSeedRequest request);
}
