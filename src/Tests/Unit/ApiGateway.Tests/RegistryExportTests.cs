using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Registry;
using ApiGateway.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiGateway.Tests;

public sealed class RegistryExportTests
{
    [Fact]
    public async Task HandleOpenExportAsync_WhenExportMissing_ReturnsNotFound()
    {
        var (service, root) = CreateService();
        try
        {
            var context = CreateContext();

            var result = await RegistryExportEndpoint.HandleOpenExportAsync(
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
            var export = await service.CreateExportAsync(
                new RegistryExportRequest("tenant-a", Grains.Abstractions.GraphNodeType.Point, 1),
                BuildNodes(),
                CancellationToken.None);
            var context = CreateContext();

            var result = await RegistryExportEndpoint.HandleOpenExportAsync(
                export.ExportId,
                service,
                context,
                export.ExpiresAt.AddMinutes(1));

            await result.ExecuteAsync(context);
            context.Response.StatusCode.Should().Be(StatusCodes.Status410Gone);
        }
        finally
        {
            CleanupTempDirectory(root);
        }
    }

    [Fact]
    public async Task HandleOpenExportAsync_WhenTenantDiffers_ReturnsNotFound()
    {
        var (service, root) = CreateService();
        try
        {
            var export = await service.CreateExportAsync(
                new RegistryExportRequest("tenant-a", Grains.Abstractions.GraphNodeType.Point, 1),
                BuildNodes(),
                CancellationToken.None);
            var context = CreateContext("tenant-b");

            var result = await RegistryExportEndpoint.HandleOpenExportAsync(
                export.ExportId,
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
    public async Task HandleOpenExportAsync_WhenReady_ReturnsFile()
    {
        var (service, root) = CreateService();
        try
        {
            var export = await service.CreateExportAsync(
                new RegistryExportRequest("tenant-a", Grains.Abstractions.GraphNodeType.Point, 1),
                BuildNodes(),
                CancellationToken.None);
            var context = CreateContext();

            var result = await RegistryExportEndpoint.HandleOpenExportAsync(
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

    [Fact]
    public async Task HandleOpenExportAsync_MultipleRequestsToSameExport_AllSucceed()
    {
        var (service, root) = CreateService();
        try
        {
            var export = await service.CreateExportAsync(
                new RegistryExportRequest("tenant-a", Grains.Abstractions.GraphNodeType.Point, 1),
                BuildNodes(),
                CancellationToken.None);

            for (var i = 0; i < 2; i++)
            {
                var context = CreateContext();
                var result = await RegistryExportEndpoint.HandleOpenExportAsync(
                    export.ExportId,
                    service,
                    context,
                    DateTimeOffset.UtcNow);

                await result.ExecuteAsync(context);
                context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
                context.Response.Body.Length.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            CleanupTempDirectory(root);
        }
    }

    private static (RegistryExportService Service, string Root) CreateService()
    {
        var root = Path.Combine(Path.GetTempPath(), "registry-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var options = Options.Create(new RegistryExportOptions
        {
            ExportRoot = root,
            DefaultTtlMinutes = 5,
            MaxInlineRecords = 1
        });

        var service = new RegistryExportService(options, NullLogger<RegistryExportService>.Instance);
        return (service, root);
    }

    private static DefaultHttpContext CreateContext(string tenant = "tenant-a")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant", tenant),
            new Claim(ClaimTypes.Name, "registry-test")
        }, TestAuthHandler.SchemeName));
        return context;
    }

    private static IReadOnlyList<RegistryNodeSummary> BuildNodes()
    {
        return new[]
        {
            new RegistryNodeSummary(
                "point-1",
                Grains.Abstractions.GraphNodeType.Point,
                "Point 1",
                new Dictionary<string, string>())
        };
    }

    private static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
