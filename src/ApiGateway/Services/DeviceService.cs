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

public sealed class DeviceService : Devices.V1.DeviceService.DeviceServiceBase
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
        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "device_id is required."));
        }

        try
        {
            var httpContext = _contextAccessor.HttpContext;
            var tenant = TenantResolver.ResolveTenant(httpContext);
            var grainKey = DeviceGrainKey.Create(tenant, request.DeviceId);
            var grain = _client.GetGrain<IDeviceGrain>(grainKey);
            var snapshot = await grain.GetAsync().WaitAsync(context.CancellationToken);
            return DeviceSnapshotMapper.ToGrpc(request.DeviceId, snapshot);
        }
        catch (OperationCanceledException)
        {
            throw CreateCancellationException(context, "request");
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, $"failed to get snapshot: {ex.Message}"));
        }
    }

    public override async Task StreamUpdates(DeviceKey request, IServerStreamWriter<Snapshot> responseStream, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "device_id is required."));
        }

        var channel = Channel.CreateUnbounded<DeviceSnapshot>();
        StreamSubscriptionHandle<DeviceSnapshot>? handle = null;

        try
        {
            var httpContext = _contextAccessor.HttpContext;
            var tenant = TenantResolver.ResolveTenant(httpContext);
            var grainKey = DeviceGrainKey.Create(tenant, request.DeviceId);
            var streamProvider = _client.GetStreamProvider("DeviceUpdates");
            var stream = streamProvider.GetStream<DeviceSnapshot>(StreamId.Create("DeviceUpdatesNs", grainKey));
            var grain = _client.GetGrain<IDeviceGrain>(grainKey);

            var initial = await grain.GetAsync().WaitAsync(context.CancellationToken);
            await responseStream.WriteAsync(DeviceSnapshotMapper.ToGrpc(request.DeviceId, initial)).WaitAsync(context.CancellationToken);

            handle = await stream.SubscribeAsync(
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
                }).WaitAsync(context.CancellationToken);

            using var cancellationRegistration = context.CancellationToken.Register(() => channel.Writer.TryComplete());

            await foreach (var snapshot in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(DeviceSnapshotMapper.ToGrpc(request.DeviceId, snapshot)).WaitAsync(context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw CreateCancellationException(context, "stream");
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, $"failed to stream updates: {ex.Message}"));
        }
        finally
        {
            channel.Writer.TryComplete();

            if (handle is not null)
            {
                await handle.UnsubscribeAsync();
            }
        }
    }
    private static RpcException CreateCancellationException(ServerCallContext context, string operation)
    {
        var now = DateTime.UtcNow;
        if (context.Deadline != DateTime.MaxValue && now >= context.Deadline)
        {
            return new RpcException(new Status(StatusCode.DeadlineExceeded, $"{operation} timed out."));
        }

        return new RpcException(new Status(StatusCode.Cancelled, $"{operation} was cancelled."));
    }

}
