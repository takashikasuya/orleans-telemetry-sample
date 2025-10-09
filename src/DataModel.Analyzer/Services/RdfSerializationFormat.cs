namespace DataModel.Analyzer.Services;

/// <summary>
/// サポートされる RDF シリアライゼーション形式
/// </summary>
public enum RdfSerializationFormat
{
    Turtle,
    Notation3,
    NTriples,
    RdfXml,
    JsonLd,
    TriG,
    TriX,
    NQuads
}
