using System.Net;
using ApiGateway.Infrastructure;
using ApiGateway.Services;
using Grains.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
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

// Configure gRPC
builder.Services.AddGrpc();

// Swagger for convenience
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Orleans client
var orleansHost = builder.Configuration["Orleans:GatewayHost"] ?? "127.0.0.1";
var orleansPort = int.TryParse(builder.Configuration["Orleans:GatewayPort"], out var parsedPort) ? parsedPort : 30000;
var orleansAddresses = Dns.GetHostAddresses(orleansHost);
var orleansAddress = orleansAddresses.Length > 0 ? orleansAddresses[0] : IPAddress.Loopback;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// REST endpoint to fetch device snapshot
app.MapGet("/api/devices/{deviceId}", async (string deviceId, IClusterClient client, HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grainKey = DeviceGrainKey.Create(tenant, deviceId);
    var grain = client.GetGrain<IDeviceGrain>(grainKey);
    var snap = await grain.GetAsync();
    http.Response.Headers.ETag = $"W/\"{snap.LastSequence}\"";
    return Results.Ok(new
    {
        deviceId,
        snap.LastSequence,
        snap.UpdatedAt,
        Properties = snap.LatestProps
    });
}).RequireAuthorization();


// Graph endpoints for node metadata and value bindings
app.MapGet("/api/nodes/{nodeId}", async (string nodeId, IClusterClient client, HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var grainKey = GraphNodeKey.Create(tenant, nodeId);
    var grain = client.GetGrain<IGraphNodeGrain>(grainKey);
    var snap = await grain.GetAsync();
    return Results.Ok(snap);
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

    snap.Node.Attributes.TryGetValue("BuildingName", out var buildingName);
    snap.Node.Attributes.TryGetValue("SpaceId", out var spaceId);
    snap.Node.Attributes.TryGetValue("DeviceId", out var deviceId);

    if (string.IsNullOrWhiteSpace(deviceId))
    {
        return Results.NotFound(new { Message = "DeviceId attribute is missing for the requested node." });
    }

    var pointKey = PointGrainKey.Create(
        tenant,
        buildingName ?? string.Empty,
        spaceId ?? string.Empty,
        deviceId,
        pointId);
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

app.MapGet("/api/telemetry/{deviceId}", async (
    string deviceId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    string? pointId,
    int? limit,
    ITelemetryStorageQuery query,
    HttpContext http) =>
{
    var tenant = TenantResolver.ResolveTenant(http);
    var rangeEnd = to ?? DateTimeOffset.UtcNow;
    var rangeStart = from ?? rangeEnd.AddMinutes(-15);
    var request = new TelemetryQueryRequest(tenant, deviceId, rangeStart, rangeEnd, pointId, limit);
    var results = await query.QueryAsync(request, http.RequestAborted);
    return Results.Ok(results);
}).RequireAuthorization();

// gRPC endpoints
app.MapGrpcService<DeviceService>().RequireAuthorization();

app.Run();

public partial class Program { }
