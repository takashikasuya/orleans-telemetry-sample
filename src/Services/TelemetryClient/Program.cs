using MudBlazor.Services;
using TelemetryClient.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
builder.Services.Configure<TelemetryClientOidcOptions>(builder.Configuration.GetSection("TelemetryClient:Oidc"));
builder.Services.AddHttpClient("OidcClient");
builder.Services.AddSingleton<OidcTokenProvider>();
builder.Services.AddTransient<ApiGatewayAuthHandler>();
var apiGatewayBaseAddress = builder.Configuration.GetValue<Uri?>("TelemetryClient:ApiGatewayBaseAddress") ?? new Uri("http://localhost:8080");

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
app.MapFallbackToPage("/_Host");

app.Run();
