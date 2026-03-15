// Telemetry real-time updates via SignalR
let telemetryConnection = null;

function normalizeSubscriptions(subscriptions) {
    if (!Array.isArray(subscriptions)) {
        return [];
    }

    // Build a plain JSON-safe payload for SignalR invocation.
    return subscriptions
        .map(item => {
            const tenantId = item?.tenantId ?? item?.TenantId;
            const deviceId = item?.deviceId ?? item?.DeviceId;
            const pointId = item?.pointId ?? item?.PointId;

            if (!tenantId || !deviceId || !pointId) {
                return null;
            }

            return {
                tenantId: String(tenantId),
                deviceId: String(deviceId),
                pointId: String(pointId)
            };
        })
        .filter(item => item !== null);
}

function toPointKeys(subscriptions) {
    return subscriptions.map(item => `${item.tenantId}|${item.deviceId}|${item.pointId}`);
}

window.subscribeToPointUpdates = async (subscriptions, dotNetHelper) => {
    const normalizedSubscriptions = normalizeSubscriptions(subscriptions);
    const pointKeys = toPointKeys(normalizedSubscriptions);

    if (!pointKeys.length) {
        throw new Error("No telemetry subscriptions were provided.");
    }

    if (!dotNetHelper || typeof dotNetHelper.invokeMethodAsync !== "function") {
        throw new Error("Invalid .NET callback reference for realtime updates.");
    }

    // Stop existing connection if any
    if (telemetryConnection) {
        try {
            await telemetryConnection.stop();
        } catch (e) {
            console.warn('Failed to stop previous connection:', e);
        }
        telemetryConnection = null;
    }

    try {
        // Create new connection
        telemetryConnection = new signalR.HubConnectionBuilder()
            .withUrl("/telemetryHub")
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Handle incoming point updates
        telemetryConnection.on("ReceivePointUpdate", (update) => {
            console.log("[telemetry-realtime.js] ReceivePointUpdate received:", update);
            dotNetHelper.invokeMethodAsync('OnPointUpdate', update)
                .then(() => console.log("[telemetry-realtime.js] OnPointUpdate invoked successfully"))
                .catch(err => console.error('[telemetry-realtime.js] Failed to invoke OnPointUpdate:', err));
        });

        // Connection lifecycle events
        telemetryConnection.onreconnecting((error) => {
            console.warn(`Telemetry connection reconnecting: ${error}`);
        });

        telemetryConnection.onreconnected((connectionId) => {
            console.info(`Telemetry connection reconnected: ${connectionId}`);
            // Resubscribe all points after reconnection
            telemetryConnection.invoke("SubscribeToPointKeys", pointKeys)
                .catch(err => console.error('Failed to resubscribe after reconnection:', err));
        });

        telemetryConnection.onclose((error) => {
            console.warn('Telemetry connection closed:', error);
            telemetryConnection = null;
        });

        // Start connection
        await telemetryConnection.start();
        console.info('Telemetry SignalR connection established');

        // Subscribe to point updates
        await telemetryConnection.invoke("SubscribeToPointKeys", pointKeys);
        console.info(`Subscribed to ${pointKeys.length} telemetry point(s)`);

    } catch (error) {
        console.error('Failed to subscribe to telemetry updates:', error);
        telemetryConnection = null;
        throw error;
    }
};

window.unsubscribeFromPointUpdates = async () => {
    if (telemetryConnection) {
        try {
            await telemetryConnection.invoke("Unsubscribe");
            await telemetryConnection.stop();
            console.info('Unsubscribed from telemetry updates');
        } catch (error) {
            console.warn('Failed to unsubscribe gracefully:', error);
        } finally {
            telemetryConnection = null;
        }
    }
};
