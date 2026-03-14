using System.Collections.Generic;
using Orleans;

namespace Grains.Abstractions;

/// <summary>
/// Grain contract for tenant-scoped SPARQL storage and query operations.
/// </summary>
public interface ISparqlQueryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Loads RDF content for a tenant.
    /// </summary>
    /// <param name="rdfContent">RDF content body.</param>
    /// <param name="format">RDF format (for example, turtle).</param>
    /// <param name="tenantId">Tenant identifier.</param>
    Task LoadRdfAsync(string rdfContent, string format, string tenantId);

    /// <summary>
    /// Executes a SPARQL query for a tenant.
    /// </summary>
    /// <param name="sparqlQuery">SPARQL query text.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <returns>Query result payload.</returns>
    Task<SparqlQueryResult> ExecuteQueryAsync(string sparqlQuery, string tenantId);

    /// <summary>
    /// Gets triple count for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <returns>Triple count.</returns>
    Task<int> GetTripleCountAsync(string tenantId);

    /// <summary>
    /// Clears all loaded RDF data for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    Task ClearAsync(string tenantId);
}

/// <summary>
/// Represents a SPARQL query result.
/// </summary>
[GenerateSerializer]
public sealed class SparqlQueryResult
{
    /// <summary>
    /// Gets or sets whether the result is boolean.
    /// </summary>
    [Id(0)] public bool IsBooleanResult { get; set; }

    /// <summary>
    /// Gets or sets the boolean result value.
    /// </summary>
    [Id(1)] public bool BooleanResult { get; set; }

    /// <summary>
    /// Gets or sets variable names in the result set.
    /// </summary>
    [Id(2)] public List<string> Variables { get; set; } = new();

    /// <summary>
    /// Gets or sets tabular result rows.
    /// </summary>
    [Id(3)] public List<SparqlResultBinding> Rows { get; set; } = new();
}

/// <summary>
/// Represents one row binding in a SPARQL result set.
/// </summary>
[GenerateSerializer]
public sealed class SparqlResultBinding
{
    /// <summary>
    /// Gets or sets bound values by variable name.
    /// </summary>
    [Id(0)] public Dictionary<string, string> Values { get; set; } = new();
}
