using MudBlazor.Services;
using TelemetryClient.Services;
using TelemetryClient.Hubs;
using Orleans;
using Orleans.Configuration;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddMudServices();
builder.Services.Configure<TelemetryClientOidcOptions>(builder.Configuration.GetSection("TelemetryClient:Oidc"));
builder.Services.AddHttpClient("OidcClient");
builder.Services.AddSingleton<OidcTokenProvider>();
builder.Services.AddTransient<ApiGatewayAuthHandler>();
var apiGatewayBaseAddress = builder.Configuration.GetValue<Uri?>("TelemetryClient:ApiGatewayBaseAddress") ?? new Uri("http://localhost:8080");

// Configure Orleans Client for real-time updates
var orleansHost = builder.Configuration["Orleans:GatewayHost"] ?? "127.0.0.1";
var orleansAddresses = Dns.GetHostAddresses(orleansHost);
var orleansAddress = orleansAddresses.Length > 0 ? orleansAddresses[0] : IPAddress.Loopback;
var orleansPort = int.TryParse(builder.Configuration["Orleans:GatewayPort"], out var parsedPort) ? parsedPort : 30000;

builder.Host.UseOrleansClient(client =>
{
    client.UseStaticClustering(new IPEndPoint(orleansAddress, orleansPort));
    client.Configure<ClusterOptions>(opts =>
    {
        opts.ClusterId = "telemetry-cluster";
        opts.ServiceId = "telemetry-service";
    });
    client.AddMemoryStreams("PointUpdates");
});

// Data access services with typed HttpClients
builder.Services.AddHttpClient<RegistryService>(client =>
{
    client.BaseAddress = apiGatewayBaseAddress;
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddHttpClient<GraphTraversalService>(client =>
{
    client.BaseAddress = apiGatewayBaseAddress;
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddHttpClient<DeviceService>(client =>
{
    client.BaseAddress = apiGatewayBaseAddress;
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddHttpClient<TelemetryService>(client =>
{
    client.BaseAddress = apiGatewayBaseAddress;
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddHttpClient<ControlService>(client =>
{
    client.BaseAddress = apiGatewayBaseAddress;
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddScoped<HierarchyTreeService>();
builder.Services.AddAuthorizationCore();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapBlazorHub();
app.MapHub<TelemetryHub>("/telemetryHub");
app.MapFallbackToPage("/_Host");

app.Run();
