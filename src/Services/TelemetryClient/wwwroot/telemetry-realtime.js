// SignalR real-time telemetry updates for TelemetryClient
// Manages connection to TelemetryHub and point subscriptions

let connection = null;
let subscriptions = new Map();
let reconnectAttempts = 0;
const MAX_RECONNECT_ATTEMPTS = 10;

// Initialize SignalR connection
window.initializeTelemetryRealtime = async function () {
    if (connection) {
        console.log('TelemetryHub connection already initialized');
        return;
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl('/telemetryHub')
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: retryContext => {
                if (retryContext.previousRetryCount >= MAX_RECONNECT_ATTEMPTS) {
                    console.error('Maximum reconnection attempts reached');
                    return null;
                }
                // Exponential backoff: 0, 2, 10, 30 seconds
                return Math.min(1000 * Math.pow(retryContext.previousRetryCount, 2), 30000);
            }
        })
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.onreconnecting(error => {
        console.warn('TelemetryHub reconnecting...', error);
        reconnectAttempts++;
    });

    connection.onreconnected(connectionId => {
        console.log('TelemetryHub reconnected:', connectionId);
        reconnectAttempts = 0;
        // Resubscribe to all active points
        resubscribeAll();
    });

    connection.onclose(error => {
        console.error('TelemetryHub connection closed', error);
        connection = null;
    });

    // Register handler for point updates
    connection.on('ReceivePointUpdate', (update) => {
        console.log('Received point update:', update);

        // Notify subscribers
        const key = `${update.TenantId}|${update.DeviceId}|${update.PointId}`;
        const callbacks = subscriptions.get(key);
        if (callbacks) {
            callbacks.forEach(callback => {
                try {
                    callback.invokeMethodAsync('OnPointUpdate', update);
                } catch (err) {
                    console.error('Error invoking callback:', err);
                }
            });
        }
    });

    try {
        await connection.start();
        console.log('TelemetryHub connection started');
    } catch (err) {
        console.error('Error starting TelemetryHub connection:', err);
        setTimeout(() => initializeTelemetryRealtime(), 5000);
    }
};

// Subscribe to a specific point
window.subscribeToPoint = async function (tenantId, deviceId, pointId, dotNetHelper) {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
        console.warn('TelemetryHub not connected, initializing...');
        await initializeTelemetryRealtime();
    }

    const key = `${tenantId}|${deviceId}|${pointId}`;

    // Add callback to subscription map
    if (!subscriptions.has(key)) {
        subscriptions.set(key, new Set());
    }
    subscriptions.get(key).add(dotNetHelper);

    try {
        await connection.invoke('SubscribeToPoint', tenantId, deviceId, pointId);
        console.log(`Subscribed to point: ${key}`);
    } catch (err) {
        console.error('Error subscribing to point:', err);
        throw err;
    }
};

// Unsubscribe from a specific point
window.unsubscribeFromPoint = function (tenantId, deviceId, pointId, dotNetHelper) {
    const key = `${tenantId}|${deviceId}|${pointId}`;

    const callbacks = subscriptions.get(key);
    if (callbacks) {
        callbacks.delete(dotNetHelper);
        if (callbacks.size === 0) {
            subscriptions.delete(key);
        }
    }

    console.log(`Unsubscribed from point: ${key}`);
};

// Resubscribe to all active points after reconnection
async function resubscribeAll() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
        return;
    }

    console.log('Resubscribing to all active points...');

    for (const key of subscriptions.keys()) {
        const parts = key.split('|');
        if (parts.length === 3) {
            try {
                await connection.invoke('SubscribeToPoint', parts[0], parts[1], parts[2]);
                console.log(`Resubscribed to: ${key}`);
            } catch (err) {
                console.error(`Error resubscribing to ${key}:`, err);
            }
        }
    }
}

// Disconnect from TelemetryHub
window.disconnectTelemetryRealtime = async function () {
    if (connection) {
        try {
            await connection.invoke('Unsubscribe');
            await connection.stop();
            console.log('TelemetryHub disconnected');
        } catch (err) {
            console.error('Error disconnecting:', err);
        }
        connection = null;
        subscriptions.clear();
    }
};
