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
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly ILogger<TelemetryHub> _logger;
    private static readonly ConcurrentDictionary<string, StreamSubscriptionHandle<PointSnapshot>> Subscriptions = new();

    public TelemetryHub(
        IClusterClient client,
        IHubContext<TelemetryHub> hubContext,
        ILogger<TelemetryHub> logger)
    {
        _client = client;
        _hubContext = hubContext;
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
            var connectionId = Context.ConnectionId;
            var pointKey = PointGrainKey.Create(tenantId, pointId);
            var streamProvider = _client.GetStreamProvider("PointUpdates");
            var stream = streamProvider.GetStream<PointSnapshot>(StreamId.Create("PointUpdatesNs", pointKey));

            var subscriptionKey = $"{connectionId}:{tenantId}:{deviceId}:{pointId}";

            if (Subscriptions.ContainsKey(subscriptionKey))
            {
                _logger.LogDebug("Client {ConnectionId} already subscribed to {TenantId}/{DeviceId}/{PointId}",
                    Context.ConnectionId, tenantId, deviceId, pointId);
                return;
            }

            // Subscribe to stream
            var subscription = await stream.SubscribeAsync(async (snapshot, token) =>
            {
                // Normalize the value to a numeric value for charting
                var numericValue = NormalizeValue(snapshot.LatestValue);

                await _hubContext.Clients.Client(connectionId).SendAsync("ReceivePointUpdate", new
                {
                    Timestamp = snapshot.UpdatedAt,
                    Value = numericValue,
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
    /// Subscribe to multiple points in one request.
    /// Existing subscriptions are preserved; duplicate entries are ignored.
    /// </summary>
    /// <param name="subscriptions">Point subscription targets.</param>
    public async Task SubscribeToPoints(IReadOnlyList<PointSubscriptionRequest>? subscriptions)
    {
        if (subscriptions is null || subscriptions.Count == 0)
        {
            return;
        }

        foreach (var item in subscriptions)
        {
            await SubscribeToPoint(item.TenantId, item.DeviceId, item.PointId);
        }
    }

    /// <summary>
    /// Subscribe to multiple points using compact string keys.
    /// Key format: "tenantId|deviceId|pointId".
    /// </summary>
    /// <param name="pointKeys">Point keys.</param>
    public async Task SubscribeToPointKeys(IReadOnlyList<string>? pointKeys)
    {
        if (pointKeys is null || pointKeys.Count == 0)
        {
            return;
        }

        foreach (var key in pointKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var parts = key.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
            {
                _logger.LogWarning("Invalid point key for realtime subscription: {PointKey}", key);
                continue;
            }

            await SubscribeToPoint(parts[0], parts[1], parts[2]);
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

    /// <summary>
    /// Normalize a value to a numeric double for charting.
    /// Handles various input types: bool, numeric types, IConvertible, null.
    /// </summary>
    private static double? NormalizeValue(object? value)
    {
        if (value is null) return null;
        if (value is bool b) return b ? 1.0 : 0.0;
        
        // Try to convert to double
        if (value is IConvertible convertible)
        {
            try
            {
                return Convert.ToDouble(convertible);
            }
            catch
            {
                // Conversion failed, return null
                return null;
            }
        }
        
        // Last resort: try parsing as string
        if (value is string str && double.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        
        return null;
    }

    public sealed record PointSubscriptionRequest(string TenantId, string DeviceId, string PointId);
}
