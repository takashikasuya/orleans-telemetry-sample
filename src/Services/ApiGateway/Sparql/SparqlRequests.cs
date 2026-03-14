namespace ApiGateway.Sparql;

/// <summary>
/// Represents a SPARQL query request payload.
/// </summary>
public sealed class SparqlQueryRequest
{
    /// <summary>
    /// Gets or sets the SPARQL query text.
    /// </summary>
    public string Query { get; set; } = string.Empty;
}

/// <summary>
/// Represents an RDF load request payload.
/// </summary>
public sealed class SparqlLoadRequest
{
    /// <summary>
    /// Gets or sets the RDF content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the RDF format.
    /// </summary>
    public string Format { get; set; } = "turtle";
}
