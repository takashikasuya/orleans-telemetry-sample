using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Orleans;

namespace ApiGateway.Tests;

internal sealed class ApiGatewayTestFactory : WebApplicationFactory<global::Program>
{
    private readonly Mock<IClusterClient> _clusterMock;
    private readonly IReadOnlyDictionary<string, string?> _extraConfig;

    public ApiGatewayTestFactory(
        Mock<IClusterClient> clusterMock,
        IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        _clusterMock = clusterMock;
        _extraConfig = extraConfig ?? new Dictionary<string, string?>();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("Orleans__DisableClient", "true");

        builder.ConfigureAppConfiguration(config =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Orleans:DisableClient"] = "true"
            };

            foreach (var pair in _extraConfig)
            {
                values[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(values);
        });

        builder.ConfigureServices(services =>
        {
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

            services.AddSingleton(_ => _clusterMock.Object);
        });

        return base.CreateHost(builder);
    }
}
