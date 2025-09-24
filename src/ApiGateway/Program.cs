using ApiGateway.Infrastructure;
using ApiGateway.Services;
using Grains.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Orleans;

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

// Configure gRPC
builder.Services.AddGrpc();

// Swagger for convenience
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Orleans client
builder.Host.UseOrleansClient(client =>
{
    client.UseStaticClustering(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11111));
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

// gRPC endpoints
app.MapGrpcService<DeviceService>().RequireAuthorization();

app.Run();
