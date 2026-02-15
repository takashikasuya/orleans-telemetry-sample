using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class AuthenticationTests
{
    static AuthenticationTests()
    {
        Environment.SetEnvironmentVariable("Orleans__DisableClient", "true");
    }

    [Fact]
    public async Task RequestWithoutAuthorizationHeader_IsRejected()
    {
        var clusterMock = new Mock<IClusterClient>();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Clear();

        var response = await client.GetAsync("/api/nodes/node-1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidAuthorizationScheme_IsRejected()
    {
        var clusterMock = new Mock<IClusterClient>();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");

        var response = await client.GetAsync("/api/nodes/node-1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthorizedRequest_UsesTenantFromHeaderWhenResolvingGrains()
    {
        var nodeId = "node-tenant";
        var nodeSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = GraphNodeType.Point
            }
        };

        var nodeGrainMock = new Mock<IGraphNodeGrain>();
        nodeGrainMock.Setup(g => g.GetAsync()).ReturnsAsync(nodeSnapshot);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>("tenant-b:node-tenant", It.IsAny<string?>()))
            .Returns(nodeGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "tenant=tenant-b");

        var response = await client.GetAsync($"/api/nodes/{nodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        clusterMock.Verify(c => c.GetGrain<IGraphNodeGrain>("tenant-b:node-tenant", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task AuthorizedRequest_WithoutTenantHeader_UsesDefaultTenant()
    {
        var nodeId = "node-default";
        var nodeSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = GraphNodeType.Point
            }
        };

        var nodeGrainMock = new Mock<IGraphNodeGrain>();
        nodeGrainMock.Setup(g => g.GetAsync()).ReturnsAsync(nodeSnapshot);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>("t1:node-default", It.IsAny<string?>()))
            .Returns(nodeGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "no-tenant");

        var response = await client.GetAsync($"/api/nodes/{nodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        clusterMock.Verify(c => c.GetGrain<IGraphNodeGrain>("t1:node-default", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task AuthorizedRequest_ParsesTenantWhenHeaderContainsAdditionalSegments()
    {
        var nodeId = "node-segmented";
        var nodeSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = GraphNodeType.Point
            }
        };

        var nodeGrainMock = new Mock<IGraphNodeGrain>();
        nodeGrainMock.Setup(g => g.GetAsync()).ReturnsAsync(nodeSnapshot);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>("tenant-c:node-segmented", It.IsAny<string?>()))
            .Returns(nodeGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "tenant=tenant-c;role=admin");

        var response = await client.GetAsync($"/api/nodes/{nodeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        clusterMock.Verify(c => c.GetGrain<IGraphNodeGrain>("tenant-c:node-segmented", It.IsAny<string?>()), Times.Once);
    }
}
