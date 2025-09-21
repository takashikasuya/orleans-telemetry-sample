using System.Security.Claims;
using Grains.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// Configure gRPC
builder.Services.AddGrpc();

// Configure Orleans client
builder.Host.UseOrleansClient(client =>
{
    client.UseStaticClustering(options =>
    {
        options.Gateways = new[] { new Orleans.Runtime.OrleansUrlGateway(11111) };
    });
    client.Configure<Orleans.Runtime.ClusterOptions>(opts =>
    {
        opts.ClusterId = "telemetry-cluster";
        opts.ServiceId = "telemetry-service";
    });
    client.AddSimpleMessageStreamProvider("DeviceUpdates");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// REST endpoint to fetch device snapshot
app.MapGet("/api/devices/{deviceId}", async (string deviceId, IGrainFactory grains, HttpContext http) =>
{
    // Extract tenant claim; fallback to t1
    var tenant = http.User.FindFirst("tenant")?.Value ?? "t1";
    var key = $"{tenant}:{deviceId}";
    var grain = grains.GetGrain<IDeviceGrain>(key);
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

// Swagger for convenience
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

// gRPC service implementation
public sealed class DeviceService : devices.v1.DeviceService.DeviceServiceBase
{
    private readonly IGrainFactory _grains;
    private readonly IClusterClient _client;
    private readonly IHttpContextAccessor _contextAccessor;
    public DeviceService(IGrainFactory grains, IClusterClient client, IHttpContextAccessor contextAccessor)
    {
        _grains = grains;
        _client = client;
        _contextAccessor = contextAccessor;
    }
    public override async Task<devices.v1.Snapshot> GetSnapshot(devices.v1.DeviceKey request, Grpc.Core.ServerCallContext context)
    {
        var tenant = _contextAccessor.HttpContext?.User.FindFirst("tenant")?.Value ?? "t1";
        var grain = _grains.GetGrain<IDeviceGrain>($"{tenant}:{request.DeviceId}");
        var snap = await grain.GetAsync();
        var proto = new devices.v1.Snapshot
        {
            DeviceId = request.DeviceId,
            LastSequence = snap.LastSequence,
            UpdatedAtIso = snap.UpdatedAt.ToUniversalTime().ToString("O")
        };
        proto.Properties.Add(snap.LatestProps.Select(kv => new devices.v1.Property { Key = kv.Key, ValueJson = System.Text.Json.JsonSerializer.Serialize(kv.Value) }));
        return proto;
    }
    public override async Task StreamUpdates(devices.v1.DeviceKey request, IAsyncStreamWriter<devices.v1.Snapshot> responseStream, Grpc.Core.ServerCallContext context)
    {
        var tenant = _contextAccessor.HttpContext?.User.FindFirst("tenant")?.Value ?? "t1";
        var streamProvider = _client.GetStreamProvider("DeviceUpdates");
        var stream = streamProvider.GetStream<DeviceSnapshot>(Orleans.Streams.StreamId.Create("DeviceUpdatesNs", $"{tenant}:{request.DeviceId}"));
        var grain = _grains.GetGrain<IDeviceGrain>($"{tenant}:{request.DeviceId}");
        // send initial snapshot
        var init = await grain.GetAsync();
        await responseStream.WriteAsync(ToProto(request.DeviceId, init));
        // subscribe to updates
        var channel = Channel.CreateUnbounded<DeviceSnapshot>();
        var subscription = await stream.SubscribeAsync((snapshot, _) =>
        {
            channel.Writer.TryWrite(snapshot);
            return Task.CompletedTask;
        });
        await foreach (var snap in channel.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(ToProto(request.DeviceId, snap));
        }
    }
    private static devices.v1.Snapshot ToProto(string deviceId, DeviceSnapshot s)
    {
        var proto = new devices.v1.Snapshot
        {
            DeviceId = deviceId,
            LastSequence = s.LastSequence,
            UpdatedAtIso = s.UpdatedAt.ToUniversalTime().ToString("O")
        };
        proto.Properties.Add(s.LatestProps.Select(kv => new devices.v1.Property { Key = kv.Key, ValueJson = System.Text.Json.JsonSerializer.Serialize(kv.Value) }));
        return proto;
    }
}

namespace devices.v1
{
    // gRPC contract definitions (generated normally by tooling but inlined for brevity)
    public class DeviceKey
    {
        public string DeviceId { get; set; } = "";
    }
    public class Property
    {
        public string Key { get; set; } = "";
        public string ValueJson { get; set; } = "";
    }
    public class Snapshot
    {
        public string DeviceId { get; set; } = "";
        public long LastSequence { get; set; }
        public string UpdatedAtIso { get; set; } = "";
        public List<Property> Properties { get; } = new();
    }
    public abstract class DeviceService
    {
        public abstract class DeviceServiceBase
        {
            public virtual Task<Snapshot> GetSnapshot(DeviceKey request, Grpc.Core.ServerCallContext context) => throw new NotImplementedException();
            public virtual Task StreamUpdates(DeviceKey request, IAsyncStreamWriter<Snapshot> responseStream, Grpc.Core.ServerCallContext context) => throw new NotImplementedException();
        }
    }
}
