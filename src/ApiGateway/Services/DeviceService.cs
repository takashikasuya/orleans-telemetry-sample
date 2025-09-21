using System.Threading.Channels;
using ApiGateway.Infrastructure;
using ApiGateway.Telemetry;
using Devices.V1;
using Grains.Abstractions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Orleans;
using Orleans.Streams;

namespace ApiGateway.Services;

public sealed class DeviceService : DeviceServiceBase
{
    private readonly IClusterClient _client;
    private readonly IHttpContextAccessor _contextAccessor;

    public DeviceService(IClusterClient client, IHttpContextAccessor contextAccessor)
    {
        _client = client;
        _contextAccessor = contextAccessor;
    }

    public override async Task<Snapshot> GetSnapshot(DeviceKey request, ServerCallContext context)
    {
        var httpContext = _contextAccessor.HttpContext;
        var tenant = TenantResolver.ResolveTenant(httpContext);
        var grainKey = DeviceGrainKey.Create(tenant, request.DeviceId);
        var grain = _client.GetGrain<IDeviceGrain>(grainKey);
        var snapshot = await grain.GetAsync();
        return DeviceSnapshotMapper.ToGrpc(request.DeviceId, snapshot);
    }

    public override async Task StreamUpdates(DeviceKey request, IServerStreamWriter<Snapshot> responseStream, ServerCallContext context)
    {
        var httpContext = _contextAccessor.HttpContext;
        var tenant = TenantResolver.ResolveTenant(httpContext);
        var grainKey = DeviceGrainKey.Create(tenant, request.DeviceId);
        var streamProvider = _client.GetStreamProvider("DeviceUpdates");
        var stream = streamProvider.GetStream<DeviceSnapshot>(StreamId.Create("DeviceUpdatesNs", grainKey));
        var grain = _client.GetGrain<IDeviceGrain>(grainKey);

        var initial = await grain.GetAsync();
        await responseStream.WriteAsync(DeviceSnapshotMapper.ToGrpc(request.DeviceId, initial));

        var channel = Channel.CreateUnbounded<DeviceSnapshot>();
        var handle = await stream.SubscribeAsync(
            (snapshot, _) =>
            {
                channel.Writer.TryWrite(snapshot);
                return Task.CompletedTask;
            },
            ex =>
            {
                channel.Writer.TryComplete(ex);
                return Task.CompletedTask;
            },
            () =>
            {
                channel.Writer.TryComplete();
                return Task.CompletedTask;
            });

        await using var subscription = new StreamSubscriptionScope<DeviceSnapshot>(handle);
        using var cancellationRegistration = context.CancellationToken.Register(() => channel.Writer.TryComplete());

        try
        {
            await foreach (var snapshot in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(DeviceSnapshotMapper.ToGrpc(request.DeviceId, snapshot));
            }
        }
        catch (OperationCanceledException)
        {
            // Swallow cancellation exceptions so gRPC can complete gracefully.
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }

    private sealed class StreamSubscriptionScope<T> : IAsyncDisposable
    {
        private readonly StreamSubscriptionHandle<T> _handle;

        public StreamSubscriptionScope(StreamSubscriptionHandle<T> handle)
        {
            _handle = handle;
        }

        public ValueTask DisposeAsync() => new(_handle.UnsubscribeAsync());
    }
}
