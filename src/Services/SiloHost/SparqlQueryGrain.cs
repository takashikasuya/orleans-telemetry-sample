using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;

namespace SiloHost;

public sealed class SparqlQueryGrain : Grain, ISparqlQueryGrain
{
    private readonly IPersistentState<SparqlState> _state;

    public SparqlQueryGrain([PersistentState("sparql-state", "SparqlStore")] IPersistentState<SparqlState> state)
    {
        _state = state;
    }

    public async Task LoadRdfAsync(string rdfContent, string format, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(rdfContent))
        {
            throw new ArgumentException("RDF content must not be empty.", nameof(rdfContent));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));
        }

        var parser = CreateParser(format);
        var graph = new Graph();
        parser.Load(graph, new StringReader(rdfContent));

        if (!_state.State.TenantTriples.TryGetValue(tenantId, out var tenantTriples))
        {
            tenantTriples = new List<SerializedTriple>();
            _state.State.TenantTriples[tenantId] = tenantTriples;
        }

        foreach (var triple in graph.Triples)
        {
            tenantTriples.Add(SerializedTriple.FromTriple(triple));
        }

        await _state.WriteStateAsync();
    }

    public Task<SparqlQueryResult> ExecuteQueryAsync(string sparqlQuery, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(sparqlQuery))
        {
            throw new ArgumentException("SPARQL query must not be empty.", nameof(sparqlQuery));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));
        }

        var graph = BuildGraphForTenant(tenantId);
        var store = new TripleStore();
        store.Add(graph);

        var parser = new SparqlQueryParser();
        var query = parser.ParseFromString(sparqlQuery);
        var processor = new LeviathanQueryProcessor(store);
        var result = processor.ProcessQuery(query);

        if (result is SparqlResultSet resultSet)
        {
            var response = new SparqlQueryResult
            {
                IsBooleanResult = resultSet.ResultsType == SparqlResultsType.Boolean,
                BooleanResult = resultSet.Result,
                Variables = resultSet.Variables.ToList()
            };

            foreach (var row in resultSet)
            {
                var binding = new SparqlResultBinding();
                foreach (var variable in resultSet.Variables)
                {
                    if (row.TryGetValue(variable, out var node) && node != null)
                    {
                        binding.Values[variable] = node is ILiteralNode literal ? literal.Value : node.ToString();
                    }
                }

                response.Rows.Add(binding);
            }

            return Task.FromResult(response);
        }

        throw new InvalidOperationException("SPARQL query did not return a result set.");
    }

    public Task<int> GetTripleCountAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));
        }

        if (_state.State.TenantTriples.TryGetValue(tenantId, out var tenantTriples))
        {
            return Task.FromResult(tenantTriples.Count);
        }

        return Task.FromResult(0);
    }

    public async Task ClearAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));
        }

        if (_state.State.TenantTriples.Remove(tenantId))
        {
            await _state.WriteStateAsync();
        }
    }

    private static IRdfReader CreateParser(string format)
    {
        return format.Trim().ToLowerInvariant() switch
        {
            "ttl" or "turtle" => new TurtleParser(),
            "nt" or "ntriples" => new NTriplesParser(),
            "rdfxml" or "xml" => new RdfXmlParser(),
            _ => throw new ArgumentException($"Unsupported RDF format: {format}", nameof(format))
        };
    }

    private Graph BuildGraphForTenant(string tenantId)
    {
        var graph = new Graph();
        if (_state.State.TenantTriples.TryGetValue(tenantId, out var tenantTriples))
        {
            foreach (var triple in tenantTriples)
            {
                graph.Assert(triple.ToTriple(graph));
            }
        }

        return graph;
    }

    [GenerateSerializer]
    public sealed class SparqlState
    {
        [Id(0)] public Dictionary<string, List<SerializedTriple>> TenantTriples { get; set; } = new();
    }

    [GenerateSerializer]
    public sealed class SerializedTriple
    {
        [Id(0)] public string Subject { get; set; } = string.Empty;
        [Id(1)] public string Predicate { get; set; } = string.Empty;
        [Id(2)] public string ObjectValue { get; set; } = string.Empty;
        [Id(3)] public string ObjectNodeType { get; set; } = string.Empty;
        [Id(4)] public string DataType { get; set; } = string.Empty;
        [Id(5)] public string Language { get; set; } = string.Empty;

        public static SerializedTriple FromTriple(Triple triple)
        {
            var serialized = new SerializedTriple
            {
                Subject = triple.Subject.ToString(),
                Predicate = triple.Predicate.ToString(),
                ObjectValue = triple.Object.ToString(),
                ObjectNodeType = triple.Object.NodeType.ToString()
            };

            if (triple.Object is ILiteralNode literal)
            {
                serialized.DataType = literal.DataType?.ToString() ?? string.Empty;
                serialized.Language = literal.Language ?? string.Empty;
                serialized.ObjectValue = literal.Value;
            }

            return serialized;
        }

        public Triple ToTriple(IGraph graph)
        {
            var subjectNode = CreateSubjectNode(graph);
            var predicateNode = graph.CreateUriNode(UriFactory.Create(Predicate));
            var objectNode = CreateObjectNode(graph);
            return new Triple(subjectNode, predicateNode, objectNode);
        }

        private INode CreateSubjectNode(IGraph graph)
        {
            if (Subject.StartsWith("_:", StringComparison.Ordinal))
            {
                return graph.CreateBlankNode(Subject[2..]);
            }

            return graph.CreateUriNode(UriFactory.Create(Subject));
        }

        private INode CreateObjectNode(IGraph graph)
        {
            if (ObjectNodeType.Equals(nameof(NodeType.Uri), StringComparison.Ordinal))
            {
                return graph.CreateUriNode(UriFactory.Create(ObjectValue));
            }

            if (ObjectNodeType.Equals(nameof(NodeType.Blank), StringComparison.Ordinal))
            {
                var blankNodeId = ObjectValue.StartsWith("_:", StringComparison.Ordinal)
                    ? ObjectValue[2..]
                    : ObjectValue;
                return graph.CreateBlankNode(blankNodeId);
            }

            if (!string.IsNullOrEmpty(DataType))
            {
                return graph.CreateLiteralNode(ObjectValue, UriFactory.Create(DataType));
            }

            if (!string.IsNullOrEmpty(Language))
            {
                return graph.CreateLiteralNode(ObjectValue, Language);
            }

            return graph.CreateLiteralNode(ObjectValue);
        }
    }
}
