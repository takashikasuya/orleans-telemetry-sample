using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Storage;

namespace ApiGateway.Telemetry;

public sealed class TelemetryExportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly TelemetryExportOptions _options;
    private readonly ILogger<TelemetryExportService> _logger;

    public TelemetryExportService(
        IOptions<TelemetryExportOptions> options,
        ILogger<TelemetryExportService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TelemetryExportReference> CreateExportAsync(
        TelemetryQueryRequest request,
        IReadOnlyList<TelemetryQueryResult> results,
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
            foreach (var item in results)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(item, SerializerOptions);
                await writer.WriteLineAsync(json);
            }
        }

        var metadata = new TelemetryExportMetadata(
            exportId,
            request.TenantId,
            request.DeviceId,
            request.From,
            request.To,
            request.PointId,
            now,
            expiresAt,
            dataPath,
            "application/x-ndjson",
            results.Count);

        var metadataPath = GetMetadataPath(exportId);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadataJson = JsonSerializer.Serialize(metadata, SerializerOptions);
        await File.WriteAllTextAsync(metadataPath, metadataJson, ct);

        return new TelemetryExportReference(exportId, BuildExportUrl(exportId), expiresAt, results.Count);
    }

    public async Task<TelemetryExportMetadata?> GetMetadataAsync(string exportId, CancellationToken ct)
    {
        var metadataPath = GetMetadataPath(exportId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(metadataPath, ct);
        return JsonSerializer.Deserialize<TelemetryExportMetadata>(json, SerializerOptions);
    }

    public async Task<TelemetryExportOpenResult> TryOpenExportAsync(
        string exportId,
        string tenantId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var metadata = await GetMetadataAsync(exportId, ct);
        if (metadata is null)
        {
            return new TelemetryExportOpenResult(TelemetryExportOpenStatus.NotFound, null, null);
        }

        if (!string.Equals(metadata.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return new TelemetryExportOpenResult(TelemetryExportOpenStatus.NotFound, null, null);
        }

        if (metadata.ExpiresAt <= now)
        {
            await DeleteExportAsync(metadata, ct);
            return new TelemetryExportOpenResult(TelemetryExportOpenStatus.Expired, null, null);
        }

        if (!File.Exists(metadata.FilePath))
        {
            return new TelemetryExportOpenResult(TelemetryExportOpenStatus.NotFound, null, null);
        }

        var stream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new TelemetryExportOpenResult(TelemetryExportOpenStatus.Ready, metadata, stream);
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
                var metadata = JsonSerializer.Deserialize<TelemetryExportMetadata>(json, SerializerOptions);
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
                _logger.LogWarning(ex, "Failed to inspect export metadata {FilePath}.", filePath);
            }
        }

        return count;
    }

    private async Task DeleteExportAsync(TelemetryExportMetadata metadata, CancellationToken ct)
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
            _logger.LogWarning(ex, "Failed to delete export data {FilePath}.", metadata.FilePath);
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
            _logger.LogWarning(ex, "Failed to delete export metadata {ExportId}.", metadata.ExportId);
        }

        await Task.CompletedTask;
    }

    private string BuildExportUrl(string exportId)
        => $"/api/telemetry/exports/{exportId}";

    private string GetMetadataPath(string exportId)
        => Path.Combine(_options.ExportRoot, "index", $"{exportId}.json");

    private string GetDataPath(string exportId)
        => Path.Combine(_options.ExportRoot, "data", $"telemetry_{exportId}.jsonl");
}

public sealed record TelemetryExportReference(
    string ExportId,
    string Url,
    DateTimeOffset ExpiresAt,
    int Count);

public sealed record TelemetryExportMetadata(
    string ExportId,
    string TenantId,
    string DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? PointId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string FilePath,
    string ContentType,
    int RecordCount);

public enum TelemetryExportOpenStatus
{
    Ready = 0,
    NotFound = 1,
    Expired = 2
}

public sealed record TelemetryExportOpenResult(
    TelemetryExportOpenStatus Status,
    TelemetryExportMetadata? Metadata,
    FileStream? Stream);
