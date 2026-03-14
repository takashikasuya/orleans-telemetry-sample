using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AdminGateway.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Storage;

namespace AdminGateway.Services;

internal sealed class TelemetryStorageScanner
{
    private readonly TelemetryStorageOptions _options;
    private readonly ILogger<TelemetryStorageScanner> _logger;

    public TelemetryStorageScanner(
        IOptions<TelemetryStorageOptions> options,
        ILogger<TelemetryStorageScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<StorageOverview> ScanAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => Scan(cancellationToken), cancellationToken);
    }

    private StorageOverview Scan(CancellationToken cancellationToken)
    {
        var stage = ScanRoot(_options.StagePath, "stage", cancellationToken);
        var parquet = ScanRoot(_options.ParquetPath, "parquet", cancellationToken);
        var index = ScanRoot(_options.IndexPath, "index", cancellationToken);
        return new StorageOverview(stage, parquet, index);
    }

    private StorageTierSummary ScanRoot(string root, string tier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new StorageTierSummary(tier, root, 0, 0, null, Array.Empty<StorageTenantSummary>());
        }

        long totalBytes = 0;
        long fileCount = 0;
        DateTime? latest = null;
        var tenants = new Dictionary<string, StorageTenantBuilder>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                totalBytes += fileInfo.Length;
                fileCount++;
                if (!latest.HasValue || fileInfo.LastWriteTimeUtc > latest)
                {
                    latest = fileInfo.LastWriteTimeUtc;
                }

                var tenantId = ExtractTenantId(root, fileInfo.FullName);
                if (!tenants.TryGetValue(tenantId, out var builder))
                {
                    builder = new StorageTenantBuilder(tenantId);
                    tenants[tenantId] = builder;
                }

                builder.AddFile(fileInfo);
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect storage tier {Tier} at {Path}", tier, root);
        }

        var tenantSummaries = tenants.Values
            .OrderBy(builder => builder.TenantId, StringComparer.OrdinalIgnoreCase)
            .Select(builder => builder.ToSummary())
            .ToArray();

        return new StorageTierSummary(tier, root, fileCount, totalBytes, latest, tenantSummaries);
    }

    private static string ExtractTenantId(string root, string filePath)
    {
        var relative = Path.GetRelativePath(root, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && segments[0].StartsWith("tenant=", StringComparison.OrdinalIgnoreCase))
        {
            return segments[0]["tenant=".Length..];
        }

        return "unknown";
    }

    private sealed class StorageTenantBuilder
    {
        public StorageTenantBuilder(string tenantId) => TenantId = tenantId;

        public string TenantId { get; }
        private long _fileCount;
        private long _totalBytes;
        private DateTime? _latest;

        public void AddFile(FileInfo info)
        {
            _fileCount++;
            _totalBytes += info.Length;
            if (!_latest.HasValue || info.LastWriteTimeUtc > _latest)
            {
                _latest = info.LastWriteTimeUtc;
            }
        }

        public StorageTenantSummary ToSummary() => new(TenantId, _fileCount, _totalBytes, _latest);
    }
}
