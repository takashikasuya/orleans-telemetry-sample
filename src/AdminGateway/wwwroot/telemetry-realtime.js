// Telemetry real-time updates via SignalR
let telemetryConnection = null;

window.subscribeToPointUpdates = async (tenantId, deviceId, pointId, dotNetHelper) => {
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
            dotNetHelper.invokeMethodAsync('OnPointUpdate', update)
                .catch(err => console.error('Failed to invoke OnPointUpdate:', err));
        });

        // Connection lifecycle events
        telemetryConnection.onreconnecting((error) => {
            console.warn(`Telemetry connection reconnecting: ${error}`);
        });

        telemetryConnection.onreconnected((connectionId) => {
            console.info(`Telemetry connection reconnected: ${connectionId}`);
            // Resubscribe after reconnection
            telemetryConnection.invoke("SubscribeToPoint", tenantId, deviceId, pointId)
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
        await telemetryConnection.invoke("SubscribeToPoint", tenantId, deviceId, pointId);
        console.info(`Subscribed to telemetry updates: ${tenantId}/${deviceId}/${pointId}`);

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
