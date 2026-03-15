using System.IO;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Services;

internal sealed class RegistryExportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RegistryExportOptions _options;
    private readonly ILogger<RegistryExportService> _logger;

    public RegistryExportService(IOptions<RegistryExportOptions> options, ILogger<RegistryExportService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegistryExportReference> CreateExportAsync(
        RegistryExportRequest request,
        IReadOnlyList<RegistryNodeSummary> nodes,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ttlMinutes = Math.Max(1, _options.DefaultTtlMinutes);
        var expiresAt = now.AddMinutes(ttlMinutes);
        var exportId = Guid.NewGuid().ToString("N");
        var dataPath = GetDataPath(exportId);
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

        await using (var stream = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var node in nodes)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(node, SerializerOptions);
                await writer.WriteLineAsync(json);
            }
        }

        var metadata = new RegistryExportMetadata(
            exportId,
            request.TenantId,
            request.NodeType,
            now,
            expiresAt,
            dataPath,
            "application/x-ndjson",
            nodes.Count);

        var metadataPath = GetMetadataPath(exportId);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadataJson = JsonSerializer.Serialize(metadata, SerializerOptions);
        await File.WriteAllTextAsync(metadataPath, metadataJson, ct);

        return new RegistryExportReference(exportId, BuildExportUrl(exportId), expiresAt, nodes.Count);
    }

    public async Task<RegistryExportMetadata?> GetMetadataAsync(string exportId, CancellationToken ct)
    {
        var metadataPath = GetMetadataPath(exportId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(metadataPath, ct);
        return JsonSerializer.Deserialize<RegistryExportMetadata>(json, SerializerOptions);
    }

    public async Task<RegistryExportOpenResult> TryOpenExportAsync(
        string exportId,
        string tenantId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var metadata = await GetMetadataAsync(exportId, ct);
        if (metadata is null)
        {
            return new RegistryExportOpenResult(RegistryExportOpenStatus.NotFound, null, null);
        }

        if (!string.Equals(metadata.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return new RegistryExportOpenResult(RegistryExportOpenStatus.NotFound, null, null);
        }

        if (metadata.ExpiresAt <= now)
        {
            await DeleteExportAsync(metadata, ct);
            return new RegistryExportOpenResult(RegistryExportOpenStatus.Expired, null, null);
        }

        if (!File.Exists(metadata.FilePath))
        {
            return new RegistryExportOpenResult(RegistryExportOpenStatus.NotFound, null, null);
        }

        var stream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new RegistryExportOpenResult(RegistryExportOpenStatus.Ready, metadata, stream);
    }

    public async Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        var indexRoot = Path.Combine(_options.ExportRoot, "index");
        if (!Directory.Exists(indexRoot))
        {
            return 0;
        }

        var count = 0;
        foreach (var filePath in Directory.EnumerateFiles(indexRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                var metadata = JsonSerializer.Deserialize<RegistryExportMetadata>(json, SerializerOptions);
                if (metadata is null)
                {
                    continue;
                }

                if (metadata.ExpiresAt <= now)
                {
                    await DeleteExportAsync(metadata, ct);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inspect registry export metadata {FilePath}.", filePath);
            }
        }

        return count;
    }

    private async Task DeleteExportAsync(RegistryExportMetadata metadata, CancellationToken ct)
    {
        try
        {
            if (File.Exists(metadata.FilePath))
            {
                File.Delete(metadata.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete registry export data {FilePath}.", metadata.FilePath);
        }

        try
        {
            var metadataPath = GetMetadataPath(metadata.ExportId);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete registry export metadata {ExportId}.", metadata.ExportId);
        }

        await Task.CompletedTask;
    }

    private string BuildExportUrl(string exportId)
        => $"/api/registry/exports/{exportId}";

    private string GetMetadataPath(string exportId)
        => Path.Combine(_options.ExportRoot, "index", $"{exportId}.json");

    private string GetDataPath(string exportId)
        => Path.Combine(_options.ExportRoot, "data", $"registry_{exportId}.jsonl");
}

internal sealed record RegistryExportReference(
    string ExportId,
    string Url,
    DateTimeOffset ExpiresAt,
    int Count);

internal sealed record RegistryExportMetadata(
    string ExportId,
    string TenantId,
    GraphNodeType NodeType,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string FilePath,
    string ContentType,
    int RecordCount);

internal enum RegistryExportOpenStatus
{
    Ready = 0,
    NotFound = 1,
    Expired = 2
}

internal sealed record RegistryExportOpenResult(
    RegistryExportOpenStatus Status,
    RegistryExportMetadata? Metadata,
    FileStream? Stream);

internal sealed record RegistryExportRequest(
    string TenantId,
    GraphNodeType NodeType,
    int NodeCount);

internal sealed class RegistryExportOptions
{
    public string ExportRoot { get; set; } = "storage/registry-exports";
    public int DefaultTtlMinutes { get; set; } = 60;
    public int CleanupIntervalSeconds { get; set; } = 300;
    public int MaxInlineRecords { get; set; } = 1000;
}
