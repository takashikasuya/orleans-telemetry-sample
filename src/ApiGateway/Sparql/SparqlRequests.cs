namespace ApiGateway.Sparql;

public sealed class SparqlQueryRequest
{
    public string Query { get; set; } = string.Empty;
}

public sealed class SparqlLoadRequest
{
    public string Content { get; set; } = string.Empty;
    public string Format { get; set; } = "turtle";
}
