using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;
using Telemetry.Ingest.RabbitMq;

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
        var configPath = GetArgValue(args, "--config") ?? "appsettings.loadtest.json";
        var quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        var includeBatchSweep = args.Contains("--batch-sweep", StringComparer.OrdinalIgnoreCase);
        var includeAbnormal = args.Contains("--abnormal", StringComparer.OrdinalIgnoreCase);
        var includeSoak = args.Contains("--soak", StringComparer.OrdinalIgnoreCase);
        var includeSpike = args.Contains("--spike", StringComparer.OrdinalIgnoreCase);
        var includeMultiConnector = args.Contains("--multi-connector", StringComparer.OrdinalIgnoreCase);
        var stages = quick ? BuildQuickStages() : BuildDefaultStages();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var rabbitMqOptions = configuration.GetSection("RabbitMq").Get<RabbitMqIngestOptions>() ?? new RabbitMqIngestOptions();

        if (includeBatchSweep)
        {
            stages = stages.Concat(BuildBatchSweepStages(quick)).ToArray();
        }

        if (includeAbnormal)
        {
            stages = stages.Concat(BuildAbnormalStages(quick)).ToArray();
        }

        if (includeSoak)
        {
            stages = stages.Concat(BuildRabbitMqSoakStages(quick)).ToArray();
        }

        if (includeSpike)
        {
            stages = stages.Concat(BuildRabbitMqSpikeStages(quick)).ToArray();
        }

        if (includeMultiConnector)
        {
            stages = stages.Concat(BuildMultiConnectorStages(quick)).ToArray();
        }

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var runStart = DateTimeOffset.UtcNow;
        var stageResults = new List<StageReport>();

        foreach (var stage in stages)
        {
            Console.WriteLine($"Stage {stage.Name} running for {stage.DurationSeconds}s...");
            var metrics = new MetricsRecorder();
            var connectors = BuildConnectors(stage, metrics, rabbitMqOptions);
            var router = new SlowRouter(stage.RouterDelayMs, metrics);
            var sink = new SlowSink(stage.SinkDelayMs, stage.SinkFailureRatePercent, metrics);

            var enabledConnectors = connectors.Select(connector => connector.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var options = Options.Create(new TelemetryIngestOptions
            {
                Enabled = enabledConnectors,
                BatchSize = stage.BatchSize,
                ChannelCapacity = stage.ChannelCapacity,
                EventSinks = new TelemetryIngestEventSinkOptions
                {
                    Enabled = new[] { sink.Name }
                }
            });

            var coordinator = new TelemetryIngestCoordinator(
                connectors,
                new[] { sink },
                router,
                new AllowAllTelemetryPointRegistrationFilter(),
                options,
                NullLogger<TelemetryIngestCoordinator>.Instance);

            var stageStopwatch = Stopwatch.StartNew();
            try
            {
                await coordinator.StartAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(stage.DurationSeconds));
                await coordinator.StopAsync(CancellationToken.None);
            }
            finally
            {
                stageStopwatch.Stop();
                await DisposeConnectorsAsync(connectors);
            }

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
                SinkDelayMs: 0,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false),
            new LoadStage(
                Name: "ramp-1",
                DurationSeconds: 20,
                ChannelCapacity: 200,
                BatchSize: 50,
                DeviceCount: 20,
                PointsPerDevice: 10,
                IntervalMilliseconds: 50,
                RouterDelayMs: 10,
                SinkDelayMs: 0,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false),
            new LoadStage(
                Name: "ramp-2",
                DurationSeconds: 20,
                ChannelCapacity: 100,
                BatchSize: 50,
                DeviceCount: 50,
                PointsPerDevice: 20,
                IntervalMilliseconds: 20,
                RouterDelayMs: 20,
                SinkDelayMs: 5,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false),
            new LoadStage(
                Name: "overload",
                DurationSeconds: 20,
                ChannelCapacity: 50,
                BatchSize: 50,
                DeviceCount: 100,
                PointsPerDevice: 20,
                IntervalMilliseconds: 10,
                RouterDelayMs: 50,
                SinkDelayMs: 10,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false)
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
                SinkDelayMs: 0,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false),
            new LoadStage(
                Name: "overload",
                DurationSeconds: 6,
                ChannelCapacity: 50,
                BatchSize: 50,
                DeviceCount: 60,
                PointsPerDevice: 20,
                IntervalMilliseconds: 10,
                RouterDelayMs: 50,
                SinkDelayMs: 10,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false)
        };
    }

    private static IReadOnlyList<LoadStage> BuildBatchSweepStages(bool quick)
    {
        var duration = quick ? 6 : 15;
        var batchSizes = quick ? new[] { 10, 100, 500 } : new[] { 10, 50, 100, 250, 500 };
        return batchSizes.Select(batchSize => new LoadStage(
            Name: $"batch-{batchSize}",
            DurationSeconds: duration,
            ChannelCapacity: 200,
            BatchSize: batchSize,
            DeviceCount: 30,
            PointsPerDevice: 10,
            IntervalMilliseconds: 50,
            RouterDelayMs: 10,
            SinkDelayMs: 5,
            SinkFailureRatePercent: 0,
            ConnectorFailureAfterMessages: 0,
            LoadTestConnectorCount: 1,
            UseRabbitMqConnector: false)).ToArray();
    }

    private static IReadOnlyList<LoadStage> BuildAbnormalStages(bool quick)
    {
        var duration = quick ? 6 : 15;
        return new[]
        {
            new LoadStage(
                Name: "sink-failure-5pct",
                DurationSeconds: duration,
                ChannelCapacity: 100,
                BatchSize: 50,
                DeviceCount: 40,
                PointsPerDevice: 15,
                IntervalMilliseconds: 30,
                RouterDelayMs: 10,
                SinkDelayMs: 10,
                SinkFailureRatePercent: 5,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false),
            new LoadStage(
                Name: "connector-stop-early",
                DurationSeconds: duration,
                ChannelCapacity: 100,
                BatchSize: 50,
                DeviceCount: 30,
                PointsPerDevice: 10,
                IntervalMilliseconds: 30,
                RouterDelayMs: 10,
                SinkDelayMs: 5,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: quick ? 500 : 2000,
                LoadTestConnectorCount: 1,
                UseRabbitMqConnector: false)
        };
    }

    private static IReadOnlyList<LoadStage> BuildRabbitMqSoakStages(bool quick)
    {
        return new[]
        {
            new LoadStage(
                Name: "rabbitmq-soak",
                DurationSeconds: quick ? 60 : 1800,
                ChannelCapacity: 200,
                BatchSize: 50,
                DeviceCount: 0,
                PointsPerDevice: 0,
                IntervalMilliseconds: 0,
                RouterDelayMs: 5,
                SinkDelayMs: 5,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 0,
                UseRabbitMqConnector: true)
        };
    }

    private static IReadOnlyList<LoadStage> BuildRabbitMqSpikeStages(bool quick)
    {
        return new[]
        {
            new LoadStage(
                Name: "rabbitmq-spike",
                DurationSeconds: quick ? 45 : 120,
                ChannelCapacity: 150,
                BatchSize: 50,
                DeviceCount: 0,
                PointsPerDevice: 0,
                IntervalMilliseconds: 0,
                RouterDelayMs: 10,
                SinkDelayMs: 5,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 0,
                UseRabbitMqConnector: true)
        };
    }

    private static IReadOnlyList<LoadStage> BuildMultiConnectorStages(bool quick)
    {
        return new[]
        {
            new LoadStage(
                Name: "mixed-connectors",
                DurationSeconds: quick ? 30 : 90,
                ChannelCapacity: 150,
                BatchSize: 50,
                DeviceCount: 20,
                PointsPerDevice: 10,
                IntervalMilliseconds: 50,
                RouterDelayMs: 10,
                SinkDelayMs: 5,
                SinkFailureRatePercent: 0,
                ConnectorFailureAfterMessages: 0,
                LoadTestConnectorCount: 2,
                UseRabbitMqConnector: true)
        };
    }

    private static List<ITelemetryIngestConnector> BuildConnectors(
        LoadStage stage,
        MetricsRecorder metrics,
        RabbitMqIngestOptions rabbitMqOptions)
    {
        var connectors = new List<ITelemetryIngestConnector>();
        for (var i = 0; i < stage.LoadTestConnectorCount; i++)
        {
            connectors.Add(new LoadTestConnector(stage, metrics));
        }

        if (stage.UseRabbitMqConnector)
        {
            var options = Options.Create(rabbitMqOptions);
            var rabbitConnector = new RabbitMqIngestConnector(options, NullLogger<RabbitMqIngestConnector>.Instance);
            connectors.Add(new InstrumentedConnector(rabbitConnector, metrics));
        }

        if (connectors.Count == 0)
        {
            throw new InvalidOperationException($"Stage {stage.Name} does not configure any connectors.");
        }

        return connectors;
    }

    private static async Task DisposeConnectorsAsync(IEnumerable<ITelemetryIngestConnector> connectors)
    {
        foreach (var connector in connectors)
        {
            switch (connector)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
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
    int SinkDelayMs,
    int SinkFailureRatePercent,
    int ConnectorFailureAfterMessages,
    int LoadTestConnectorCount,
    bool UseRabbitMqConnector);

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
    int SinkFailureRatePercent,
    int ConnectorFailureAfterMessages,
    int LoadTestConnectorCount,
    bool UseRabbitMqConnector,
    string ConnectorSummary,
    string ConnectorName,
    string RouterName,
    string SinkName,
    double ObservedDurationSeconds,
    long ProducedCount,
    long RoutedCount,
    long RoutedBatches,
    long SinkFailureCount,
    long ConnectorFailureCount,
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
                $"SinkDelayMs={stage.SinkDelayMs}, SinkFailureRate={stage.SinkFailureRatePercent}%, " +
                $"LoadTestConnectors={stage.LoadTestConnectorCount}, UseRabbitMq={stage.UseRabbitMqConnector}");
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
                $"SinkDelayMs={stage.SinkDelayMs}, SinkFailureRate={stage.SinkFailureRatePercent}%, " +
                $"ConnectorFailureAfterMessages={stage.ConnectorFailureAfterMessages}, " +
                $"LoadTestConnectors={stage.LoadTestConnectorCount}, UseRabbitMq={stage.UseRabbitMqConnector}");
            lines.Add($"Connector: {stage.ConnectorSummary}");
            lines.Add($"Router: {stage.RouterName}");
            lines.Add($"Sink: {stage.SinkName}");
            lines.Add($"Sink failures: {stage.SinkFailureCount}");
            lines.Add($"Connector failures: {stage.ConnectorFailureCount}");
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
    private long _sinkFailureCount;
    private long _connectorFailureCount;
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

    public void RecordSinkFailure()
    {
        Interlocked.Increment(ref _sinkFailureCount);
    }

    public void RecordConnectorFailure()
    {
        Interlocked.Increment(ref _connectorFailureCount);
    }

    public StageReport CreateStageReport(LoadStage stage, TimeSpan elapsed)
    {
        var produced = Interlocked.Read(ref _producedCount);
        var routed = Interlocked.Read(ref _routedCount);
        var routedBatches = Interlocked.Read(ref _routedBatches);
        var sinkFailures = Interlocked.Read(ref _sinkFailureCount);
        var connectorFailures = Interlocked.Read(ref _connectorFailureCount);
        var waitSumTicks = Interlocked.Read(ref _writeWaitTicksSum);
        var waitMaxTicks = Interlocked.Read(ref _writeWaitTicksMax);
        var blocked = Interlocked.Read(ref _writeBlockedCount);

        var elapsedSeconds = Math.Max(0.001, elapsed.TotalSeconds);
        var averageWaitMs = produced == 0 ? 0 : ToMilliseconds(waitSumTicks) / produced;
        var waitMaxMs = ToMilliseconds(waitMaxTicks);
        var waitP95Ms = ComputePercentileMs(0.95, produced);
        var blockedPercent = produced == 0 ? 0 : blocked * 100.0 / produced;
        var connectorSummary = DescribeConnectors(stage);

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
            stage.SinkFailureRatePercent,
            stage.ConnectorFailureAfterMessages,
            stage.LoadTestConnectorCount,
            stage.UseRabbitMqConnector,
            connectorSummary,
            connectorSummary,
            "SlowRouter",
            "SlowSink",
            elapsedSeconds,
            produced,
            routed,
            routedBatches,
            sinkFailures,
            connectorFailures,
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

    private static string DescribeConnectors(LoadStage stage)
    {
        var parts = new List<string>();
        if (stage.LoadTestConnectorCount > 0)
        {
            parts.Add($"LoadTest x{stage.LoadTestConnectorCount}");
        }

        if (stage.UseRabbitMqConnector)
        {
            parts.Add("RabbitMq");
        }

        return parts.Count == 0 ? "None" : string.Join(" + ", parts);
    }
}

internal sealed class LoadTestConnector : ITelemetryIngestConnector
{
    private readonly LoadStage _stage;
    private readonly MetricsRecorder _metrics;
    private readonly Random _random = new();
    private long _messageCounter;

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
                    if (_stage.ConnectorFailureAfterMessages > 0 &&
                        Interlocked.Increment(ref _messageCounter) >= _stage.ConnectorFailureAfterMessages)
                    {
                        _metrics.RecordConnectorFailure();
                        throw new InvalidOperationException("Simulated connector failure.");
                    }

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

internal sealed class InstrumentedConnector : ITelemetryIngestConnector
{
    private readonly ITelemetryIngestConnector _inner;
    private readonly MetricsRecorder _metrics;

    public InstrumentedConnector(ITelemetryIngestConnector inner, MetricsRecorder metrics)
    {
        _inner = inner;
        _metrics = metrics;
    }

    public string Name => _inner.Name;

    public Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        var instrumentedWriter = new MetricsChannelWriter(writer, _metrics);
        return _inner.StartAsync(instrumentedWriter, ct);
    }
}

internal sealed class MetricsChannelWriter : ChannelWriter<TelemetryPointMsg>
{
    private readonly ChannelWriter<TelemetryPointMsg> _inner;
    private readonly MetricsRecorder _metrics;

    public MetricsChannelWriter(ChannelWriter<TelemetryPointMsg> inner, MetricsRecorder metrics)
    {
        _inner = inner;
        _metrics = metrics;
    }

    public override bool TryComplete(Exception? error = null)
    {
        return _inner.TryComplete(error);
    }

    public override bool TryWrite(TelemetryPointMsg item)
    {
        var written = _inner.TryWrite(item);
        if (written)
        {
            _metrics.TryRecordSample(item);
            _metrics.RecordProduced(0);
        }

        return written;
    }

    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
    {
        return _inner.WaitToWriteAsync(cancellationToken);
    }

    public override ValueTask WriteAsync(TelemetryPointMsg item, CancellationToken cancellationToken = default)
    {
        return WriteAsyncInternal(item, cancellationToken);
    }

    private async ValueTask WriteAsyncInternal(TelemetryPointMsg item, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        await _inner.WriteAsync(item, cancellationToken);
        var elapsedTicks = Stopwatch.GetTimestamp() - start;
        _metrics.TryRecordSample(item);
        _metrics.RecordProduced(elapsedTicks);
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
    private readonly int _failureRatePercent;
    private readonly MetricsRecorder _metrics;
    private readonly Random _random = new();

    public SlowSink(int delayMs, int failureRatePercent, MetricsRecorder metrics)
    {
        _delayMs = delayMs;
        _failureRatePercent = Math.Clamp(failureRatePercent, 0, 100);
        _metrics = metrics;
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

        if (_failureRatePercent > 0 && _random.Next(0, 100) < _failureRatePercent)
        {
            _metrics.RecordSinkFailure();
            throw new InvalidOperationException("Simulated sink failure.");
        }

        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, ct);
        }
    }
}
