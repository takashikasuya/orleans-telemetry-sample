using System.Threading.Channels;
using Grains.Abstractions;

namespace Telemetry.Ingest;

public interface ITelemetryIngestConnector
{
    string Name { get; }

    Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct);
}
