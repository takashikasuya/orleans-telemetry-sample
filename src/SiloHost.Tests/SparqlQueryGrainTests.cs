using FluentAssertions;
using Xunit;

namespace SiloHost.Tests;

public sealed class SparqlQueryGrainTests
{
    private const string BuildingTurtle = """
        @prefix brick: <https://brickschema.org/schema/Brick#> .
        <urn:building1> a brick:Building .
        <urn:building1> brick:label "HQ" .
        """;

    [Fact]
    public async Task LoadRdfAsync_ParsesTurtleAndStoresTriples()
    {
        var persistence = new TestPersistentState<SparqlQueryGrain.SparqlState>(() => new SparqlQueryGrain.SparqlState());
        var grain = new SparqlQueryGrain(persistence);

        await grain.LoadRdfAsync(BuildingTurtle, "turtle", "t1");

        var count = await grain.GetTripleCountAsync("t1");
        count.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteQueryAsync_FiltersByTenant()
    {
        var persistence = new TestPersistentState<SparqlQueryGrain.SparqlState>(() => new SparqlQueryGrain.SparqlState());
        var grain = new SparqlQueryGrain(persistence);

        await grain.LoadRdfAsync("@prefix brick: <https://brickschema.org/schema/Brick#> . <urn:tenant-a-building> a brick:Building .", "turtle", "tenant-a");
        await grain.LoadRdfAsync("@prefix brick: <https://brickschema.org/schema/Brick#> . <urn:tenant-b-building> a brick:Building .", "turtle", "tenant-b");

        var query = "SELECT ?s WHERE { ?s a <https://brickschema.org/schema/Brick#Building> }";
        var tenantAResult = await grain.ExecuteQueryAsync(query, "tenant-a");
        var tenantBResult = await grain.ExecuteQueryAsync(query, "tenant-b");

        tenantAResult.Rows.Should().ContainSingle();
        tenantAResult.Rows[0].Values["s"].Should().Be("urn:tenant-a-building");
        tenantBResult.Rows.Should().ContainSingle();
        tenantBResult.Rows[0].Values["s"].Should().Be("urn:tenant-b-building");
    }

    [Fact]
    public async Task ExecuteQueryAsync_ReturnsBindings()
    {
        var persistence = new TestPersistentState<SparqlQueryGrain.SparqlState>(() => new SparqlQueryGrain.SparqlState());
        var grain = new SparqlQueryGrain(persistence);

        await grain.LoadRdfAsync(BuildingTurtle, "turtle", "t1");
        var query = "SELECT ?s ?label WHERE { ?s <https://brickschema.org/schema/Brick#label> ?label }";

        var result = await grain.ExecuteQueryAsync(query, "t1");

        result.IsBooleanResult.Should().BeFalse();
        result.Variables.Should().Contain(new[] { "s", "label" });
        result.Rows.Should().ContainSingle();
        result.Rows[0].Values["s"].Should().Be("urn:building1");
        result.Rows[0].Values["label"].Should().Be("HQ");
    }

    [Fact]
    public async Task ClearAsync_RemovesTenantTriples()
    {
        var persistence = new TestPersistentState<SparqlQueryGrain.SparqlState>(() => new SparqlQueryGrain.SparqlState());
        var grain = new SparqlQueryGrain(persistence);

        await grain.LoadRdfAsync(BuildingTurtle, "turtle", "t1");
        await grain.ClearAsync("t1");

        var count = await grain.GetTripleCountAsync("t1");
        count.Should().Be(0);
    }
}
