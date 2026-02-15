using Grains.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Streams;
using System.Collections.Concurrent;

namespace AdminGateway.Hubs;

/// <summary>
/// SignalR hub for real-time telemetry updates.
/// Subscribes to Orleans PointUpdates stream and pushes point changes to connected clients.
/// </summary>
public sealed class TelemetryHub : Hub
{
    private readonly IClusterClient _client;
    private readonly ILogger<TelemetryHub> _logger;
    private static readonly ConcurrentDictionary<string, StreamSubscriptionHandle<PointSnapshot>> Subscriptions = new();

    public TelemetryHub(IClusterClient client, ILogger<TelemetryHub> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to telemetry updates for a specific point.
    /// Client will receive "ReceivePointUpdate" messages when the point value changes.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="deviceId">Device ID</param>
    /// <param name="pointId">Point ID</param>
    public async Task SubscribeToPoint(string tenantId, string deviceId, string pointId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(pointId))
        {
            _logger.LogWarning("Invalid subscription request: tenantId={TenantId}, deviceId={DeviceId}, pointId={PointId}",
                tenantId, deviceId, pointId);
            return;
        }

        try
        {
            var pointKey = PointGrainKey.Create(tenantId, pointId);
            var streamProvider = _client.GetStreamProvider("PointUpdates");
            var stream = streamProvider.GetStream<PointSnapshot>(StreamId.Create("PointUpdatesNs", pointKey));

            var subscriptionKey = $"{Context.ConnectionId}:{tenantId}:{deviceId}:{pointId}";

            // Keep one active point subscription per SignalR connection.
            await UnsubscribeAllForConnectionAsync(Context.ConnectionId);

            // Subscribe to stream
            var subscription = await stream.SubscribeAsync(async (snapshot, token) =>
            {
                await Clients.Caller.SendAsync("ReceivePointUpdate", new
                {
                    Timestamp = snapshot.UpdatedAt,
                    Value = snapshot.LatestValue,
                    PointId = pointId,
                    DeviceId = deviceId,
                    TenantId = tenantId
                }, token);
            });

            Subscriptions[subscriptionKey] = subscription;

            _logger.LogInformation("Client {ConnectionId} subscribed to {TenantId}/{DeviceId}/{PointId}",
                Context.ConnectionId, tenantId, deviceId, pointId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to point {TenantId}/{DeviceId}/{PointId}",
                tenantId, deviceId, pointId);
            throw;
        }
    }

    /// <summary>
    /// Unsubscribe from telemetry updates.
    /// </summary>
    public async Task Unsubscribe()
    {
        var subscriptionsToRemove = await UnsubscribeAllForConnectionAsync(Context.ConnectionId);

        _logger.LogInformation("Client {ConnectionId} unsubscribed from {Count} streams",
            Context.ConnectionId, subscriptionsToRemove.Count);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Clean up subscriptions when client disconnects
        await Unsubscribe();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<List<string>> UnsubscribeAllForConnectionAsync(string connectionId)
    {
        var subscriptionsToRemove = Subscriptions
            .Where(kvp => kvp.Key.StartsWith(connectionId + ":", StringComparison.Ordinal))
            .ToList();

        foreach (var (key, subscription) in subscriptionsToRemove)
        {
            try
            {
                await subscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe {Key}", key);
            }
            finally
            {
                Subscriptions.TryRemove(key, out _);
            }
        }

        return subscriptionsToRemove.Select(item => item.Key).ToList();
    }
}
