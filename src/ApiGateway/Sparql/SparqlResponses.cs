namespace ApiGateway.Sparql;

/// <summary>
/// Represents SPARQL storage statistics.
/// </summary>
/// <param name="TripleCount">Number of stored triples.</param>
public sealed record SparqlStatsResponse(int TripleCount);
