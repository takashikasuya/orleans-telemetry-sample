using ApiGateway.Telemetry;
using FluentAssertions;
using Telemetry.Storage;
using Xunit;

namespace ApiGateway.Tests;

public sealed class TelemetryInlineResponsePolicyTests
{
    [Fact]
    public void ShouldReturnInline_WhenEstimatedPayloadIsWithinLimit_ReturnsTrue()
    {
        var options = new TelemetryExportOptions { MaxInlineBytes = 4096 };
        var results = CreateResults(2, payloadSize: 100);

        var actual = TelemetryInlineResponsePolicy.ShouldReturnInline(results, options);

        actual.Should().BeTrue();
    }

    [Fact]
    public void ShouldReturnInline_WhenEstimatedPayloadExceedsLimit_ReturnsFalse()
    {
        var options = new TelemetryExportOptions { MaxInlineBytes = 1024 };
        var results = CreateResults(5, payloadSize: 500);

        var actual = TelemetryInlineResponsePolicy.ShouldReturnInline(results, options);

        actual.Should().BeFalse();
    }

    [Fact]
    public void ShouldReturnInline_WhenMaxInlineBytesIsZero_UsesMinimumByteLimit()
    {
        var options = new TelemetryExportOptions { MaxInlineBytes = 0 };
        var results = CreateResults(1, payloadSize: 100);

        var actual = TelemetryInlineResponsePolicy.ShouldReturnInline(results, options);

        actual.Should().BeFalse();
    }

    private static IReadOnlyList<TelemetryQueryResult> CreateResults(int count, int payloadSize)
    {
        var items = new List<TelemetryQueryResult>(count);
        var payload = new string('x', payloadSize);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            items.Add(new TelemetryQueryResult(
                "t1",
                "d1",
                $"p{i}",
                now,
                i,
                payload,
                payload,
                null));
        }

        return items;
    }
}
