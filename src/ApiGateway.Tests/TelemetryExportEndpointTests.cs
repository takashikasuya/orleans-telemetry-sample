using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Storage;
using Xunit;

namespace ApiGateway.Tests;

public sealed class TelemetryExportEndpointTests
{
    [Fact]
    public async Task HandleOpenExportAsync_WhenExportMissing_ReturnsNotFound()
    {
        var (service, root) = CreateService();
        try
        {
            var context = CreateContext();
            var result = await TelemetryExportEndpoint.HandleOpenExportAsync(
                "missing-export",
                service,
                context,
                DateTimeOffset.UtcNow);

            await result.ExecuteAsync(context);
            context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }
        finally
        {
            CleanupTempDirectory(root);
        }
    }

    [Fact]
    public async Task HandleOpenExportAsync_WhenExportExpired_ReturnsGone()
    {
        var (service, root) = CreateService();
        try
        {
            var request = BuildQueryRequest("t1");
            var results = BuildResults(request);
            var export = await service.CreateExportAsync(request, results, CancellationToken.None);
            var context = CreateContext();
            var expiredAt = export.ExpiresAt.AddMinutes(1);

            var result = await TelemetryExportEndpoint.HandleOpenExportAsync(
                export.ExportId,
                service,
                context,
                expiredAt);

            await result.ExecuteAsync(context);
            context.Response.StatusCode.Should().Be(StatusCodes.Status410Gone);
        }
        finally
        {
            CleanupTempDirectory(root);
        }
    }

    [Fact]
    public async Task HandleOpenExportAsync_WhenExportReady_ReturnsFile()
    {
        var (service, root) = CreateService();
        try
        {
            var request = BuildQueryRequest("t1");
            var results = BuildResults(request);
            var export = await service.CreateExportAsync(request, results, CancellationToken.None);
            var context = CreateContext();

            var result = await TelemetryExportEndpoint.HandleOpenExportAsync(
                export.ExportId,
                service,
                context,
                DateTimeOffset.UtcNow);

            await result.ExecuteAsync(context);
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            context.Response.ContentType.Should().Be("application/x-ndjson");
            context.Response.Body.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            CleanupTempDirectory(root);
        }
    }

    private static (TelemetryExportService Service, string Root) CreateService()
    {
        var root = CreateTempDirectory();
        var options = Options.Create(new TelemetryExportOptions
        {
            ExportRoot = root,
            DefaultTtlMinutes = 5
        });

        var service = new TelemetryExportService(options, NullLogger<TelemetryExportService>.Instance);
        return (service, root);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        return context;
    }

    private static TelemetryQueryRequest BuildQueryRequest(string tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        return new TelemetryQueryRequest(
            tenantId,
            "device-1",
            now.AddMinutes(-5),
            now,
            "point-1",
            null);
    }

    private static IReadOnlyList<TelemetryQueryResult> BuildResults(TelemetryQueryRequest request)
    {
        return new List<TelemetryQueryResult>
        {
            new(request.TenantId, request.DeviceId, request.PointId!, request.From.AddMinutes(1), 1, "10", null, null),
            new(request.TenantId, request.DeviceId, request.PointId!, request.From.AddMinutes(2), 2, "12", null, null)
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-export-endpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
