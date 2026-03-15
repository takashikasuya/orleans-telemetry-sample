using System;
using Publisher;
using Xunit;

namespace Publisher.Tests;

public class ProgramReconnectTests
{
    [Fact]
    public void ComputeReconnectDelay_UsesInitialDelayOnFirstAttempt()
    {
        var delay = Program.ComputeReconnectDelay(attempt: 0, initialMs: 1000, maxMs: 30000);

        Assert.Equal(TimeSpan.FromMilliseconds(1000), delay);
    }

    [Fact]
    public void ComputeReconnectDelay_UsesExponentialBackoffUntilMax()
    {
        var secondAttempt = Program.ComputeReconnectDelay(attempt: 1, initialMs: 1000, maxMs: 30000);
        var cappedAttempt = Program.ComputeReconnectDelay(attempt: 6, initialMs: 1000, maxMs: 30000);

        Assert.Equal(TimeSpan.FromMilliseconds(2000), secondAttempt);
        Assert.Equal(TimeSpan.FromMilliseconds(30000), cappedAttempt);
    }

    [Fact]
    public void ComputeReconnectDelay_ClampsTooSmallInitialDelay()
    {
        var delay = Program.ComputeReconnectDelay(attempt: 0, initialMs: 1, maxMs: 10);

        Assert.Equal(TimeSpan.FromMilliseconds(250), delay);
    }
}
