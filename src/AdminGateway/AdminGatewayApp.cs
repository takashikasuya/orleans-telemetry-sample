using System.Net;
using AdminGateway.Hubs;
using AdminGateway.Models;
using AdminGateway.Services;
using Grains.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Orleans;
using Orleans.Configuration;
using Telemetry.Ingest;
using Telemetry.Storage;

namespace AdminGateway;

public static class AdminGatewayApp
{
    public static void Configure(WebApplicationBuilder builder, bool includeOrleansClient)
    {
        ConfigureServices(builder);

        if (!includeOrleansClient)
        {
            return;
        }

        var retryInitialSeconds = Math.Max(1, builder.Configuration.GetValue("Orleans:ClientRetry:InitialDelaySeconds", 2));
        var retryMaxSeconds = Math.Max(retryInitialSeconds, builder.Configuration.GetValue("Orleans:ClientRetry:MaxDelaySeconds", 30));

        var orleansHost = builder.Configuration["Orleans:GatewayHost"] ?? "127.0.0.1";
        var orleansAddresses = Dns.GetHostAddresses(orleansHost);
        var orleansAddress = orleansAddresses.Length > 0 ? orleansAddresses[0] : IPAddress.Loopback;
        var orleansPort = int.TryParse(builder.Configuration["Orleans:GatewayPort"], out var parsedPort) ? parsedPort : 30000;

        builder.Host.UseOrleansClient(client =>
        {
            client.UseStaticClustering(new IPEndPoint(orleansAddress, orleansPort));
            client.Configure<ClusterOptions>(opts =>
            {
                opts.ClusterId = "telemetry-cluster";
                opts.ServiceId = "telemetry-service";
            });
            client.ConfigureServices(services =>
            {
                services.AddSingleton<IClientConnectionRetryFilter>(sp =>
                    new AdminClientConnectionRetryFilter(
                        sp.GetRequiredService<ILogger<AdminClientConnectionRetryFilter>>(),
                        TimeSpan.FromSeconds(retryInitialSeconds),
                        TimeSpan.FromSeconds(retryMaxSeconds)));
            });
            client.AddMemoryStreams("DeviceUpdates");
            client.AddMemoryStreams("PointUpdates");
        });
    }

    public static void ConfigureApp(WebApplication app)
    {
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/admin/grains", async (AdminMetricsService metrics) =>
        {
            var result = await metrics.GetGrainActivationsAsync();
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/grains/hierarchy", async (AdminMetricsService metrics, int? maxTypesPerSilo, int? maxGrainsPerType) =>
        {
            var typeLimit = maxTypesPerSilo is > 0 ? maxTypesPerSilo.Value : 20;
            var grainLimit = maxGrainsPerType is > 0 ? maxGrainsPerType.Value : 50;
            var result = await metrics.GetGrainHierarchyAsync(typeLimit, grainLimit);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/clients", async (AdminMetricsService metrics) =>
        {
            var result = await metrics.GetSiloSummariesAsync();
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/storage", async (AdminMetricsService metrics, CancellationToken ct) =>
        {
            var result = await metrics.GetStorageOverviewAsync(ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/graph/import/status", async (AdminMetricsService metrics) =>
        {
            var status = await metrics.GetLastGraphSeedStatusAsync();
            return Results.Ok(status);
        }).RequireAuthorization();

        app.MapGet("/admin/graph/tenants", async (AdminMetricsService metrics) =>
        {
            var tenants = await metrics.GetGraphTenantsAsync();
            return Results.Ok(tenants);
        }).RequireAuthorization();

        app.MapPost("/admin/graph/import", async (GraphSeedRequest request, AdminMetricsService metrics) =>
        {
            var status = await metrics.TriggerGraphSeedAsync(request);
            return Results.Ok(status);
        }).RequireAuthorization();

        app.MapGet("/admin/ingest", (AdminMetricsService metrics) =>
        {
            var result = metrics.GetIngestSummary();
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/graph/statistics", async (AdminMetricsService metrics, string? tenantId) =>
        {
            var tenant = tenantId ?? "default";
            var result = await metrics.GetGraphStatisticsAsync(tenant);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/admin/graph/hierarchy", async (AdminMetricsService metrics, string? tenantId, int? maxDepth) =>
        {
            var tenant = tenantId ?? "default";
            var depth = maxDepth ?? 3;
            var result = await metrics.GetGraphHierarchyAsync(tenant, depth);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapRazorPages();
        app.MapBlazorHub();
        app.MapHub<TelemetryHub>("/telemetryHub");
        app.MapFallbackToPage("/_Host");
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["OIDC_AUTHORITY"] ?? "http://mock-oidc:8080/default";
        var audience = builder.Configuration["OIDC_AUDIENCE"] ?? builder.Configuration["ADMIN_AUDIENCE"] ?? "api";
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            });

        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();
        builder.Services.AddSignalR();
        builder.Services.AddMudServices();
        builder.Services.Configure<TelemetryStorageOptions>(builder.Configuration.GetSection("TelemetryStorage"));
        builder.Services.Configure<TelemetryIngestOptions>(builder.Configuration.GetSection("TelemetryIngest"));
        builder.Services.AddSingleton<TelemetryStorageScanner>();
        builder.Services.AddSingleton<ITelemetryStorageQuery, ParquetTelemetryStorageQuery>();
        builder.Services.AddScoped<AdminMetricsService>();
    }
}
