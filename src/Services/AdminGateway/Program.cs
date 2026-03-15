using AdminGateway;

var builder = WebApplication.CreateBuilder(args);
var includeOrleansClient = !builder.Configuration.GetValue("Orleans:DisableClient", false);
AdminGatewayApp.Configure(builder, includeOrleansClient);
var app = builder.Build();
AdminGatewayApp.ConfigureApp(app);
app.Run();

public partial class Program
{
}
