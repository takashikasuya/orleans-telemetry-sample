using System.Collections.Concurrent;
using System.Threading.Channels;
using FluentAssertions;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;
using Xunit;

namespace Telemetry.Ingest.Tests;

public sealed class TelemetryIngestCoordinatorTests
{
    [Fact]
    public async Task CoordinatorRoutesTelemetryFromEnabledConnector()
    {
        var options = Options.Create(new TelemetryIngestOptions
        {
            Enabled = new[] { "Fake" },
            BatchSize = 2,
            ChannelCapacity = 8
        });

        var router = new FakeRouter(expectedCount: 3);
        var connector = new FakeConnector(messageCount: 3);
        var coordinator = new TelemetryIngestCoordinator(
            new[] { connector },
            Array.Empty<ITelemetryEventSink>(),
            router,
            options,
            NullLogger<TelemetryIngestCoordinator>.Instance);

        await coordinator.StartAsync(CancellationToken.None);

        var received = await router.WaitForMessagesAsync(TimeSpan.FromSeconds(2));
        received.Should().HaveCount(3);
        received.Select(x => x.PointId).Should().BeEquivalentTo(new[] { "p1", "p2", "p3" });

        await coordinator.StopAsync(CancellationToken.None);
    }

    private sealed class FakeConnector : ITelemetryIngestConnector
    {
        private readonly int _messageCount;

        public FakeConnector(int messageCount)
        {
            _messageCount = messageCount;
        }

        public string Name => "Fake";

        public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
        {
            for (var i = 1; i <= _messageCount; i++)
            {
                await writer.WriteAsync(new TelemetryPointMsg
                {
                    TenantId = "t1",
                    BuildingName = "b1",
                    SpaceId = "s1",
                    DeviceId = "d1",
                    PointId = $"p{i}",
                    Sequence = i,
                    Timestamp = DateTimeOffset.UtcNow,
                    Value = i
                }, ct);
            }

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private sealed class FakeRouter : ITelemetryRouterGrain
    {
        private readonly ConcurrentQueue<TelemetryPointMsg> _received = new();
        private readonly TaskCompletionSource<IReadOnlyCollection<TelemetryPointMsg>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expectedCount;

        public FakeRouter(int expectedCount)
        {
            _expectedCount = expectedCount;
        }

        public Task RouteAsync(TelemetryPointMsg msg)
        {
            _received.Enqueue(msg);
            TryComplete();
            return Task.CompletedTask;
        }

        public Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch)
        {
            foreach (var msg in batch)
            {
                _received.Enqueue(msg);
            }

            TryComplete();
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyCollection<TelemetryPointMsg>> WaitForMessagesAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            using var _ = cts.Token.Register(() =>
                _completion.TrySetCanceled(cts.Token));
            return await _completion.Task;
        }

        private void TryComplete()
        {
            if (_received.Count >= _expectedCount)
            {
                _completion.TrySetResult(_received.ToArray());
            }
        }
    }
}
