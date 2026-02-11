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
builder.Services.AddHttpClient("ApiGateway", client =>
{
    client.BaseAddress = builder.Configuration.GetValue<Uri?>("TelemetryClient:ApiGatewayBaseAddress") ?? new Uri("http://localhost:8080");
}).AddHttpMessageHandler<ApiGatewayAuthHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiGateway"));
builder.Services.AddAuthorizationCore();

// Data access services
builder.Services.AddScoped<RegistryService>();
builder.Services.AddScoped<GraphTraversalService>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<ControlService>();
builder.Services.AddScoped<HierarchyTreeService>();

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
