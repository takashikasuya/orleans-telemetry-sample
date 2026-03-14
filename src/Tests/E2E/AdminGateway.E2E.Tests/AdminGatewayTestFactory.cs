using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdminGateway;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace AdminGateway.E2E.Tests;

internal sealed class AdminGatewayTestFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configOverrides;
    private readonly IClusterClient _clusterClient;

    public AdminGatewayTestFactory(IReadOnlyDictionary<string, string?> configOverrides, int port, IClusterClient clusterClient)
    {
        _configOverrides = configOverrides;
        _clusterClient = clusterClient;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseStaticWebAssets();
        
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(_configOverrides);
        });

        builder.ConfigureServices((context, services) =>
        {
            // Configure host options
            services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(5));
            
            // Override authentication for testing
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
            });

            // Replace with test cluster client
            services.RemoveAll<IClusterClient>();
            services.RemoveAll<IGrainFactory>();
            services.AddSingleton(_clusterClient);
            services.AddSingleton<IGrainFactory>(sp => sp.GetRequiredService<IClusterClient>());
        });
    }
}
