using AdminGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdminGateway.Tests;

public sealed class AdminClientConnectionRetryFilterTests
{
    [Fact]
    public void ComputeDelay_UsesExponentialBackoffAndCapsAtMax()
    {
        var filter = new AdminClientConnectionRetryFilter(
            NullLogger<AdminClientConnectionRetryFilter>.Instance,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(2), filter.ComputeDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), filter.ComputeDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(8), filter.ComputeDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), filter.ComputeDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(10), filter.ComputeDelay(10));
    }

    [Fact]
    public async Task ShouldRetryConnectionAttempt_ReturnsTrueAfterDelay()
    {
        var filter = new AdminClientConnectionRetryFilter(
            NullLogger<AdminClientConnectionRetryFilter>.Instance,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(5));

        var result = await filter.ShouldRetryConnectionAttempt(new InvalidOperationException("test"), CancellationToken.None);

        Assert.True(result);
    }
}
