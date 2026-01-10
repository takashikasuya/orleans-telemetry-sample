using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace Telemetry.Ingest.LoadTest;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task<int> Main(string[] args)
    {
        var outputDir = GetArgValue(args, "--output-dir") ?? "reports";
        var quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        var stages = quick ? BuildQuickStages() : BuildDefaultStages();

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var runStart = DateTimeOffset.UtcNow;
        var stageResults = new List<StageReport>();

        foreach (var stage in stages)
        {
            Console.WriteLine($"Stage {stage.Name} running for {stage.DurationSeconds}s...");
            var metrics = new MetricsRecorder();
            var connector = new LoadTestConnector(stage, metrics);
            var router = new SlowRouter(stage.RouterDelayMs, metrics);
            var sink = new SlowSink(stage.SinkDelayMs);

            var options = Options.Create(new TelemetryIngestOptions
            {
                Enabled = new[] { connector.Name },
                BatchSize = stage.BatchSize,
                ChannelCapacity = stage.ChannelCapacity,
                EventSinks = new TelemetryIngestEventSinkOptions
                {
                    Enabled = new[] { sink.Name }
                }
            });

            var coordinator = new TelemetryIngestCoordinator(
                new[] { connector },
                new[] { sink },
                router,
                options,
                NullLogger<TelemetryIngestCoordinator>.Instance);

            var stageStopwatch = Stopwatch.StartNew();
            await coordinator.StartAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(stage.DurationSeconds));
            await coordinator.StopAsync(CancellationToken.None);
            stageStopwatch.Stop();

            var result = metrics.CreateStageReport(stage, stageStopwatch.Elapsed);
            stageResults.Add(result);
        }

        var runEnd = DateTimeOffset.UtcNow;
        var report = new LoadTestReport(runId, runStart, runEnd, stageResults);

        Directory.CreateDirectory(outputDir);
        var mdPath = Path.Combine(outputDir, $"telemetry-ingest-backpressure-{runId}.md");
        var jsonPath = Path.Combine(outputDir, $"telemetry-ingest-backpressure-{runId}.json");

        await File.WriteAllTextAsync(mdPath, ReportFormatter.ToMarkdown(report));
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions));

        Console.WriteLine($"Report written: {mdPath}");
        Console.WriteLine($"Report written: {jsonPath}");
        return 0;
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static IReadOnlyList<LoadStage> BuildDefaultStages()
    {
        return new[]
        {
            new LoadStage(
                Name: "baseline",
                DurationSeconds: 15,
                ChannelCapacity: 200,
                BatchSize: 50,
                DeviceCount: 5,
                PointsPerDevice: 5,
                IntervalMilliseconds: 100,
                RouterDelayMs: 5,
                SinkDelayMs: 0),
            new LoadStage(
                Name: "ramp-1",
                DurationSeconds: 20,
                ChannelCapacity: 200,
                BatchSize: 50,
                DeviceCount: 20,
                PointsPerDevice: 10,
                IntervalMilliseconds: 50,
                RouterDelayMs: 10,
                SinkDelayMs: 0),
            new LoadStage(
                Name: "ramp-2",
                DurationSeconds: 20,
                ChannelCapacity: 100,
                BatchSize: 50,
                DeviceCount: 50,
                PointsPerDevice: 20,
                IntervalMilliseconds: 20,
                RouterDelayMs: 20,
                SinkDelayMs: 5),
            new LoadStage(
                Name: "overload",
                DurationSeconds: 20,
                ChannelCapacity: 50,
                BatchSize: 50,
                DeviceCount: 100,
                PointsPerDevice: 20,
                IntervalMilliseconds: 10,
                RouterDelayMs: 50,
                SinkDelayMs: 10)
        };
    }

    private static IReadOnlyList<LoadStage> BuildQuickStages()
    {
        return new[]
        {
            new LoadStage(
                Name: "baseline",
                DurationSeconds: 5,
                ChannelCapacity: 200,
                BatchSize: 50,
                DeviceCount: 5,
                PointsPerDevice: 5,
                IntervalMilliseconds: 100,
                RouterDelayMs: 5,
                SinkDelayMs: 0),
            new LoadStage(
                Name: "overload",
                DurationSeconds: 6,
                ChannelCapacity: 50,
                BatchSize: 50,
                DeviceCount: 60,
                PointsPerDevice: 20,
                IntervalMilliseconds: 10,
                RouterDelayMs: 50,
                SinkDelayMs: 10)
        };
    }
}

internal sealed record LoadStage(
    string Name,
    int DurationSeconds,
    int ChannelCapacity,
    int BatchSize,
    int DeviceCount,
    int PointsPerDevice,
    int IntervalMilliseconds,
    int RouterDelayMs,
    int SinkDelayMs);

internal sealed record LoadTestReport(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<StageReport> Stages);

internal sealed record StageReport(
    string Name,
    int DurationSeconds,
    int ChannelCapacity,
    int BatchSize,
    int DeviceCount,
    int PointsPerDevice,
    int IntervalMilliseconds,
    int RouterDelayMs,
    int SinkDelayMs,
    string ConnectorName,
    string RouterName,
    string SinkName,
    double ObservedDurationSeconds,
    long ProducedCount,
    long RoutedCount,
    long RoutedBatches,
    double ProducedPerSecond,
    double RoutedPerSecond,
    double WriteWaitAverageMs,
    double WriteWaitP95Ms,
    double WriteWaitMaxMs,
    double WriteWaitBlockedPercent,
    IReadOnlyList<long> WriteWaitHistogramMs,
    IReadOnlyList<long> WriteWaitHistogramCounts,
    string SampleMessageJson);

internal static class ReportFormatter
{
    public static string ToMarkdown(LoadTestReport report)
    {
        var lines = new List<string>
        {
            "# Telemetry Ingest Back Pressure Report",
            string.Empty,
            $"RunId: {report.RunId}",
            $"StartedAt: {report.StartedAt:O}",
            $"CompletedAt: {report.CompletedAt:O}",
            string.Empty,
            "| Stage | Duration(s) | Produced | Routed | Produced/s | Routed/s | WaitAvg(ms) | WaitP95(ms) | WaitMax(ms) | Blocked(%) |",
            "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
        };

        foreach (var stage in report.Stages)
        {
            lines.Add(
                $"| {stage.Name} | {stage.ObservedDurationSeconds:F1} | {stage.ProducedCount} | {stage.RoutedCount} | " +
                $"{stage.ProducedPerSecond:F1} | {stage.RoutedPerSecond:F1} | {stage.WriteWaitAverageMs:F2} | " +
                $"{stage.WriteWaitP95Ms:F2} | {stage.WriteWaitMaxMs:F2} | {stage.WriteWaitBlockedPercent:F1} |");
        }

        lines.Add(string.Empty);
        lines.Add("## Stage Configurations");
        lines.Add(string.Empty);

        foreach (var stage in report.Stages)
        {
            lines.Add($"### {stage.Name}");
            lines.Add(
                $"ChannelCapacity={stage.ChannelCapacity}, BatchSize={stage.BatchSize}, " +
                $"Devices={stage.DeviceCount}, PointsPerDevice={stage.PointsPerDevice}, " +
                $"IntervalMs={stage.IntervalMilliseconds}, RouterDelayMs={stage.RouterDelayMs}, " +
                $"SinkDelayMs={stage.SinkDelayMs}");
            lines.Add(string.Empty);
        }

        lines.Add("## Appendix");
        lines.Add(string.Empty);
        foreach (var stage in report.Stages)
        {
            lines.Add($"### {stage.Name} - Connector Details");
            lines.Add(
                $"Config: ChannelCapacity={stage.ChannelCapacity}, BatchSize={stage.BatchSize}, " +
                $"Devices={stage.DeviceCount}, PointsPerDevice={stage.PointsPerDevice}, " +
                $"IntervalMs={stage.IntervalMilliseconds}, RouterDelayMs={stage.RouterDelayMs}, " +
                $"SinkDelayMs={stage.SinkDelayMs}");
            lines.Add($"Connector: {stage.ConnectorName}");
            lines.Add($"Router: {stage.RouterName}");
            lines.Add($"Sink: {stage.SinkName}");
            lines.Add("Sample message:");
            lines.Add("```json");
            lines.Add(stage.SampleMessageJson);
            lines.Add("```");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class MetricsRecorder
{
    private static readonly JsonSerializerOptions SampleJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly long[] WaitHistogramEdgesMs =
        { 0, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };

    private readonly long[] _waitHistogramCounts = new long[WaitHistogramEdgesMs.Length + 1];
    private long _producedCount;
    private long _routedCount;
    private long _routedBatches;
    private long _writeWaitTicksSum;
    private long _writeWaitTicksMax;
    private long _writeBlockedCount;
    private string? _sampleMessageJson;

    public void RecordProduced(long waitTicks)
    {
        Interlocked.Increment(ref _producedCount);
        Interlocked.Add(ref _writeWaitTicksSum, waitTicks);
        UpdateMax(ref _writeWaitTicksMax, waitTicks);
        if (waitTicks > 0)
        {
            Interlocked.Increment(ref _writeBlockedCount);
        }

        var waitMs = ToMilliseconds(waitTicks);
        var index = GetWaitBucketIndex(waitMs);
        Interlocked.Increment(ref _waitHistogramCounts[index]);
    }

    public void TryRecordSample(TelemetryPointMsg msg)
    {
        if (Volatile.Read(ref _sampleMessageJson) is not null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(msg, SampleJsonOptions);
        Interlocked.CompareExchange(ref _sampleMessageJson, json, null);
    }

    public void RecordRouted(int batchCount)
    {
        Interlocked.Add(ref _routedCount, batchCount);
        Interlocked.Increment(ref _routedBatches);
    }

    public StageReport CreateStageReport(LoadStage stage, TimeSpan elapsed)
    {
        var produced = Interlocked.Read(ref _producedCount);
        var routed = Interlocked.Read(ref _routedCount);
        var routedBatches = Interlocked.Read(ref _routedBatches);
        var waitSumTicks = Interlocked.Read(ref _writeWaitTicksSum);
        var waitMaxTicks = Interlocked.Read(ref _writeWaitTicksMax);
        var blocked = Interlocked.Read(ref _writeBlockedCount);

        var elapsedSeconds = Math.Max(0.001, elapsed.TotalSeconds);
        var averageWaitMs = produced == 0 ? 0 : ToMilliseconds(waitSumTicks) / produced;
        var waitMaxMs = ToMilliseconds(waitMaxTicks);
        var waitP95Ms = ComputePercentileMs(0.95, produced);
        var blockedPercent = produced == 0 ? 0 : blocked * 100.0 / produced;

        return new StageReport(
            stage.Name,
            stage.DurationSeconds,
            stage.ChannelCapacity,
            stage.BatchSize,
            stage.DeviceCount,
            stage.PointsPerDevice,
            stage.IntervalMilliseconds,
            stage.RouterDelayMs,
            stage.SinkDelayMs,
            "LoadTestConnector",
            "SlowRouter",
            "SlowSink",
            elapsedSeconds,
            produced,
            routed,
            routedBatches,
            produced / elapsedSeconds,
            routed / elapsedSeconds,
            averageWaitMs,
            waitP95Ms,
            waitMaxMs,
            blockedPercent,
            WaitHistogramEdgesMs.ToArray(),
            _waitHistogramCounts.ToArray(),
            _sampleMessageJson ?? "{}");
    }

    private static void UpdateMax(ref long target, long value)
    {
        long current;
        while (value > (current = Interlocked.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private static int GetWaitBucketIndex(double waitMs)
    {
        for (var i = 0; i < WaitHistogramEdgesMs.Length; i++)
        {
            if (waitMs <= WaitHistogramEdgesMs[i])
            {
                return i;
            }
        }

        return WaitHistogramEdgesMs.Length;
    }

    private double ComputePercentileMs(double percentile, long totalSamples)
    {
        if (totalSamples == 0)
        {
            return 0;
        }

        var threshold = totalSamples * percentile;
        long cumulative = 0;
        for (var i = 0; i < _waitHistogramCounts.Length; i++)
        {
            cumulative += Interlocked.Read(ref _waitHistogramCounts[i]);
            if (cumulative >= threshold)
            {
                return i < WaitHistogramEdgesMs.Length
                    ? WaitHistogramEdgesMs[i]
                    : WaitHistogramEdgesMs[^1] * 2;
            }
        }

        return WaitHistogramEdgesMs[^1] * 2;
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}

internal sealed class LoadTestConnector : ITelemetryIngestConnector
{
    private readonly LoadStage _stage;
    private readonly MetricsRecorder _metrics;
    private readonly Random _random = new();

    public LoadTestConnector(LoadStage stage, MetricsRecorder metrics)
    {
        _stage = stage;
        _metrics = metrics;
    }

    public string Name => "LoadTest";

    public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        var deviceCount = Math.Max(1, _stage.DeviceCount);
        var pointsPerDevice = Math.Max(1, _stage.PointsPerDevice);
        var sequences = new long[deviceCount];
        var delay = TimeSpan.FromMilliseconds(Math.Max(1, _stage.IntervalMilliseconds));

        while (!ct.IsCancellationRequested)
        {
            var timestamp = DateTimeOffset.UtcNow;
            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                sequences[deviceIndex]++;
                var deviceId = $"load-{deviceIndex + 1}";
                for (var pointIndex = 0; pointIndex < pointsPerDevice; pointIndex++)
                {
                    var pointId = $"p{pointIndex + 1}";
                    var msg = new TelemetryPointMsg
                    {
                        TenantId = "load",
                        BuildingName = "building",
                        SpaceId = "space",
                        DeviceId = deviceId,
                        PointId = pointId,
                        Sequence = sequences[deviceIndex],
                        Timestamp = timestamp,
                        Value = Math.Round(_random.NextDouble() * 100.0, 3)
                    };

                    _metrics.TryRecordSample(msg);
                    var start = Stopwatch.GetTimestamp();
                    try
                    {
                        await writer.WriteAsync(msg, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    var elapsedTicks = Stopwatch.GetTimestamp() - start;
                    _metrics.RecordProduced(elapsedTicks);
                }
            }

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

internal sealed class SlowRouter : ITelemetryRouterGrain
{
    private readonly int _delayMs;
    private readonly MetricsRecorder _metrics;

    public SlowRouter(int delayMs, MetricsRecorder metrics)
    {
        _delayMs = delayMs;
        _metrics = metrics;
    }

    public Task RouteAsync(TelemetryPointMsg msg)
    {
        _metrics.RecordRouted(1);
        return Task.CompletedTask;
    }

    public async Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        _metrics.RecordRouted(batch.Count);
        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs);
        }
    }
}

internal sealed class SlowSink : ITelemetryEventSink
{
    private readonly int _delayMs;

    public SlowSink(int delayMs)
    {
        _delayMs = delayMs;
    }

    public string Name => "SlowSink";

    public Task WriteAsync(TelemetryEventEnvelope envelope, CancellationToken ct)
    {
        return WriteBatchAsync(new[] { envelope }, ct);
    }

    public async Task WriteBatchAsync(IReadOnlyList<TelemetryEventEnvelope> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, ct);
        }
    }
}
