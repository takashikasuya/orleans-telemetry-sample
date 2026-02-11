namespace TelemetryClient.Services;

public sealed class TelemetryClientOidcOptions
{
    public string Authority { get; set; } = string.Empty;
    public string? TokenEndpoint { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Audience { get; set; }
    public string? Scope { get; set; }
    public int RefreshSkewSeconds { get; set; } = 60;
}
