using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApiGateway.Client;
using Microsoft.Extensions.Configuration;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        var configPath = GetArgValue(args, "--config") ?? "appsettings.json";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = configuration.GetSection("ApiClient").Get<ApiClientOptions>() ?? new ApiClientOptions();
        ApplyOverrides(args, options);

        var report = new ApiClientReport
        {
            RunId = $"api-client-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            StartedAt = DateTimeOffset.UtcNow,
            ApiBaseUrl = options.Api.BaseUrl,
            OidcAuthority = options.Oidc.Authority
        };

        try
        {
            using var oidcClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.Api.TimeoutSeconds) };
            var tokenResult = await AcquireTokenAsync(oidcClient, options, report);
            report.TokenType = tokenResult.TokenType;
            report.TokenExpiresIn = tokenResult.ExpiresIn;
            report.TenantId = tokenResult.TenantId;

            using var apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.Api.TimeoutSeconds) };
            apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

            var registryLimit = options.Registry.Limit;
            var sites = await FetchRegistryAsync(apiClient, report, options, "sites", registryLimit);
            var buildings = await FetchRegistryAsync(apiClient, report, options, "buildings", registryLimit);
            var devices = await FetchRegistryAsync(apiClient, report, options, "devices", registryLimit);
            var points = await FetchRegistryAsync(apiClient, report, options, "points", registryLimit);

            report.Registry.SitesCount = sites.TotalCount;
            report.Registry.BuildingsCount = buildings.TotalCount;
            report.Registry.DevicesCount = devices.TotalCount;
            report.Registry.PointsCount = points.TotalCount;

            report.Registry.SelectedSiteNodeId = sites.Items.FirstOrDefault()?.NodeId;
            report.Registry.SelectedBuildingNodeId = buildings.Items.FirstOrDefault()?.NodeId;
            report.Registry.SelectedPointNodeId = points.Items.FirstOrDefault()?.NodeId;

            var graphNodeId = report.Registry.SelectedPointNodeId
                ?? report.Registry.SelectedBuildingNodeId
                ?? report.Registry.SelectedSiteNodeId;

            if (string.IsNullOrWhiteSpace(graphNodeId))
            {
                throw new InvalidOperationException("No graph node found from registry responses.");
            }

            var nodeResponse = await FetchGraphNodeAsync(apiClient, report, options, graphNodeId);
            if (nodeResponse?.Node?.Attributes is null || nodeResponse.Node.Attributes.Count == 0)
            {
                var resolved = await ResolveNodeWithAttributesAsync(apiClient, report, options, points.Items);
                if (resolved is not null)
                {
                    graphNodeId = resolved.NodeId;
                    nodeResponse = resolved.Node;
                    report.Registry.SelectedPointNodeId = graphNodeId;
                }
            }
            report.Graph.NodeId = nodeResponse?.Node?.NodeId;
            report.Graph.NodeAttributes = nodeResponse?.Node?.Attributes ?? new Dictionary<string, string>();
            report.Graph.OutgoingEdgeCount = nodeResponse?.OutgoingEdges?.Count ?? 0;
            report.Graph.IncomingEdgeCount = nodeResponse?.IncomingEdges?.Count ?? 0;
            report.Graph.Relationships = nodeResponse?.OutgoingEdges
                ?.Select(edge => new GraphEdgeSummary { Predicate = edge.Predicate, TargetNodeId = edge.TargetNodeId })
                .ToList() ?? new List<GraphEdgeSummary>();

            var traversal = await FetchGraphTraversalAsync(apiClient, report, options, graphNodeId, options.Graph.TraverseDepth);
            report.Graph.TraversedNodeCount = traversal?.Nodes?.Count ?? 0;

            if (report.Graph.NodeAttributes is null || report.Graph.NodeAttributes.Count == 0)
            {
                throw new InvalidOperationException("Graph node attributes are empty; cannot resolve device/point identifiers.");
            }

            report.Telemetry.DeviceId = TryGetAttribute(report.Graph.NodeAttributes, "DeviceId");
            report.Telemetry.PointId = TryGetAttribute(report.Graph.NodeAttributes, "PointId");

            if (string.IsNullOrWhiteSpace(report.Telemetry.DeviceId))
            {
                report.Telemetry.DeviceId = devices.Items.FirstOrDefault()?.Attributes?.GetValueOrDefault("DeviceId");
            }

            if (string.IsNullOrWhiteSpace(report.Telemetry.DeviceId))
            {
                throw new InvalidOperationException("DeviceId could not be resolved from graph attributes or registry devices.");
            }

            if (string.IsNullOrWhiteSpace(report.Telemetry.PointId))
            {
                throw new InvalidOperationException("PointId could not be resolved from graph attributes.");
            }

            var pointSnapshot = await FetchPointSnapshotAsync(apiClient, report, options, graphNodeId);
            if (pointSnapshot is not null)
            {
                report.Telemetry.PointLastSequence = pointSnapshot.LastSequence;
                report.Telemetry.PointUpdatedAt = pointSnapshot.UpdatedAt;
                report.Telemetry.PointLatestValueJson = JsonSerializer.Serialize(pointSnapshot.LatestValue, JsonOptions);
                report.Telemetry.PointSnapshotJson = JsonSerializer.Serialize(pointSnapshot, JsonOptions);
            }

            var deviceSnapshot = await FetchDeviceSnapshotAsync(apiClient, report, options, report.Telemetry.DeviceId);
            if (deviceSnapshot is not null)
            {
                report.Telemetry.DeviceLastSequence = deviceSnapshot.LastSequence;
                report.Telemetry.DeviceUpdatedAt = deviceSnapshot.UpdatedAt;
                if (deviceSnapshot.Properties is not null)
                {
                    report.Telemetry.DevicePropertiesJson = JsonSerializer.Serialize(deviceSnapshot.Properties, JsonOptions);
                }
                report.Telemetry.DeviceSnapshotJson = JsonSerializer.Serialize(deviceSnapshot, JsonOptions);
            }

            var history = await FetchTelemetryHistoryAsync(
                apiClient,
                report,
                options,
                report.Telemetry.DeviceId,
                report.Telemetry.PointId,
                options.Telemetry.HistoryMinutes,
                options.Telemetry.Limit);

            report.Telemetry.HistoryResultCount = history.Count;
            report.Telemetry.HistoryMode = history.Mode;
            report.Telemetry.HistoryExportUrl = history.ExportUrl;
            report.Telemetry.HistoryFirstResultJson = history.FirstResultJson;
            report.Telemetry.HistorySamplesJson = history.SamplesJson;

            report.Status = "Passed";
            return 0;
        }
        catch (Exception ex)
        {
            report.Status = "Failed";
            report.Error = ex.ToString();
            return 1;
        }
        finally
        {
            report.CompletedAt = DateTimeOffset.UtcNow;
            var outputDir = ResolveOutputDir(options.Report.OutputDir);
            await ApiClientReportWriter.WriteAsync(report, outputDir, CancellationToken.None);
            Console.WriteLine($"Report written: {outputDir}");
        }
    }

    private static string ResolveOutputDir(string outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = "reports";
        }

        return Path.IsPathRooted(outputDir)
            ? outputDir
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), outputDir));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project src/ApiGateway.Client -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --config <path>            Config file path (default: appsettings.json)");
        Console.WriteLine("  --api-base <url>           ApiGateway base URL");
        Console.WriteLine("  --authority <url>          OIDC authority URL");
        Console.WriteLine("  --token-endpoint <url>     OIDC token endpoint URL");
        Console.WriteLine("  --client-id <id>           OIDC client id");
        Console.WriteLine("  --client-secret <secret>   OIDC client secret");
        Console.WriteLine("  --scope <scope>            OIDC scope (optional)");
        Console.WriteLine("  --registry-limit <n>       Registry query limit");
        Console.WriteLine("  --graph-depth <n>          Graph traversal depth");
        Console.WriteLine("  --history-minutes <n>      Telemetry history window (minutes)");
        Console.WriteLine("  --telemetry-limit <n>      Telemetry history limit");
        Console.WriteLine("  --report-dir <path>        Report output directory");
    }

    private static void ApplyOverrides(string[] args, ApiClientOptions options)
    {
        var apiBase = GetArgValue(args, "--api-base");
        if (!string.IsNullOrWhiteSpace(apiBase))
        {
            options.Api.BaseUrl = apiBase;
        }

        var authority = GetArgValue(args, "--authority");
        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Oidc.Authority = authority;
        }

        var tokenEndpoint = GetArgValue(args, "--token-endpoint");
        if (!string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            options.Oidc.TokenEndpoint = tokenEndpoint;
        }

        var clientId = GetArgValue(args, "--client-id");
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            options.Oidc.ClientId = clientId;
        }

        var clientSecret = GetArgValue(args, "--client-secret");
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            options.Oidc.ClientSecret = clientSecret;
        }

        var scope = GetArgValue(args, "--scope");
        if (!string.IsNullOrWhiteSpace(scope))
        {
            options.Oidc.Scope = scope;
        }

        if (int.TryParse(GetArgValue(args, "--registry-limit"), out var registryLimit))
        {
            options.Registry.Limit = registryLimit;
        }

        if (int.TryParse(GetArgValue(args, "--graph-depth"), out var graphDepth))
        {
            options.Graph.TraverseDepth = graphDepth;
        }

        if (int.TryParse(GetArgValue(args, "--history-minutes"), out var historyMinutes))
        {
            options.Telemetry.HistoryMinutes = historyMinutes;
        }

        if (int.TryParse(GetArgValue(args, "--telemetry-limit"), out var telemetryLimit))
        {
            options.Telemetry.Limit = telemetryLimit;
        }

        var reportDir = GetArgValue(args, "--report-dir");
        if (!string.IsNullOrWhiteSpace(reportDir))
        {
            options.Report.OutputDir = reportDir;
        }
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }
                return string.Empty;
            }

            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }
        }

        return null;
    }

    private static async Task<TokenResult> AcquireTokenAsync(HttpClient client, ApiClientOptions options, ApiClientReport report)
    {
        var authority = options.Oidc.Authority;
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("OIDC authority is required.");
        }

        var tokenEndpoint = options.Oidc.TokenEndpoint;
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            tokenEndpoint = await ResolveTokenEndpointAsync(client, authority, report, options.Report.ResponsePreviewChars);
        }

        report.TokenEndpoint = tokenEndpoint;

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials")
        };

        if (!string.IsNullOrWhiteSpace(options.Oidc.ClientId))
        {
            form.Add(new KeyValuePair<string, string>("client_id", options.Oidc.ClientId));
        }

        if (!string.IsNullOrWhiteSpace(options.Oidc.Scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", options.Oidc.Scope));
        }

        if (!string.IsNullOrWhiteSpace(options.Oidc.Audience))
        {
            form.Add(new KeyValuePair<string, string>("audience", options.Oidc.Audience));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };

        if (!string.IsNullOrWhiteSpace(options.Oidc.ClientSecret))
        {
            var basicValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Oidc.ClientId}:{options.Oidc.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
        }

        var response = await SendAsync(client, report, options, "oidc-token", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Failed to acquire token: HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(response.Body);
        var root = doc.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var tokenProp) ? tokenProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Token response missing access_token.");
        }

        var tokenType = root.TryGetProperty("token_type", out var typeProp) ? typeProp.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expProp) && expProp.TryGetInt32(out var expVal)
            ? expVal
            : (int?)null;

        var tenantId = TryGetTenantFromJwt(accessToken);

        return new TokenResult(accessToken, tokenType, expiresIn, tenantId);
    }

    private static async Task<string> ResolveTokenEndpointAsync(
        HttpClient client,
        string authority,
        ApiClientReport report,
        int previewChars)
    {
        var discoveryUrl = CombineUrl(authority, ".well-known/openid-configuration");
        using var request = new HttpRequestMessage(HttpMethod.Get, discoveryUrl);
        var response = await SendAsync(client, report, previewChars, "oidc-discovery", request);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("token_endpoint", out var tokenProp))
            {
                var endpoint = tokenProp.GetString();
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    return endpoint;
                }
            }
        }

        return CombineUrl(authority, "token");
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return path;
        }

        if (baseUrl.EndsWith('/'))
        {
            return baseUrl + path.TrimStart('/');
        }

        return baseUrl + "/" + path.TrimStart('/');
    }

    private static async Task<RegistryFetchResult> FetchRegistryAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string nodeType,
        int limit)
    {
        var url = CombineUrl(options.Api.BaseUrl, $"api/registry/{nodeType}");
        if (limit > 0)
        {
            url += $"?limit={limit}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, $"registry-{nodeType}", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Registry {nodeType} failed: HTTP {(int)response.StatusCode}");
        }

        var registryResponse = JsonSerializer.Deserialize<RegistryQueryResponse>(response.Body, JsonOptions)
            ?? new RegistryQueryResponse();

        var items = registryResponse.Items ?? new List<RegistryNodeSummary>();
        if (string.Equals(registryResponse.Mode, "url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(registryResponse.Url))
        {
            var exportUrl = BuildAbsoluteUrl(options.Api.BaseUrl, registryResponse.Url);
            var exportItems = await DownloadRegistryExportAsync(client, report, options, exportUrl);
            items = exportItems;
        }

        return new RegistryFetchResult(registryResponse.TotalCount, items);
    }

    private static async Task<List<RegistryNodeSummary>> DownloadRegistryExportAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "registry-export", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Registry export download failed: HTTP {(int)response.StatusCode}");
        }

        var items = new List<RegistryNodeSummary>();
        using var reader = new StringReader(response.Body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<RegistryNodeSummary>(line, JsonOptions);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static async Task<GraphNodeResponse?> FetchGraphNodeAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string nodeId)
    {
        var encodedNode = Uri.EscapeDataString(nodeId);
        var url = CombineUrl(options.Api.BaseUrl, $"api/nodes/{encodedNode}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "graph-node", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Graph node fetch failed: HTTP {(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<GraphNodeResponse>(response.Body, JsonOptions);
    }

    private static async Task<ResolvedNode?> ResolveNodeWithAttributesAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        IEnumerable<RegistryNodeSummary> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.NodeId))
            {
                continue;
            }

            if (candidate.Attributes is not null
                && candidate.Attributes.TryGetValue("DeviceId", out var deviceId)
                && !string.IsNullOrWhiteSpace(deviceId)
                && candidate.Attributes.TryGetValue("PointId", out var pointId)
                && !string.IsNullOrWhiteSpace(pointId))
            {
                return new ResolvedNode(candidate.NodeId, new GraphNodeResponse
                {
                    Node = new GraphNodeDefinition
                    {
                        NodeId = candidate.NodeId,
                        Attributes = new Dictionary<string, string>(candidate.Attributes)
                    }
                });
            }

            var response = await FetchGraphNodeAsync(client, report, options, candidate.NodeId);
            var attrs = response?.Node?.Attributes;
            if (attrs is not null
                && attrs.TryGetValue("DeviceId", out var resolvedDeviceId)
                && !string.IsNullOrWhiteSpace(resolvedDeviceId)
                && attrs.TryGetValue("PointId", out var resolvedPointId)
                && !string.IsNullOrWhiteSpace(resolvedPointId))
            {
                return new ResolvedNode(candidate.NodeId, response);
            }
        }

        return null;
    }

    private static async Task<GraphTraversalResponse?> FetchGraphTraversalAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string nodeId,
        int depth)
    {
        var encodedNode = Uri.EscapeDataString(nodeId);
        var url = CombineUrl(options.Api.BaseUrl, $"api/graph/traverse/{encodedNode}?depth={depth}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "graph-traverse", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Graph traversal failed: HTTP {(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<GraphTraversalResponse>(response.Body, JsonOptions);
    }

    private static async Task<PointSnapshotResponse?> FetchPointSnapshotAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string nodeId)
    {
        var encodedNode = Uri.EscapeDataString(nodeId);
        var url = CombineUrl(options.Api.BaseUrl, $"api/nodes/{encodedNode}/value");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "point-snapshot", request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Point snapshot fetch failed: HTTP {(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<PointSnapshotResponse>(response.Body, JsonOptions);
    }

    private static async Task<DeviceSnapshotResponse?> FetchDeviceSnapshotAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string deviceId)
    {
        var encodedDevice = Uri.EscapeDataString(deviceId);
        var url = CombineUrl(options.Api.BaseUrl, $"api/devices/{encodedDevice}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "device-snapshot", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Device snapshot fetch failed: HTTP {(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<DeviceSnapshotResponse>(response.Body, JsonOptions);
    }

    private static async Task<TelemetryHistoryResult> FetchTelemetryHistoryAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string deviceId,
        string pointId,
        int historyMinutes,
        int limit)
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddMinutes(-Math.Abs(historyMinutes));
        var query = new StringBuilder();
        query.Append($"from={Uri.EscapeDataString(from.ToString("O"))}");
        query.Append($"&to={Uri.EscapeDataString(now.ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(pointId))
        {
            query.Append($"&pointId={Uri.EscapeDataString(pointId)}");
        }

        if (limit > 0)
        {
            query.Append($"&limit={limit}");
        }

        var encodedDevice = Uri.EscapeDataString(deviceId);
        var url = CombineUrl(options.Api.BaseUrl, $"api/telemetry/{encodedDevice}?{query}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "telemetry-query", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Telemetry history fetch failed: HTTP {(int)response.StatusCode}");
        }

        var telemetryResponse = JsonSerializer.Deserialize<TelemetryQueryResponse>(response.Body, JsonOptions)
            ?? new TelemetryQueryResponse();

        var items = telemetryResponse.Items ?? new List<TelemetryQueryResult>();
        if (string.Equals(telemetryResponse.Mode, "url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(telemetryResponse.Url))
        {
            var exportUrl = BuildAbsoluteUrl(options.Api.BaseUrl, telemetryResponse.Url);
            var exportItems = await DownloadTelemetryExportAsync(client, report, options, exportUrl);
            items = exportItems;
        }

        var firstResult = items.FirstOrDefault();
        var firstJson = firstResult is null ? null : JsonSerializer.Serialize(firstResult, JsonOptions);

        var samples = items
            .Take(3)
            .Select(item => JsonSerializer.Serialize(item, JsonOptions))
            .ToList();

        return new TelemetryHistoryResult(
            telemetryResponse.Mode,
            telemetryResponse.Url,
            items.Count,
            firstJson,
            samples);
    }

    private static async Task<List<TelemetryQueryResult>> DownloadTelemetryExportAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(client, report, options, "telemetry-export", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Telemetry export download failed: HTTP {(int)response.StatusCode}");
        }

        var items = new List<TelemetryQueryResult>();
        using var reader = new StringReader(response.Body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<TelemetryQueryResult>(line, JsonOptions);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string BuildAbsoluteUrl(string baseUrl, string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return CombineUrl(baseUrl, relativeOrAbsolute);
    }

    private static string? TryGetAttribute(Dictionary<string, string>? attributes, string key)
    {
        if (attributes is null)
        {
            return null;
        }

        return attributes.TryGetValue(key, out var value) ? value : null;
    }

    private static string? TryGetTenantFromJwt(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1];
            var padded = payload.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            var bytes = Convert.FromBase64String(padded);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("tenant", out var tenantProp))
            {
                return tenantProp.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task<SendResponse> SendAsync(
        HttpClient client,
        ApiClientReport report,
        ApiClientOptions options,
        string name,
        HttpRequestMessage request)
    {
        return await SendAsync(client, report, options.Report.ResponsePreviewChars, name, request);
    }

    private static async Task<SendResponse> SendAsync(
        HttpClient client,
        ApiClientReport report,
        int previewChars,
        string name,
        HttpRequestMessage request)
    {
        var sw = Stopwatch.StartNew();
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        sw.Stop();

        var preview = body.Length > previewChars ? body[..previewChars] + "..." : body;
        report.ApiCalls.Add(new ApiCallLog
        {
            Name = name,
            Method = request.Method.Method,
            Url = request.RequestUri?.ToString() ?? string.Empty,
            StatusCode = (int)response.StatusCode,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            ResponseBytes = Encoding.UTF8.GetByteCount(body),
            ResponsePreview = preview
        });

        return new SendResponse(response.StatusCode, body);
    }

    private sealed record TokenResult(string AccessToken, string? TokenType, int? ExpiresIn, string? TenantId);

    private sealed record SendResponse(HttpStatusCode StatusCode, string Body);

    private sealed record RegistryFetchResult(int TotalCount, List<RegistryNodeSummary> Items);

    private sealed record TelemetryHistoryResult(string? Mode, string? ExportUrl, int Count, string? FirstResultJson, List<string> SamplesJson);

    private sealed record ResolvedNode(string NodeId, GraphNodeResponse Node);

    private sealed class RegistryQueryResponse
    {
        public string Mode { get; set; } = "inline";
        public int Count { get; set; }
        public int TotalCount { get; set; }
        public List<RegistryNodeSummary>? Items { get; set; }
        public string? Url { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed class RegistryNodeSummary
    {
        public string NodeId { get; set; } = string.Empty;
        public int NodeType { get; set; }
        public string? DisplayName { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }

    private sealed class GraphNodeResponse
    {
        public GraphNodeDefinition? Node { get; set; }
        public List<GraphEdge>? OutgoingEdges { get; set; }
        public List<GraphEdge>? IncomingEdges { get; set; }
        public Dictionary<string, PointSummary>? Points { get; set; }
    }

    private sealed class GraphNodeDefinition
    {
        public string NodeId { get; set; } = string.Empty;
        public int NodeType { get; set; }
        public string? DisplayName { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    private sealed class GraphEdge
    {
        public string Predicate { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;
    }

    private sealed class GraphTraversalResponse
    {
        public string? StartNodeId { get; set; }
        public int Depth { get; set; }
        public List<GraphNodeSnapshot>? Nodes { get; set; }
    }

    private sealed class GraphNodeSnapshot
    {
        public GraphNodeDefinition? Node { get; set; }
        public List<GraphEdge>? OutgoingEdges { get; set; }
        public List<GraphEdge>? IncomingEdges { get; set; }
    }

    private sealed class PointSummary
    {
        public object? Value { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class PointSnapshotResponse
    {
        public long LastSequence { get; set; }
        public object? LatestValue { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class DeviceSnapshotResponse
    {
        public string DeviceId { get; set; } = string.Empty;
        public long LastSequence { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Dictionary<string, JsonElement>? Properties { get; set; }
    }

    private sealed class TelemetryQueryResponse
    {
        public string Mode { get; set; } = "inline";
        public int Count { get; set; }
        public List<TelemetryQueryResult>? Items { get; set; }
        public string? Url { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed class TelemetryQueryResult
    {
        public string? TenantId { get; set; }
        public string? DeviceId { get; set; }
        public string? PointId { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public long Sequence { get; set; }
        public string? ValueJson { get; set; }
        public string? PayloadJson { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }
}
