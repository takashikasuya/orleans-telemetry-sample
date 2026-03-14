using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.Sparql;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class SparqlEndpointTests
{
    [Fact]
    public async Task POST_api_sparql_query_with_select_returns_results()
    {
        var grainMock = new Mock<ISparqlQueryGrain>();
        grainMock.Setup(g => g.ExecuteQueryAsync("SELECT * WHERE { ?s ?p ?o } LIMIT 1", "tenant-a"))
            .ReturnsAsync(new SparqlQueryResult
            {
                Variables = new List<string> { "s" },
                Rows = new List<SparqlResultBinding>
                {
                    new() { Values = new Dictionary<string, string> { ["s"] = "urn:building1" } }
                }
            });

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<ISparqlQueryGrain>("sparql", It.IsAny<string?>()))
            .Returns(grainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "tenant=tenant-a");

        var response = await client.PostAsJsonAsync("/api/sparql/query", new SparqlQueryRequest
        {
            Query = "SELECT * WHERE { ?s ?p ?o } LIMIT 1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SparqlQueryResult>();
        body.Should().NotBeNull();
        body!.Rows.Should().ContainSingle();
        body.Rows[0].Values["s"].Should().Be("urn:building1");

        clusterMock.Verify(c => c.GetGrain<ISparqlQueryGrain>("sparql", It.IsAny<string?>()), Times.Once);
        grainMock.Verify(g => g.ExecuteQueryAsync("SELECT * WHERE { ?s ?p ?o } LIMIT 1", "tenant-a"), Times.Once);
    }

    [Fact]
    public async Task POST_api_sparql_load_without_auth_returns_401()
    {
        var clusterMock = new Mock<IClusterClient>();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Clear();

        var response = await client.PostAsJsonAsync("/api/sparql/load", new SparqlLoadRequest
        {
            Content = "@prefix brick: <https://brickschema.org/schema/Brick#> . <urn:b> a brick:Building .",
            Format = "turtle"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_api_sparql_stats_returns_triple_count()
    {
        var grainMock = new Mock<ISparqlQueryGrain>();
        grainMock.Setup(g => g.GetTripleCountAsync("tenant-z")).ReturnsAsync(42);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<ISparqlQueryGrain>("sparql", It.IsAny<string?>()))
            .Returns(grainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "tenant=tenant-z");

        var response = await client.GetAsync("/api/sparql/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SparqlStatsResponse>();
        body.Should().NotBeNull();
        body!.TripleCount.Should().Be(42);
        grainMock.Verify(g => g.GetTripleCountAsync("tenant-z"), Times.Once);
    }
}
