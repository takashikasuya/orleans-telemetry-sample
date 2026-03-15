using Grains.Abstractions;

namespace Telemetry.Ingest;

public interface ITelemetryPointRegistrationFilter
{
    Task<bool> IsRegisteredAsync(TelemetryPointMsg message, CancellationToken cancellationToken);
}

public sealed class AllowAllTelemetryPointRegistrationFilter : ITelemetryPointRegistrationFilter
{
    public Task<bool> IsRegisteredAsync(TelemetryPointMsg message, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
