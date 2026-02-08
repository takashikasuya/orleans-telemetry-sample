using System.Collections;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using DataModel.Analyzer.Extensions;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Telemetry.Ingest;
using Telemetry.Ingest.Kafka;
using Telemetry.Ingest.RabbitMq;
using Telemetry.Ingest.Simulator;
using Telemetry.Storage;
using Publisher;
using SiloHost;
using Xunit;

namespace Telemetry.E2E.Tests;

public sealed class TelemetryE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task EndToEndReport_IsGenerated()
    {
        var report = new TelemetryE2EReport
        {
            RunId = $"telemetry-e2e-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            StartedAt = DateTimeOffset.UtcNow
        };

        IHost? siloHost = null;
        var siloStarted = false;
        ApiGatewayFactory? apiFactory = null;

        try
        {
            var options = LoadOptions();
            report.MaxApiLagMilliseconds = options.MaxApiLagMilliseconds;
            report.ReportDirectory = ResolveReportPath(options.ReportPath);

            var tenantId = "t1";
            report.TenantId = tenantId;
            TestAuthHandler.TenantId = tenantId;

            var rdfPath = Path.Combine(AppContext.BaseDirectory, "seed.ttl");
            report.RdfSeedPath = rdfPath;

            var tempRoot = CreateTempDirectory();
            var stageRoot = Path.Combine(tempRoot, "stage");
            var parquetRoot = Path.Combine(tempRoot, "parquet");
            var indexRoot = Path.Combine(tempRoot, "index");
            Directory.CreateDirectory(stageRoot);
            Directory.CreateDirectory(parquetRoot);
            Directory.CreateDirectory(indexRoot);

            report.Simulator = new TelemetryE2ESimulatorConfig
            {
                TenantId = tenantId,
                BuildingName = "building",
                SpaceId = "space",
                DeviceIdPrefix = "device",
                DeviceCount = 1,
                PointsPerDevice = 1,
                IntervalMilliseconds = 500
            };

            Environment.SetEnvironmentVariable("RDF_SEED_PATH", rdfPath);
            Environment.SetEnvironmentVariable("TENANT_ID", tenantId);

            var siloPort = GetFreeTcpPort();
            var gatewayPort = GetFreeTcpPort();
            var siloConfig = BuildSiloConfig(stageRoot, parquetRoot, indexRoot, report.Simulator, siloPort, gatewayPort);
            siloHost = CreateSiloHost(siloConfig);
            await siloHost.StartAsync();
            siloStarted = true;

            var apiConfig = BuildApiConfig(stageRoot, parquetRoot, indexRoot, gatewayPort);
            apiFactory = new ApiGatewayFactory(apiConfig);
            using var client = apiFactory.CreateClient();

            var timeout = TimeSpan.FromSeconds(options.WaitTimeoutSeconds);
            var nodeId = "urn:point-1";
            var nodeSnapshot = await WaitForNodeSnapshotAsync(client, nodeId, timeout);
            report.Graph = new TelemetryE2EGraphBinding
            {
                NodeId = nodeId,
                Attributes = nodeSnapshot.Node.Attributes
            };

            var stageRecord = await WaitForStageRecordAsync(stageRoot, timeout);
            report.SeedEvent = new TelemetryE2EEvent
            {
                TenantId = stageRecord.TenantId,
                BuildingName = stageRecord.BuildingName,
                SpaceId = stageRecord.SpaceId,
                DeviceId = stageRecord.DeviceId,
                PointId = stageRecord.PointId,
                Sequence = stageRecord.Sequence,
                OccurredAt = stageRecord.OccurredAt,
                IngestedAt = stageRecord.IngestedAt,
                ValueJson = stageRecord.ValueJson
            };

            var compactor = siloHost.Services.GetRequiredService<TelemetryStorageCompactor>();
            var compacted = await compactor.CompactAsync(CancellationToken.None);

            var bucketStart = TelemetryStoragePaths.GetBucketStart(stageRecord.OccurredAt, 15);
            var stageFile = TelemetryStoragePaths.BuildStageFilePath(stageRoot, stageRecord.TenantId, stageRecord.DeviceId, bucketStart);
            var parquetFile = TelemetryStoragePaths.BuildParquetFilePath(parquetRoot, stageRecord.TenantId, stageRecord.DeviceId, bucketStart);
            var indexFile = TelemetryStoragePaths.BuildIndexFilePath(indexRoot, stageRecord.TenantId, stageRecord.DeviceId, bucketStart);
            report.Storage = new TelemetryE2EStorageCheck
            {
                StageFilePath = stageFile,
                ParquetFilePath = parquetFile,
                IndexFilePath = indexFile,
                StageExists = File.Exists(stageFile),
                ParquetExists = File.Exists(parquetFile),
                IndexExists = File.Exists(indexFile),
                CompactedBuckets = compacted
            };

            var pointResult = await WaitForPointSnapshotAsync(client, nodeId, stageRecord.Sequence, timeout);
            report.Api = new TelemetryE2EApiCheck
            {
                PointLastSequence = pointResult.Snapshot.LastSequence,
                PointUpdatedAt = pointResult.Snapshot.UpdatedAt,
                PointLatestValueJson = JsonSerializer.Serialize(pointResult.Snapshot.LatestValue, JsonOptions),
                PointReadAt = pointResult.ReadAt,
                PointLagMilliseconds = pointResult.LagMilliseconds
            };

            report.Api.PointLagMilliseconds.Should().BeLessThanOrEqualTo(options.MaxApiLagMilliseconds);

            var deviceSnapshot = await WaitForDeviceSnapshotAsync(client, stageRecord.DeviceId, stageRecord.Sequence, timeout);
            report.Api.DeviceLastSequence = deviceSnapshot.LastSequence;
            report.Api.DeviceUpdatedAt = deviceSnapshot.UpdatedAt;
            report.Api.DevicePropertiesJson = deviceSnapshot.PropertiesJson;

            var telemetryResult = await WaitForTelemetryResultsAsync(client, stageRecord, timeout);
            report.Api.TelemetryResultCount = telemetryResult.Count;
            report.Api.TelemetryFirstResultJson = telemetryResult.Count > 0 ? telemetryResult[0].GetRawText() : string.Empty;

            report.Storage.ParquetExists.Should().BeTrue();
            report.Storage.IndexExists.Should().BeTrue();
            telemetryResult.Count.Should().BeGreaterThan(0);

            report.Graph.Attributes.Should().ContainKey("PointId");
            report.Graph.Attributes["PointId"].Should().Be(stageRecord.PointId);
            report.Graph.Attributes.Should().ContainKey("DeviceId");
            report.Graph.Attributes["DeviceId"].Should().Be(stageRecord.DeviceId);

            report.Status = "Passed";
        }
        catch (Exception ex)
        {
            report.Status = "Failed";
            report.Error = ex.ToString();
            throw;
        }
        finally
        {
            report.CompletedAt = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(report.ReportDirectory))
            {
                await TelemetryE2EReportWriter.WriteAsync(report, report.ReportDirectory);
            }

            if (apiFactory is not null)
            {
                apiFactory.Dispose();
            }

            if (siloHost is not null)
            {
                if (siloStarted)
                {
                    await siloHost.StopAsync();
                }
                siloHost.Dispose();
            }

            Environment.SetEnvironmentVariable("RDF_SEED_PATH", null);
            Environment.SetEnvironmentVariable("TENANT_ID", null);
        }
    }

    [Fact]
    public async Task RdfPublisherTelemetry_IsVisibleThroughApi()
    {
        var options = LoadOptions();
        IHost? siloHost = null;
        var siloStarted = false;
        ApiGatewayFactory? apiFactory = null;

        try
        {
            var tenantId = "t1";
            var rdfPath = Path.Combine(AppContext.BaseDirectory, "seed.ttl");
            Environment.SetEnvironmentVariable("RDF_SEED_PATH", rdfPath);
            Environment.SetEnvironmentVariable("TENANT_ID", tenantId);

            var tempRoot = CreateTempDirectory();
            var stageRoot = Path.Combine(tempRoot, "stage");
            var parquetRoot = Path.Combine(tempRoot, "parquet");
            var indexRoot = Path.Combine(tempRoot, "index");
            Directory.CreateDirectory(stageRoot);
            Directory.CreateDirectory(parquetRoot);
            Directory.CreateDirectory(indexRoot);

            var simulatorConfig = new TelemetryE2ESimulatorConfig
            {
                TenantId = tenantId,
                BuildingName = "building",
                SpaceId = "space",
                DeviceIdPrefix = "device",
                DeviceCount = 1,
                PointsPerDevice = 1,
                IntervalMilliseconds = 500
            };

            var siloPort = GetFreeTcpPort();
            var gatewayPort = GetFreeTcpPort();
            var siloConfig = BuildSiloConfig(stageRoot, parquetRoot, indexRoot, simulatorConfig, siloPort, gatewayPort);
            siloHost = CreateSiloHost(siloConfig);
            await siloHost.StartAsync();
            siloStarted = true;

            var apiConfig = BuildApiConfig(stageRoot, parquetRoot, indexRoot, gatewayPort);
            apiFactory = new ApiGatewayFactory(apiConfig);
            using var client = apiFactory.CreateClient();

            var analyzer = new RdfAnalyzerService(NullLogger<RdfAnalyzerService>.Instance);
            var model = await analyzer.AnalyzeRdfFileAsync(rdfPath);
            var generator = new RdfTelemetryGenerator(model);
            var device = generator.Devices.First();
            var telemetry = generator.CreateTelemetry(tenantId, device, 1);

            var router = siloHost.Services.GetRequiredService<IGrainFactory>().GetGrain<ITelemetryRouterGrain>(Guid.Empty);
            foreach (var property in telemetry.Properties)
            {
                var value = property.Key == RdfTelemetryGenerator.MetadataKey
                    ? NormalizeMetadata(property.Value)
                    : property.Value;

                var pointMsg = new TelemetryPointMsg
                {
                    TenantId = telemetry.TenantId,
                    BuildingName = telemetry.BuildingName,
                    SpaceId = telemetry.SpaceId,
                    DeviceId = telemetry.DeviceId,
                    PointId = property.Key,
                    Sequence = telemetry.Sequence,
                    Timestamp = telemetry.Timestamp,
                    Value = value
                };

                await router.RouteAsync(pointMsg);
            }

            var timeout = TimeSpan.FromSeconds(options.WaitTimeoutSeconds);
            var snapshot = await WaitForDeviceSnapshotAsync(client, device.DeviceId, telemetry.Sequence, timeout);

            using var propsDoc = JsonDocument.Parse(snapshot.PropertiesJson);
            propsDoc.RootElement.TryGetProperty(device.Points.First().PointId, out _).Should().BeTrue();

            telemetry.Properties.Should().ContainKey(RdfTelemetryGenerator.MetadataKey);
            var metadata = telemetry.Properties[RdfTelemetryGenerator.MetadataKey] as IDictionary;
            metadata.Should().NotBeNull();
            metadata!.Contains(device.Points.First().PointId).Should().BeTrue();
            var metadataEntry = metadata[device.Points.First().PointId];
            var pointTypeProperty = metadataEntry?.GetType().GetProperty("PointType");
            pointTypeProperty?.GetValue(metadataEntry)?.Should().Be(device.Points.First().PointType);
        }
        finally
        {
            if (apiFactory is not null)
            {
                apiFactory.Dispose();
            }

            if (siloHost is not null)
            {
                if (siloStarted)
                {
                    await siloHost.StopAsync();
                }
                siloHost.Dispose();
            }

            Environment.SetEnvironmentVariable("RDF_SEED_PATH", null);
            Environment.SetEnvironmentVariable("TENANT_ID", null);
        }
    }

    private static object NormalizeMetadata(object value)
    {
        if (value is not IDictionary metadata)
        {
            return value;
        }

        var result = new Dictionary<string, object>();
        foreach (DictionaryEntry entry in metadata)
        {
            var key = entry.Key?.ToString() ?? string.Empty;
            var entryValue = entry.Value;
            if (entryValue is null)
            {
                result[key] = null!;
                continue;
            }

            var valueDict = new Dictionary<string, object?>();
            foreach (var property in entryValue.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                valueDict[property.Name] = property.GetValue(entryValue);
            }

            result[key] = valueDict;
        }

        return result;
    }

    private static TelemetryE2EReportOptions LoadOptions()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetSection("TelemetryE2E").Get<TelemetryE2EReportOptions>() ?? new TelemetryE2EReportOptions();
    }

    private static string ResolveReportPath(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            reportPath = "reports";
        }

        return Path.IsPathRooted(reportPath)
            ? reportPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), reportPath));
    }

    private static Dictionary<string, string?> BuildSiloConfig(
        string stageRoot,
        string parquetRoot,
        string indexRoot,
        TelemetryE2ESimulatorConfig simulator,
        int siloPort,
        int gatewayPort)
    {
        return new Dictionary<string, string?>
        {
            ["TelemetryIngest:Enabled:0"] = "Simulator",
            ["TelemetryIngest:BatchSize"] = "10",
            ["TelemetryIngest:ChannelCapacity"] = "100",
            ["TelemetryIngest:EventSinks:Enabled:0"] = "ParquetStorage",
            ["TelemetryIngest:Simulator:TenantId"] = simulator.TenantId,
            ["TelemetryIngest:Simulator:BuildingName"] = simulator.BuildingName,
            ["TelemetryIngest:Simulator:SpaceId"] = simulator.SpaceId,
            ["TelemetryIngest:Simulator:DeviceIdPrefix"] = simulator.DeviceIdPrefix,
            ["TelemetryIngest:Simulator:DeviceCount"] = simulator.DeviceCount.ToString(),
            ["TelemetryIngest:Simulator:PointsPerDevice"] = simulator.PointsPerDevice.ToString(),
            ["TelemetryIngest:Simulator:IntervalMilliseconds"] = simulator.IntervalMilliseconds.ToString(),
            ["TelemetryStorage:StagePath"] = stageRoot,
            ["TelemetryStorage:ParquetPath"] = parquetRoot,
            ["TelemetryStorage:IndexPath"] = indexRoot,
            ["TelemetryStorage:BucketMinutes"] = "15",
            ["TelemetryStorage:CompactionIntervalSeconds"] = "2",
            ["TelemetryStorage:DefaultQueryLimit"] = "100",
            ["Orleans:SiloPort"] = siloPort.ToString(),
            ["Orleans:GatewayPort"] = gatewayPort.ToString()
        };
    }

    private static Dictionary<string, string?> BuildApiConfig(string stageRoot, string parquetRoot, string indexRoot, int gatewayPort)
    {
        return new Dictionary<string, string?>
        {
            ["TelemetryStorage:StagePath"] = stageRoot,
            ["TelemetryStorage:ParquetPath"] = parquetRoot,
            ["TelemetryStorage:IndexPath"] = indexRoot,
            ["TelemetryStorage:DefaultQueryLimit"] = "100",
            ["Orleans:GatewayHost"] = "127.0.0.1",
            ["Orleans:GatewayPort"] = gatewayPort.ToString()
        };
    }

    private static IHost CreateSiloHost(Dictionary<string, string?> overrides)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices((context, services) =>
        {
            services.AddDataModelAnalyzer();
            services.AddSerializer(builder =>
            {
                builder.AddAssembly(typeof(TelemetryRouterGrain).Assembly);
                builder.AddAssembly(typeof(ITelemetryRouterGrain).Assembly);
            });
            services.AddSingleton<ITelemetryRouterGrain>(provider =>
            {
                var grainFactory = provider.GetRequiredService<IGrainFactory>();
                return grainFactory.GetGrain<ITelemetryRouterGrain>(Guid.Empty);
            });

            var ingestSection = context.Configuration.GetSection("TelemetryIngest");
            var storageSection = context.Configuration.GetSection("TelemetryStorage");
            services.AddTelemetryIngest(ingestSection);
            services.AddKafkaIngest(ingestSection.GetSection("Kafka"));
            services.AddRabbitMqIngest(ingestSection.GetSection("RabbitMq"));
            services.AddSimulatorIngest(ingestSection.GetSection("Simulator"));
            services.AddLoggingTelemetryEventSink();
            services.Configure<TelemetryStorageOptions>(storageSection);
            services.AddTelemetryStorage();
            services.AddHostedService<TestGraphSeedService>();
        });

        builder.UseOrleans((context, siloBuilder) =>
        {
            var siloPort = context.Configuration.GetValue("Orleans:SiloPort", 11111);
            var gatewayPort = context.Configuration.GetValue("Orleans:GatewayPort", 30000);
            siloBuilder.UseLocalhostClustering(siloPort: siloPort, gatewayPort: gatewayPort);
            siloBuilder.Configure<Orleans.Configuration.ClusterOptions>(options =>
            {
                options.ClusterId = "telemetry-cluster";
                options.ServiceId = "telemetry-service";
            });
            siloBuilder.AddMemoryGrainStorage("DeviceStore");
            siloBuilder.AddMemoryGrainStorage("GraphStore");
            siloBuilder.AddMemoryGrainStorage("GraphIndexStore");
            siloBuilder.AddMemoryGrainStorage("GraphTenantStore");
            siloBuilder.AddMemoryStreams("DeviceUpdates");
            siloBuilder.AddMemoryGrainStorage("PointStore");
            siloBuilder.AddMemoryStreams("PointUpdates");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
        });

        return builder.Build();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<GraphNodeSnapshot> WaitForNodeSnapshotAsync(HttpClient client, string nodeId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var encoded = Uri.EscapeDataString(nodeId);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/nodes/{encoded}");
            if (response.IsSuccessStatusCode)
            {
                var snapshot = await response.Content.ReadFromJsonAsync<GraphNodeSnapshot>(JsonOptions);
                if (snapshot is not null && snapshot.Node.Attributes.Count > 0)
                {
                    return snapshot;
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Graph node was not available within the timeout.");
    }

    private static async Task<TelemetryStageRecord> WaitForStageRecordAsync(string stageRoot, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Directory.Exists(stageRoot))
            {
                var files = Directory.GetFiles(stageRoot, "*.jsonl", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    var lines = await File.ReadAllLinesAsync(files[0]);
                    if (lines.Length > 0)
                    {
                        var record = JsonSerializer.Deserialize<TelemetryStageRecord>(lines[0], JsonOptions);
                        if (record is not null)
                        {
                            return record;
                        }
                    }
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Stage file was not created within the timeout.");
    }

    private static async Task<PointSnapshotResult> WaitForPointSnapshotAsync(
        HttpClient client,
        string nodeId,
        long minSequence,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var encoded = Uri.EscapeDataString(nodeId);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/nodes/{encoded}/value");
            if (response.IsSuccessStatusCode)
            {
                var readAt = DateTimeOffset.UtcNow;
                var snapshot = await response.Content.ReadFromJsonAsync<PointSnapshot>(JsonOptions);
                if (snapshot is not null && snapshot.LastSequence >= minSequence)
                {
                    var lagMs = (readAt - snapshot.UpdatedAt).TotalMilliseconds;
                    return new PointSnapshotResult(snapshot, readAt, lagMs);
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Point snapshot was not updated within the timeout.");
    }

    private static async Task<DeviceSnapshotResult> WaitForDeviceSnapshotAsync(HttpClient client, string deviceId, long minSequence, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var lastSequence = -1L;
        var lastUpdatedAt = DateTimeOffset.MinValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.GetFromJsonAsync<JsonElement>($"/api/devices/{deviceId}");
            if (response.ValueKind != JsonValueKind.Undefined)
            {
                lastSequence = GetPropertyCaseInsensitive(response, "lastSequence").GetInt64();
                lastUpdatedAt = GetPropertyCaseInsensitive(response, "updatedAt").GetDateTimeOffset();
                var props = GetPropertyCaseInsensitive(response, "properties").GetRawText();
                if (lastSequence >= minSequence)
                {
                    return new DeviceSnapshotResult(lastSequence, lastUpdatedAt, props);
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Device snapshot was not updated within the timeout. " +
            $"DeviceId={deviceId} LastSequence={lastSequence} UpdatedAt={lastUpdatedAt:O} TargetSequence={minSequence}");
    }

    private static async Task<List<JsonElement>> WaitForTelemetryResultsAsync(HttpClient client, TelemetryStageRecord stageRecord, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var from = stageRecord.OccurredAt.AddMinutes(-1).ToUniversalTime().ToString("O");
        var to = stageRecord.OccurredAt.AddMinutes(1).ToUniversalTime().ToString("O");
        var fromEncoded = Uri.EscapeDataString(from);
        var toEncoded = Uri.EscapeDataString(to);
        var url = $"/api/telemetry/{stageRecord.DeviceId}?from={fromEncoded}&to={toEncoded}&pointId={stageRecord.PointId}&limit=10";

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
                var list = await ReadTelemetryResultsAsync(client, payload);
                if (list.Count > 0)
                {
                    return list;
                }
            }

            await Task.Delay(200);
        }

        return new List<JsonElement>();
    }

    private static async Task<List<JsonElement>> ReadTelemetryResultsAsync(HttpClient client, JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            return payload.EnumerateArray().ToList();
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new List<JsonElement>();
        }

        var mode = GetPropertyCaseInsensitive(payload, "mode").GetString();
        if (string.Equals(mode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            var items = GetPropertyCaseInsensitive(payload, "items");
            if (items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray().ToList();
            }
        }

        if (string.Equals(mode, "url", StringComparison.OrdinalIgnoreCase))
        {
            var url = GetPropertyCaseInsensitive(payload, "url").GetString();
            if (!string.IsNullOrWhiteSpace(url))
            {
                using var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return new List<JsonElement>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                var results = new List<JsonElement>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    using var doc = JsonDocument.Parse(line);
                    results.Add(doc.RootElement.Clone());
                }

                return results;
            }
        }

        return new List<JsonElement>();
    }

    private static JsonElement GetPropertyCaseInsensitive(JsonElement element, string name)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }

        throw new InvalidOperationException($"Missing property '{name}'.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-e2e-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record PointSnapshotResult(PointSnapshot Snapshot, DateTimeOffset ReadAt, double LagMilliseconds);

    private sealed record DeviceSnapshotResult(long LastSequence, DateTimeOffset UpdatedAt, string PropertiesJson);
}
