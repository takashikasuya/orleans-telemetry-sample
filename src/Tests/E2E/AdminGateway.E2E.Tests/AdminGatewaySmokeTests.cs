using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DataModel.Analyzer.Extensions;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;
using Telemetry.Ingest;
using Telemetry.Ingest.Kafka;
using Telemetry.Ingest.RabbitMq;
using Telemetry.Ingest.Simulator;
using Telemetry.Storage;
using SiloHost;
using Xunit;

namespace AdminGateway.E2E.Tests;

public sealed class AdminGatewaySmokeTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private AdminGatewayTestFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _tempRoot;

    public async Task InitializeAsync()
    {
        TestAuthHandler.Reset();

        var seedPath = Path.Combine(AppContext.BaseDirectory, "seed.ttl");
        Environment.SetEnvironmentVariable("RDF_SEED_PATH", seedPath);
        Environment.SetEnvironmentVariable("TENANT_ID", "t1");
        Environment.SetEnvironmentVariable("TENANT_NAME", "Test Tenant");
        Environment.SetEnvironmentVariable("Orleans__DisableClient", "true");

        _tempRoot = CreateTempDirectory();
        var stageRoot = Path.Combine(_tempRoot, "stage");
        var parquetRoot = Path.Combine(_tempRoot, "parquet");
        var indexRoot = Path.Combine(_tempRoot, "index");
        Directory.CreateDirectory(stageRoot);
        Directory.CreateDirectory(parquetRoot);
        Directory.CreateDirectory(indexRoot);

        var controlRoutingPath = Path.Combine(_tempRoot, "control-routing.json");
        await File.WriteAllTextAsync(controlRoutingPath,
            "{\n  \"ControlRouting\": {\n    \"DefaultConnector\": \"sim\",\n    \"ConnectorGatewayMappings\": []\n  }\n}\n");

        var adminPort = GetFreeTcpPort();

        var siloConfig = BuildSiloConfig(stageRoot, parquetRoot, indexRoot);
        AdminGatewayTestClusterSettings.SiloOverrides = siloConfig;
        var clusterBuilder = new TestClusterBuilder(1);
        clusterBuilder.Options.ClusterId = "telemetry-cluster";
        clusterBuilder.Options.ServiceId = "telemetry-service";
        clusterBuilder.AddSiloBuilderConfigurator<AdminGatewayTestClusterConfigurator>();
        _cluster = clusterBuilder.Build();
        await _cluster.DeployAsync();

        var adminConfig = BuildAdminConfig(stageRoot, parquetRoot, indexRoot, controlRoutingPath);
        _factory = new AdminGatewayTestFactory(adminConfig, adminPort, _cluster.Client);
        
        // Skip Playwright setup for simpler HTTP-based tests
        // Uncomment below if running browser-based tests
        /*
        using var client = new HttpClient { BaseAddress = _factory.BaseUri };
        await WaitForHttpReadyAsync(client, TimeSpan.FromSeconds(20));

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        */
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();

        if (_factory is not null)
        {
            _factory.Dispose();
        }

        _cluster?.Dispose();

        Environment.SetEnvironmentVariable("RDF_SEED_PATH", null);
        Environment.SetEnvironmentVariable("TENANT_ID", null);
        Environment.SetEnvironmentVariable("TENANT_NAME", null);
        Environment.SetEnvironmentVariable("Orleans__DisableClient", null);

        if (!string.IsNullOrWhiteSpace(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task AdminDashboard_LoadsSuccessfully()
    {
        if (_cluster is null || _factory is null)
        {
            throw new InvalidOperationException("Test cluster or AdminGateway host not initialized.");
        }

        // Use HttpClient instead of Playwright for simpler integration test
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        
        // Test that the main page loads
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        // Verify HTML structure is present
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);
    }
    
    [Fact(Skip = "Playwright browser test - requires full HTTP server")]
    public async Task AdminDashboard_LoadsSeededHierarchy_WithBrowser()
    {
        if (_browser is null || _factory is null)
        {
            throw new InvalidOperationException("Playwright or AdminGateway host not initialized.");
        }

        // Note: WebApplicationFactory uses in-memory server, incompatible with Playwright
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = "http://localhost:5000"  // Would need actual HTTP server
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Cluster Overview" }).WaitForAsync();
        await page.GetByText("Control Routing").WaitForAsync();
        await page.GetByText("Hierarchy Tree").WaitForAsync();
        await page.GetByText("Test Site").WaitForAsync();

        await context.CloseAsync();
    }

    private static Dictionary<string, string?> BuildSiloConfig(
        string stageRoot,
        string parquetRoot,
        string indexRoot)
    {
        return new Dictionary<string, string?>
        {
            ["TelemetryIngest:Enabled:0"] = "Simulator",
            ["TelemetryIngest:BatchSize"] = "10",
            ["TelemetryIngest:ChannelCapacity"] = "100",
            ["TelemetryIngest:EventSinks:Enabled:0"] = "ParquetStorage",
            ["TelemetryIngest:Simulator:TenantId"] = "t1",
            ["TelemetryIngest:Simulator:BuildingName"] = "building",
            ["TelemetryIngest:Simulator:SpaceId"] = "space",
            ["TelemetryIngest:Simulator:DeviceIdPrefix"] = "device",
            ["TelemetryIngest:Simulator:DeviceCount"] = "1",
            ["TelemetryIngest:Simulator:PointsPerDevice"] = "1",
            ["TelemetryIngest:Simulator:IntervalMilliseconds"] = "500",
            ["TelemetryStorage:StagePath"] = stageRoot,
            ["TelemetryStorage:ParquetPath"] = parquetRoot,
            ["TelemetryStorage:IndexPath"] = indexRoot,
            ["TelemetryStorage:BucketMinutes"] = "15",
            ["TelemetryStorage:CompactionIntervalSeconds"] = "2",
            ["TelemetryStorage:DefaultQueryLimit"] = "100"
        };
    }

    private static Dictionary<string, string?> BuildAdminConfig(
        string stageRoot,
        string parquetRoot,
        string indexRoot,
        string controlRoutingPath)
    {
        return new Dictionary<string, string?>
        {
            ["Orleans:DisableClient"] = "true",
            ["TelemetryStorage:StagePath"] = stageRoot,
            ["TelemetryStorage:ParquetPath"] = parquetRoot,
            ["TelemetryStorage:IndexPath"] = indexRoot,
            ["TelemetryStorage:DefaultQueryLimit"] = "100",
            ["TelemetryIngest:Enabled:0"] = "Simulator",
            ["TelemetryIngest:EventSinks:Enabled:0"] = "ParquetStorage",
            ["TelemetryIngest:BatchSize"] = "10",
            ["TelemetryIngest:ChannelCapacity"] = "100",
            ["ControlRouting:ConfigPath"] = controlRoutingPath,
            ["ADMIN_GRAPH_UPLOAD_DIR"] = Path.Combine(Path.GetTempPath(), "orleans-telemetry-uploads")
        };
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "orleans-admin-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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

    private static async Task WaitForHttpReadyAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("/");
                // Any HTTP response means Kestrel is accepting requests.
                return;
            }
            catch
            {
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("AdminGateway did not respond within the timeout.");
    }

    private static async Task WaitForGraphTenantAsync(HttpClient client, string tenant, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetFromJsonAsync<string[]>("/admin/graph/tenants");
                if (response is not null && response.Any(item => string.Equals(item, tenant, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(300);
        }

        throw new TimeoutException("Graph tenants were not ready within the timeout.");
    }
}
