namespace ApiGateway.Client;

public sealed class ApiClientOptions
{
    public ApiOptions Api { get; set; } = new();
    public OidcOptions Oidc { get; set; } = new();
    public RegistryOptions Registry { get; set; } = new();
    public GraphOptions Graph { get; set; } = new();
    public TelemetryOptions Telemetry { get; set; } = new();
    public ReportOptions Report { get; set; } = new();
}

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class OidcOptions
{
    public string Authority { get; set; } = "http://localhost:8081/default";
    public string Audience { get; set; } = "default";
    public string ClientId { get; set; } = "test-client";
    public string ClientSecret { get; set; } = "test-secret";
    public string? TokenEndpoint { get; set; }
    public string? Scope { get; set; }
}

public sealed class RegistryOptions
{
    public int Limit { get; set; } = 5;
}

public sealed class GraphOptions
{
    public int TraverseDepth { get; set; } = 2;
}

public sealed class TelemetryOptions
{
    public int HistoryMinutes { get; set; } = 15;
    public int Limit { get; set; } = 50;
}

public sealed class ReportOptions
{
    public string OutputDir { get; set; } = "reports";
    public int ResponsePreviewChars { get; set; } = 4000;
}
