using System.Net;
using System;
using System.Collections.Generic;
using ApiGateway.Contracts;
using ApiControlRequest = ApiGateway.Contracts.PointControlRequest;
using ApiControlResponse = ApiGateway.Contracts.PointControlResponse;
using ApiGateway.Infrastructure;
using ApiGateway.Services;
using ApiGateway.Sparql;
using ApiGateway.Telemetry;
using Grains.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orleans;
using Telemetry.Storage;

var builder = WebApplication.CreateBuilder(args);

// Configure authentication using OIDC / JWT
var authority = builder.Configuration["OIDC_AUTHORITY"] ?? "http://mock-oidc:8080/default";
var audience = builder.Configuration["OIDC_AUDIENCE"] ?? "api";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = authority.StartsWith("https://");
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<GraphTraversal>();
builder.Services.Configure<TelemetryStorageOptions>(builder.Configuration.GetSection("TelemetryStorage"));
builder.Services.AddSingleton<ITelemetryStorageQuery, ParquetTelemetryStorageQuery>();
builder.Services.Configure<TelemetryExportOptions>(builder.Configuration.GetSection("TelemetryExport"));
builder.Services.AddSingleton<TelemetryExportService>();
builder.Services.AddHostedService<TelemetryExportCleanupService>();
builder.Services.Configure<RegistryExportOptions>(builder.Configuration.GetSection("RegistryExport"));
builder.Services.AddSingleton<RegistryExportService>();
builder.Services.AddSingleton<GraphRegistryService>();
builder.Services.AddSingleton<TagSearchService>();
builder.Services.AddSingleton<GraphPointResolver>();
builder.Services.AddHostedService<RegistryExportCleanupService>();

// Configure gRPC
builder.Services.AddGrpc();
var grpcEnabled = builder.Configuration.GetValue<bool>("Grpc:Enabled", true);

// Swagger for convenience
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure Orleans client
var disableOrleansClient = builder.Configuration.GetValue<bool>("Orleans:DisableClient", false);
if (!disableOrleansClient)
{
    var orleansHost = builder.Configuration["Orleans:GatewayHost"] ?? "127.0.0.1";
    var orleansPort = int.TryParse(builder.Configuration["Orleans:GatewayPort"], out var parsedPort) ? parsedPort : 30000;
    
    // Try to resolve the hostname, but handle cases where it might be a Docker service name
    IPAddress orleansAddress;
    if (IPAddress.TryParse(orleansHost, out var ipAddress))
    {
        // It's already an IP address
        orleansAddress = ipAddress;
    }
    else
    {
        // Try to resolve hostname via DNS
        try
        {
            var addresses = Dns.GetHostAddresses(orleansHost);
            if (addresses.Length > 0)
            {
                // Prefer IPv4
                orleansAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) 
                    ?? addresses[0];
            }
            else
            {
                orleansAddress = IPAddress.Loopback;
            }
        }
        catch (Exception)
        {
            // DNS resolution failed, fall back to loopback
            orleansAddress = IPAddress.Loopback;
        }
    }

    builder.Host.UseOrleansClient(client =>
    {
        client.UseStaticClustering(new IPEndPoint(orleansAddress, orleansPort));
        client.Configure<Orleans.Configuration.ClusterOptions>(opts =>
        {
            opts.ClusterId = "telemetry-cluster";
            opts.ServiceId = "telemetry-service";
        });
        client.AddMemoryStreams("DeviceUpdates");
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// REST endpoint to fetch device snapshot
app.MapGet("/api/devices/{deviceId}", async (
    string deviceId,
    IClusterClient client,
    GraphPointResolver pointResolver,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grainKey = DeviceGrainKey.Create(tenant, deviceId);
    var grain = client.GetGrain<IDeviceGrain>(grainKey);
    var snap = await grain.GetAsync();
    var points = await pointResolver.GetPointsForDeviceAsync(tenant, deviceId);
    http.Response.Headers.ETag = $"W/\"{snap.LastSequence}\"";
    return Results.Ok(new
    {
        deviceId,
        snap.LastSequence,
        snap.UpdatedAt,
        Properties = snap.LatestProps,
        Points = points
    });
}).RequireAuthorization();

app.MapPost("/api/devices/{deviceId}/control", async (
    string deviceId,
    ApiControlRequest command,
    IClusterClient client,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);

    if (string.IsNullOrWhiteSpace(command.DeviceId))
    {
        return Results.BadRequest(new { Message = "DeviceId is required." });
    }

    if (!string.Equals(deviceId, command.DeviceId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Route deviceId must match request payload." });
    }

    if (string.IsNullOrWhiteSpace(command.PointId))
    {
        return Results.BadRequest(new { Message = "PointId is required." });
    }

    var requestId = string.IsNullOrWhiteSpace(command.CommandId) ? Guid.NewGuid().ToString("D") : command.CommandId;
    var metadata = command.Metadata ?? new Dictionary<string, string>();
    var grainRequest = new Grains.Abstractions.PointControlRequest
    {
        CommandId = requestId,
        TenantId = tenant,
        BuildingName = command.BuildingName ?? string.Empty,
        SpaceId = command.SpaceId ?? string.Empty,
        DeviceId = command.DeviceId,
        PointId = command.PointId,
        DesiredValue = command.DesiredValue,
        Metadata = metadata,
        RequestedAt = DateTimeOffset.UtcNow
    };

    var grainKey = PointControlGrainKey.Create(tenant, deviceId, command.PointId);
    var grain = client.GetGrain<IPointControlGrain>(grainKey);
    var snapshot = await grain.SubmitAsync(grainRequest);
    var response = new ApiControlResponse(
        snapshot.CommandId,
        snapshot.Status.ToString(),
        snapshot.RequestedAt,
        snapshot.AcceptedAt,
        snapshot.AppliedAt,
        snapshot.ConnectorName,
        snapshot.CorrelationId,
        snapshot.LastError);

    var location = $"/api/devices/{deviceId}/control/{snapshot.CommandId}";
    return Results.Accepted(location, response);
}).RequireAuthorization();


// Graph endpoints for node metadata and value bindings
app.MapGet("/api/nodes/{nodeId}", async (
    string nodeId,
    IClusterClient client,
    GraphPointResolver pointResolver,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grainKey = GraphNodeKey.Create(tenant, nodeId);
    var grain = client.GetGrain<IGraphNodeGrain>(grainKey);
    var snap = await grain.GetAsync();
    var points = await pointResolver.GetPointsForNodeAsync(tenant, snap);
    return Results.Ok(new
    {
        snap.Node,
        snap.OutgoingEdges,
        snap.IncomingEdges,
        Points = points
    });
}).RequireAuthorization();

app.MapGet("/api/nodes/{nodeId}/value", async (string nodeId, IClusterClient client, HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grainKey = GraphNodeKey.Create(tenant, nodeId);
    var grain = client.GetGrain<IGraphNodeGrain>(grainKey);
    var snap = await grain.GetAsync();

    if (!snap.Node.Attributes.TryGetValue("PointId", out var pointId) || string.IsNullOrWhiteSpace(pointId))
    {
        return Results.NotFound(new { Message = "PointId attribute is missing for the requested node." });
    }

    var pointKey = PointGrainKey.Create(tenant, pointId);
    var pointGrain = client.GetGrain<IPointGrain>(pointKey);
    var pointSnap = await pointGrain.GetAsync();
    return Results.Ok(pointSnap);
}).RequireAuthorization();

app.MapGet("/api/graph/traverse/{nodeId}", async (
    string nodeId,
    int? depth,
    string? predicate,
    IClusterClient client,
    HttpContext http,
    GraphTraversal traversal) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var requestedDepth = depth ?? 1;
    var cappedDepth = Math.Min(Math.Max(requestedDepth, 0), 5);
    var result = await traversal.TraverseAsync(client, tenant, nodeId, cappedDepth, predicate);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/devices", async (
    int? limit,
    GraphRegistryService registry,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registry.GetNodesAsync(tenant, GraphNodeType.Equipment, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/spaces", async (
    int? limit,
    GraphRegistryService registry,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registry.GetNodesAsync(tenant, GraphNodeType.Area, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/points", async (
    int? limit,
    GraphRegistryService registry,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registry.GetNodesAsync(tenant, GraphNodeType.Point, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/buildings", async (
    int? limit,
    GraphRegistryService registry,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registry.GetNodesAsync(tenant, GraphNodeType.Building, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/sites", async (
    int? limit,
    GraphRegistryService registry,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registry.GetNodesAsync(tenant, GraphNodeType.Site, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/search/nodes", async (
    string[] tags,
    int? limit,
    TagSearchService tagSearch,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await tagSearch.SearchNodesByTagsAsync(tenant, tags, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/search/grains", async (
    string[] tags,
    int? limit,
    TagSearchService tagSearch,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await tagSearch.SearchGrainsByTagsAsync(tenant, tags, limit, http.RequestAborted);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/registry/exports/{exportId}", async (
    string exportId,
    RegistryExportService registryExports,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var result = await registryExports.TryOpenExportAsync(exportId, tenant, DateTimeOffset.UtcNow, http.RequestAborted);
    if (result.Status == RegistryExportOpenStatus.NotFound)
    {
        return Results.NotFound();
    }

    if (result.Status == RegistryExportOpenStatus.Expired)
    {
        return Results.StatusCode(StatusCodes.Status410Gone);
    }

    var metadata = result.Metadata;
    return Results.File(
        result.Stream!,
        metadata?.ContentType ?? "application/x-ndjson",
        $"registry_{exportId}.jsonl");
}).RequireAuthorization();

app.MapGet("/api/telemetry/{deviceId}", async (
    string deviceId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    string? pointId,
    int? limit,
    ITelemetryStorageQuery query,
    TelemetryExportService exports,
    IOptions<TelemetryExportOptions> exportOptions,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var rangeEnd = to ?? DateTimeOffset.UtcNow;
    var rangeStart = from ?? rangeEnd.AddMinutes(-15);
    var request = new TelemetryQueryRequest(tenant, deviceId, rangeStart, rangeEnd, pointId, limit);
    var results = await query.QueryAsync(request, http.RequestAborted);
    if (results.Count == 0)
    {
        return Results.Ok(TelemetryQueryResponse.Inline(results));
    }

    var maxInline = Math.Max(1, exportOptions.Value.MaxInlineRecords);
    if (results.Count <= maxInline)
    {
        return Results.Ok(TelemetryQueryResponse.Inline(results));
    }

    var export = await exports.CreateExportAsync(request, results, http.RequestAborted);
    return Results.Ok(TelemetryQueryResponse.UrlResult(export.Url, export.ExpiresAt, export.Count));
}).RequireAuthorization();

app.MapGet("/api/telemetry/exports/{exportId}", async (
    string exportId,
    TelemetryExportService exports,
    HttpContext http) =>
{
    return await TelemetryExportEndpoint.HandleOpenExportAsync(exportId, exports, http, DateTimeOffset.UtcNow);
}).RequireAuthorization();

var sparqlGroup = app.MapGroup("/api/sparql").RequireAuthorization();

sparqlGroup.MapPost("/query", async (
    SparqlQueryRequest request,
    IClusterClient client,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { Message = "query is required." });
    }

    var tenant = TenantResolver.ResolveTenant(http);
    var grain = client.GetGrain<ISparqlQueryGrain>("sparql");
    var result = await grain.ExecuteQueryAsync(request.Query, tenant);
    return Results.Ok(result);
});

sparqlGroup.MapPost("/load", async (
    SparqlLoadRequest request,
    IClusterClient client,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { Message = "content is required." });
    }

    if (string.IsNullOrWhiteSpace(request.Format))
    {
        return Results.BadRequest(new { Message = "format is required." });
    }

    var tenant = TenantResolver.ResolveTenant(http);
    var grain = client.GetGrain<ISparqlQueryGrain>("sparql");
    await grain.LoadRdfAsync(request.Content, request.Format, tenant);
    return Results.Ok();
});

sparqlGroup.MapGet("/stats", async (
    IClusterClient client,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grain = client.GetGrain<ISparqlQueryGrain>("sparql");
    var count = await grain.GetTripleCountAsync(tenant);
    return Results.Ok(new SparqlStatsResponse(count));
});

// gRPC endpoints
if (grpcEnabled)
{
    app.MapGrpcService<DeviceService>().RequireAuthorization();
    app.MapGrpcService<RegistryGrpcService>().RequireAuthorization();
}

app.Run();

public partial class Program { }
