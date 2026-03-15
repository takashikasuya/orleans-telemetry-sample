using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ApiGateway.Telemetry;
using Telemetry.Storage;
using Xunit;

namespace Telemetry.E2E.Tests;

public sealed class TelemetryExportServiceTests
{
    [Fact]
    public async Task CreateExportAsync_WritesMetadataAndData()
    {
        var root = CreateTempDirectory();
        var options = Options.Create(new TelemetryExportOptions
        {
            ExportRoot = root,
            DefaultTtlMinutes = 5
        });
        var service = new TelemetryExportService(options, NullLogger<TelemetryExportService>.Instance);

        var now = DateTimeOffset.UtcNow;
        var request = new TelemetryQueryRequest(
            "tenant-a",
            "device-1",
            now.AddMinutes(-5),
            now,
            "point-1",
            null);
        var results = new List<TelemetryQueryResult>
        {
            new("tenant-a", "device-1", "point-1", now.AddMinutes(-4), 1, "10", null, null),
            new("tenant-a", "device-1", "point-1", now.AddMinutes(-3), 2, "11", null, null)
        };

        var export = await service.CreateExportAsync(request, results, CancellationToken.None);

        var metadata = await service.GetMetadataAsync(export.ExportId, CancellationToken.None);
        metadata.Should().NotBeNull();
        metadata!.RecordCount.Should().Be(2);
        File.Exists(metadata.FilePath).Should().BeTrue();

        var opened = await service.TryOpenExportAsync(export.ExportId, "tenant-a", DateTimeOffset.UtcNow, CancellationToken.None);
        opened.Status.Should().Be(TelemetryExportOpenStatus.Ready);
        opened.Stream.Should().NotBeNull();
        using (var reader = new StreamReader(opened.Stream!))
        {
            var lines = new List<string>();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            lines.Should().HaveCount(2);
        }

        var cleaned = await service.CleanupExpiredAsync(metadata.ExpiresAt.AddMinutes(1), CancellationToken.None);
        cleaned.Should().Be(1);
        File.Exists(metadata.FilePath).Should().BeFalse();
        var deletedMetadata = await service.GetMetadataAsync(export.ExportId, CancellationToken.None);
        deletedMetadata.Should().BeNull();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
