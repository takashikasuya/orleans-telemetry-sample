namespace ApiGateway.Services;

public sealed class ApiRequestLoggingOptions
{
    public bool Enabled { get; set; } = true;

    public string DeviceId { get; set; } = "api-gateway";

    public string PointId { get; set; } = "http-request";

    public string[]? EnabledSinks { get; set; }

    public int MaxBodyLength { get; set; } = 2048;
}
