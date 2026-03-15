using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace AdminGateway.Services;

internal sealed class AdminClientConnectionRetryFilter : IClientConnectionRetryFilter
{
    private readonly ILogger<AdminClientConnectionRetryFilter> _logger;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private int _attempt;

    public AdminClientConnectionRetryFilter(
        ILogger<AdminClientConnectionRetryFilter> logger,
        TimeSpan initialDelay,
        TimeSpan maxDelay)
    {
        _logger = logger;
        _initialDelay = initialDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : initialDelay;
        _maxDelay = maxDelay < _initialDelay ? _initialDelay : maxDelay;
    }

    public async Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _attempt);
        var delay = ComputeDelay(attempt);

        _logger.LogWarning(
            exception,
            "Orleans client connection attempt {Attempt} failed. Retrying in {DelaySeconds:F1}s.",
            attempt,
            delay.TotalSeconds);

        await Task.Delay(delay, cancellationToken);
        return true;
    }

    internal TimeSpan ComputeDelay(int attempt)
    {
        if (attempt <= 1)
        {
            return _initialDelay;
        }

        var multiplier = Math.Pow(2, attempt - 1);
        var seconds = _initialDelay.TotalSeconds * multiplier;
        var bounded = Math.Min(seconds, _maxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(bounded);
    }
}
