using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Telemetry.Orleans.MemoryLoadTest;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        var outputDir = GetArgValue(args, "--output-dir")
            ?? Environment.GetEnvironmentVariable("TELEMETRY_MEMORY_REPORT_DIR")
            ?? "reports";
        var configPath = GetArgValue(args, "--config") ?? "appsettings.memoryloadtest.json";
        var configuration = BuildConfiguration(configPath);
        var config = configuration.Get<MemoryLoadTestConfig>() ?? new MemoryLoadTestConfig();
        EnsureStages(config);

        var report = new MemoryLoadTestReport(
            $"telemetry-memory-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            DateTimeOffset.UtcNow,
            config.TenantId);

        await RunLoadTestAsync(outputDir, config, report);
        return report.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static IConfiguration BuildConfiguration(string configPath)
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static void EnsureStages(MemoryLoadTestConfig config)
    {
        if (config.Stages.Count > 0)
        {
            return;
        }

        config.Stages.AddRange(new[]
        {
            new MemoryLoadTestStageConfig
            {
                Name = "baseline",
                TargetNodeCount = 100,
                DurationSeconds = 30,
                SampleIntervalSeconds = 5,
                NodeBatchSize = 25,
                NodeBatchDelayMilliseconds = 100,
                NodeType = GraphNodeType.Point,
                MaxGrainTypes = 5
            },
            new MemoryLoadTestStageConfig
            {
                Name = "expansion",
                TargetNodeCount = 500,
                DurationSeconds = 45,
                SampleIntervalSeconds = 5,
                NodeBatchSize = 50,
                NodeBatchDelayMilliseconds = 150,
                NodeType = GraphNodeType.Point,
                MaxGrainTypes = 5
            },
            new MemoryLoadTestStageConfig
            {
                Name = "pressure",
                TargetNodeCount = 1000,
                DurationSeconds = 60,
                SampleIntervalSeconds = 5,
                NodeBatchSize = 100,
                NodeBatchDelayMilliseconds = 200,
                NodeType = GraphNodeType.Point,
                MaxGrainTypes = 5
            }
        });
    }

    private static async Task RunLoadTestAsync(string outputDir, MemoryLoadTestConfig config, MemoryLoadTestReport report)
    {
        IClusterClient? client = null;
        IHost? host = null;
        try
        {
            var connection = await BuildAndConnectClientAsync(config.Orleans);
            host = connection.Host;
            client = connection.Client;
            await RegisterTenantAsync(client, config.TenantId);

            var createdNodes = 0;
            foreach (var stage in config.Stages)
            {
                Console.WriteLine($"Starting stage {stage.Name} targeting {stage.TargetNodeCount} nodes...");
                var nodesToCreate = Math.Max(0, stage.TargetNodeCount - createdNodes);
                if (nodesToCreate > 0)
                {
                    await CreateGraphNodesAsync(client, config, createdNodes, nodesToCreate, stage);
                    createdNodes += nodesToCreate;
                }

                var stageReport = await ExecuteStageAsync(client, stage);
                report.Stages.Add(stageReport with { NodesCreated = nodesToCreate });
            }

            report.FinalNodeCount = createdNodes;
            report.Status = "Passed";
        }
        catch (Exception ex)
        {
            report.Status = "Failed";
            report.Error = ex.ToString();
            Console.Error.WriteLine($"Load test failed: {ex.Message}");
        }
        finally
        {
            report.CompletedAt = DateTimeOffset.UtcNow;
            await WriteReportAsync(report, outputDir);
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    private static async Task<(IHost Host, IClusterClient Client)> BuildAndConnectClientAsync(OrleansClientOptions options)
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleansClient(clientBuilder =>
            {
                clientBuilder.Configure<ClusterOptions>(cluster =>
                {
                    cluster.ClusterId = options.ClusterId;
                    cluster.ServiceId = options.ServiceId;
                });

                if (string.IsNullOrWhiteSpace(options.GatewayHost) ||
                    string.Equals(options.GatewayHost, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    clientBuilder.UseLocalhostClustering(gatewayPort: options.GatewayPort);
                }
                else
                {
                    var gatewayAddress = ResolveGatewayAddress(options.GatewayHost);
                    clientBuilder.UseStaticClustering(new[]
                    {
                        new IPEndPoint(gatewayAddress, options.GatewayPort)
                    });
                }
            });

        var host = hostBuilder.Build();
        await host.StartAsync();
        var client = host.Services.GetRequiredService<IClusterClient>();
        return (host, client);
    }

    private static IPAddress ResolveGatewayAddress(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed;
        }

        return Dns.GetHostAddresses(host).First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
    }

    private static async Task RegisterTenantAsync(IClusterClient client, string tenantId)
    {
        var registry = client.GetGrain<IGraphTenantRegistryGrain>(0);
        await registry.RegisterTenantAsync(tenantId);
    }

    private static async Task CreateGraphNodesAsync(
        IClusterClient client,
        MemoryLoadTestConfig config,
        int existingCount,
        int nodesToCreate,
        MemoryLoadTestStageConfig stage)
    {
        var index = client.GetGrain<IGraphIndexGrain>(config.TenantId);
        var tasks = new List<Task>();

        for (var i = 0; i < nodesToCreate; i++)
        {
            var nodeOrdinal = existingCount + i + 1;
            var nodeId = $"{config.NodeIdPrefix}-{nodeOrdinal}";
            var key = GraphNodeKey.Create(config.TenantId, nodeId);
            var definition = new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = stage.NodeType,
                DisplayName = $"{stage.Name} Node {nodeOrdinal}",
                Attributes =
                {
                    ["load:stage"] = stage.Name,
                    ["load:sequence"] = nodeOrdinal.ToString(),
                    ["load:tenant"] = config.TenantId
                }
            };

            var grain = client.GetGrain<IGraphNodeGrain>(key);
            tasks.Add(CreateNodeAsync(grain, index, definition));

            if (stage.NodeBatchSize > 0 && tasks.Count >= stage.NodeBatchSize)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                if (stage.NodeBatchDelayMilliseconds > 0)
                {
                    await Task.Delay(stage.NodeBatchDelayMilliseconds);
                }
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private static async Task CreateNodeAsync(
        IGraphNodeGrain grain,
        IGraphIndexGrain index,
        GraphNodeDefinition definition)
    {
        await grain.UpsertAsync(definition);
        await index.AddNodeAsync(definition);
    }

    private static async Task<MemoryStageReport> ExecuteStageAsync(IClusterClient client, MemoryLoadTestStageConfig stage)
    {
        var mgmt = client.GetGrain<IManagementGrain>(0);
        var hosts = await mgmt.GetHosts(true);
        var addresses = hosts?.Keys.ToArray() ?? Array.Empty<SiloAddress>();
        var samples = new List<SiloMemorySample>();
        var sampleInterval = TimeSpan.FromSeconds(Math.Max(1, stage.SampleIntervalSeconds));
        var stageDuration = TimeSpan.FromSeconds(Math.Max(1, stage.DurationSeconds));
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < stageDuration)
        {
            if (addresses.Length > 0)
            {
                var stats = await mgmt.GetRuntimeStatistics(addresses);
                var timestamp = DateTimeOffset.UtcNow;
                for (var i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    var siloAddress = i < addresses.Length
                        ? addresses[i].ToString() ?? "unknown"
                        : "unknown";
                    var env = stat.EnvironmentStatistics;
                    samples.Add(new SiloMemorySample(
                        siloAddress,
                        timestamp,
                        env.FilteredMemoryUsageBytes,
                        env.MaximumAvailableMemoryBytes,
                        env.FilteredCpuUsagePercentage,
                        stat.ActivationCount));
                }
            }

            var remaining = stageDuration - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < sampleInterval ? remaining : sampleInterval);
        }

        var grainStats = await mgmt.GetSimpleGrainStatistics();
        var grainSummaries = grainStats
            .GroupBy(stat => string.IsNullOrWhiteSpace(stat.GrainType) ? "<unknown>" : stat.GrainType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GrainTypeSummary(
                group.Key,
                group.Sum(stat => stat.ActivationCount),
                group.Select(stat => stat.SiloAddress?.ToString() ?? "unknown").Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(summary => summary.ActivationCount)
            .Take(stage.MaxGrainTypes)
            .ToList();

        var siloSummaries = BuildSiloSummaries(samples);

        return new MemoryStageReport(
            stage.Name,
            stage.TargetNodeCount,
            0,
            stage.DurationSeconds,
            stage.SampleIntervalSeconds,
            stage.NodeBatchSize,
            stage.NodeBatchDelayMilliseconds,
            siloSummaries,
            samples,
            grainSummaries);
    }

    private static IReadOnlyList<SiloMemorySummary> BuildSiloSummaries(IReadOnlyList<SiloMemorySample> samples)
    {
        return samples
            .GroupBy(sample => sample.SiloAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var memoryValues = group.Where(sample => sample.MemoryUsageBytes.HasValue).Select(sample => sample.MemoryUsageBytes!.Value).ToList();
                var maxMemory = memoryValues.Any() ? memoryValues.Max() : (double?)null;
                var minMemory = memoryValues.Any() ? memoryValues.Min() : (double?)null;
                var avgMemory = memoryValues.Any() ? memoryValues.Average() : (double?)null;

                var maxAvailableValues = group.Where(sample => sample.MaximumAvailableMemoryBytes.HasValue).Select(sample => sample.MaximumAvailableMemoryBytes!.Value).ToList();
                var avgAvailable = maxAvailableValues.Any() ? maxAvailableValues.Average() : (double?)null;
                var peakAvailable = maxAvailableValues.Any() ? maxAvailableValues.Max() : (double?)null;

                var cpuValues = group.Where(sample => sample.CpuPercentage.HasValue).Select(sample => sample.CpuPercentage!.Value).ToList();
                var avgCpu = cpuValues.Any() ? cpuValues.Average() : (double?)null;
                var peakCpu = cpuValues.Any() ? cpuValues.Max() : (double?)null;

                var activationValues = group.Select(sample => (double)sample.ActivationCount).ToList();
                var avgActivation = activationValues.Any() ? activationValues.Average() : (double?)null;
                var peakActivation = activationValues.Any() ? activationValues.Max() : (double?)null;

                return new SiloMemorySummary(
                    group.Key,
                    group.Count(),
                    avgMemory,
                    maxMemory,
                    minMemory,
                    avgAvailable,
                    peakAvailable,
                    avgCpu,
                    peakCpu,
                    avgActivation,
                    peakActivation);
            })
            .ToList();
    }

    private static async Task WriteReportAsync(MemoryLoadTestReport report, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var mdPath = Path.Combine(outputDir, $"{report.RunId}.md");
        var jsonPath = Path.Combine(outputDir, $"{report.RunId}.json");
        await File.WriteAllTextAsync(mdPath, MemoryReportFormatter.ToMarkdown(report));
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        Console.WriteLine($"Report written: {mdPath}");
        Console.WriteLine($"Report written: {jsonPath}");
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
}

internal sealed class MemoryLoadTestConfig
{
    public OrleansClientOptions Orleans { get; set; } = new();
    public string TenantId { get; set; } = "load";
    public string NodeIdPrefix { get; set; } = "load-node";
    public List<MemoryLoadTestStageConfig> Stages { get; set; } = new();
    public int NodeAttributeCount { get; set; } = 0;
}

internal sealed class OrleansClientOptions
{
    public string ClusterId { get; set; } = "telemetry-cluster";
    public string ServiceId { get; set; } = "telemetry-service";
    public string GatewayHost { get; set; } = "localhost";
    public int GatewayPort { get; set; } = 30000;
}

internal sealed class MemoryLoadTestStageConfig
{
    public string Name { get; set; } = string.Empty;
    public int TargetNodeCount { get; set; }
    public int DurationSeconds { get; set; } = 30;
    public int SampleIntervalSeconds { get; set; } = 5;
    public int NodeBatchSize { get; set; } = 20;
    public int NodeBatchDelayMilliseconds { get; set; } = 100;
    public GraphNodeType NodeType { get; set; } = GraphNodeType.Point;
    public int MaxGrainTypes { get; set; } = 5;
}

internal sealed class MemoryLoadTestReport
{
    public MemoryLoadTestReport(string runId, DateTimeOffset startedAt, string tenantId)
    {
        RunId = runId;
        StartedAt = startedAt;
        TenantId = tenantId;
        CompletedAt = DateTimeOffset.MinValue;
    }

    public string RunId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; set; }
    public string TenantId { get; }
    public string Status { get; set; } = "Unknown";
    public string? Error { get; set; }
    public int FinalNodeCount { get; set; }
    public List<MemoryStageReport> Stages { get; } = new();
}

internal sealed record MemoryStageReport(
    string Name,
    int TargetNodeCount,
    int NodesCreated,
    int DurationSeconds,
    int SampleIntervalSeconds,
    int NodeBatchSize,
    int NodeBatchDelayMilliseconds,
    IReadOnlyList<SiloMemorySummary> SiloSummaries,
    IReadOnlyList<SiloMemorySample> Samples,
    IReadOnlyList<GrainTypeSummary> GrainSummaries);

internal sealed record SiloMemorySample(
    string SiloAddress,
    DateTimeOffset Timestamp,
    double? MemoryUsageBytes,
    double? MaximumAvailableMemoryBytes,
    double? CpuPercentage,
    int ActivationCount);

internal sealed record SiloMemorySummary(
    string SiloAddress,
    int SampleCount,
    double? AverageMemoryUsageBytes,
    double? PeakMemoryUsageBytes,
    double? MinimumMemoryUsageBytes,
    double? AverageMaximumAvailableMemoryBytes,
    double? PeakMaximumAvailableMemoryBytes,
    double? AverageCpuPercentage,
    double? PeakCpuPercentage,
    double? AverageActivationCount,
    double? PeakActivationCount);

internal sealed record GrainTypeSummary(string GrainType, int ActivationCount, string[] Silos);

internal static class MemoryReportFormatter
{
    public static string ToMarkdown(MemoryLoadTestReport report)
    {
        var lines = new List<string>
        {
            "# Orleans Memory Load Test Report",
            $"RunId: {report.RunId}",
            $"Status: {report.Status}",
            $"Tenant: {report.TenantId}",
            $"StartedAt: {report.StartedAt:O}",
            $"CompletedAt: {report.CompletedAt:O}",
            $"FinalNodeCount: {report.FinalNodeCount}",
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            lines.Add("## Error");
            lines.Add(report.Error);
            lines.Add(string.Empty);
        }

        foreach (var stage in report.Stages)
        {
            lines.Add($"## Stage: {stage.Name}");
            lines.Add($"- TargetNodeCount: {stage.TargetNodeCount}");
            lines.Add($"- NodesCreated: {stage.NodesCreated}");
            lines.Add($"- DurationSeconds: {stage.DurationSeconds}");
            lines.Add($"- SampleIntervalSeconds: {stage.SampleIntervalSeconds}");
            lines.Add($"- NodeBatchSize: {stage.NodeBatchSize}");
            lines.Add($"- NodeBatchDelayMilliseconds: {stage.NodeBatchDelayMilliseconds}");
            lines.Add(string.Empty);

            if (stage.SiloSummaries.Count > 0)
            {
                lines.Add("| Silo | Samples | Avg Memory | Peak Memory | Avg CPU | Peak CPU | Avg Activations | Peak Activations |");
                lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- |");
                foreach (var summary in stage.SiloSummaries)
                {
                    lines.Add(
                        $"| {summary.SiloAddress} | {summary.SampleCount} | {FormatBytes(summary.AverageMemoryUsageBytes)} | {FormatBytes(summary.PeakMemoryUsageBytes)} | " +
                        $"{FormatPercent(summary.AverageCpuPercentage)} | {FormatPercent(summary.PeakCpuPercentage)} | {FormatInteger(summary.AverageActivationCount)} | {FormatInteger(summary.PeakActivationCount)} |");
                }

                lines.Add(string.Empty);
            }

            if (stage.GrainSummaries.Count > 0)
            {
                lines.Add("### Grain activations");
                foreach (var grain in stage.GrainSummaries)
                {
                    lines.Add($"- {grain.GrainType}: {grain.ActivationCount} activations across {string.Join(", ", grain.Silos)}");
                }

                lines.Add(string.Empty);
            }

            lines.Add($"Sampled {stage.Samples.Count} memory snapshots.");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(double? bytes)
    {
        if (bytes is null)
        {
            return "n/a";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = bytes.Value;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:F1} {units[index]}";
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:F1} %" : "n/a";
    }

    private static string FormatInteger(double? value)
    {
        return value.HasValue ? $"{value.Value:F0}" : "n/a";
    }
}
