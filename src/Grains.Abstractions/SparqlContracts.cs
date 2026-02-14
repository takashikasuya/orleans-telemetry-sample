using System.Collections.Generic;
using Orleans;

namespace Grains.Abstractions;

public interface ISparqlQueryGrain : IGrainWithStringKey
{
    Task LoadRdfAsync(string rdfContent, string format, string tenantId);
    Task<SparqlQueryResult> ExecuteQueryAsync(string sparqlQuery, string tenantId);
    Task<int> GetTripleCountAsync(string tenantId);
    Task ClearAsync(string tenantId);
}

[GenerateSerializer]
public sealed class SparqlQueryResult
{
    [Id(0)] public bool IsBooleanResult { get; set; }
    [Id(1)] public bool BooleanResult { get; set; }
    [Id(2)] public List<string> Variables { get; set; } = new();
    [Id(3)] public List<SparqlResultBinding> Rows { get; set; } = new();
}

[GenerateSerializer]
public sealed class SparqlResultBinding
{
    [Id(0)] public Dictionary<string, string> Values { get; set; } = new();
}
