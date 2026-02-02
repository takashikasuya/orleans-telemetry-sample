using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;
using IOPath = System.IO.Path;

namespace DataModel.Analyzer.Services;

/// <summary>
/// RDF を解析して建物データモデルに変換するサービス
/// </summary>
public class RdfAnalyzerService
{
    private readonly ILogger<RdfAnalyzerService> _logger;
    private readonly Lazy<IGraph> _ontologyGraph;
    private readonly Lazy<ShapesGraph> _shapesGraph;

    private readonly string _ontologyFileName;
    private readonly string _shaclFileName;
    private readonly string _schemaFolder;

    private const string RecNamespace = "https://w3id.org/rec/";
    private const string SbcoNamespace = "https://www.sbco.or.jp/ont/";
    private const string BrickNamespace = "https://brickschema.org/schema/Brick#";
    private const string DctNamespace = "http://purl.org/dc/terms/";
    private const string RdfTypeUri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string ShaclFileName = "building_model.shacl.ttl";
    private const string SchemaDirectoryName = "Schema";
    private static readonly string[] DocumentationPredicates = { $"{SbcoNamespace}documentation", $"{RecNamespace}documentation" };
    private static readonly string[] AddressPredicates = { $"{SbcoNamespace}address", $"{RecNamespace}address" };
    private static readonly string[] CustomPropertiesPredicates = { $"{SbcoNamespace}customProperties", $"{RecNamespace}customProperties" };
    private static readonly string[] CustomTagsPredicates = { $"{SbcoNamespace}customTags", $"{RecNamespace}customTags" };
    private static readonly string[] DescriptionPredicates = { $"{SbcoNamespace}description", $"{RecNamespace}description" };
    private static readonly string[] FormatPredicates = { $"{SbcoNamespace}format", $"{RecNamespace}format" };
    private static readonly string[] LanguagePredicates = { $"{SbcoNamespace}language", $"{RecNamespace}language" };
    private static readonly string[] VersionPredicates = { $"{SbcoNamespace}version", $"{RecNamespace}version" };
    private static readonly string[] UrlPredicates = { $"{SbcoNamespace}url", $"{RecNamespace}url" };
    private static readonly string[] ChecksumPredicates = { $"{SbcoNamespace}checksum", $"{RecNamespace}checksum" };
    private static readonly string[] SizePredicates = { $"{SbcoNamespace}size", $"{RecNamespace}size" };
    private static readonly string[] PartOfPredicates = { $"{RecNamespace}isPartOf", $"{SbcoNamespace}isPartOf" };

    public RdfAnalyzerService(ILogger<RdfAnalyzerService> logger)
        : this(logger, Microsoft.Extensions.Options.Options.Create(new RdfAnalyzerOptions()))
    {
    }

    public RdfAnalyzerService(ILogger<RdfAnalyzerService> logger, IOptions<RdfAnalyzerOptions> options)
    {
        _logger = logger;
        var opts = options?.Value ?? new RdfAnalyzerOptions();
        _ontologyFileName = string.IsNullOrWhiteSpace(opts.OntologyFile) ? "building_model.owl.ttl" : opts.OntologyFile;
        _shaclFileName = string.IsNullOrWhiteSpace(opts.ShapesFile) ? "building_model.shacl.ttl" : opts.ShapesFile;
        _schemaFolder = string.IsNullOrWhiteSpace(opts.SchemaFolder) ? "Schema" : opts.SchemaFolder;

        _ontologyGraph = new Lazy<IGraph>(() => LoadSchemaGraph(_ontologyFileName));
        _shapesGraph = new Lazy<ShapesGraph>(() => LoadShapesGraph(_shaclFileName));
    }

    public async Task<BuildingDataModel> AnalyzeRdfFileAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルの解析を開始します {FilePath}", rdfFilePath);

        try
        {
            var graph = await Task.Run(() => LoadGraphFromFile(rdfFilePath));

            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);
            var model = BuildModel(graph, rdfFilePath);

            _logger.LogInformation(
                "RDFファイルの解析完了 Sites: {Sites}, Buildings: {Buildings}, Levels: {Levels}, Areas: {Areas}, Equipment: {Equipment}, Points: {Points}",
                model.Sites.Count, model.Buildings.Count, model.Levels.Count, model.Areas.Count, model.Equipment.Count, model.Points.Count);

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFファイルの解析中にエラーが発生しました: {FilePath}", rdfFilePath);
            throw;
        }
    }

    public async Task<BuildingDataModel> AnalyzeRdfContentAsync(string content, RdfSerializationFormat format, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツ解析を開始します {SourceName} ({Format})", sourceName, format);

        try
        {
            var graph = await Task.Run(() => LoadGraphFromContent(content, format));
            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);
            return BuildModel(graph, sourceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFコンテンツ解析中にエラーが発生しました: {SourceName}", sourceName);
            throw;
        }
    }

    public async Task<RdfAnalysisResult> AnalyzeRdfContentWithValidationAsync(
        string content,
        RdfSerializationFormat format,
        string sourceName = "content",
        string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFコンテンツ解析(検証付き)を開始します {SourceName} ({Format})", sourceName, format);

        var graph = await Task.Run(() => LoadGraphFromContent(content, format));
        var validation = ValidateAgainstShacl(graph, sourceName, shaclFilePath, false);
        var model = BuildModel(graph, sourceName);

        return new RdfAnalysisResult
        {
            Model = model,
            Validation = validation
        };
    }

    public async Task<RdfAnalysisResult> AnalyzeRdfFileWithValidationAsync(string rdfFilePath, string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFファイル解析(検証付き)を開始します {FilePath}", rdfFilePath);

        var graph = await Task.Run(() => LoadGraphFromFile(rdfFilePath));
        var validation = ValidateAgainstShacl(graph, rdfFilePath, shaclFilePath, false);
        var model = BuildModel(graph, rdfFilePath);

        return new RdfAnalysisResult
        {
            Model = model,
            Validation = validation
        };
    }

    private RdfValidationResult ValidateAgainstShacl(IGraph dataGraph, string sourceName, string? shaclFilePath, bool throwOnFailure)
    {
        ShapesGraph shapes;

        if (!string.IsNullOrWhiteSpace(shaclFilePath))
        {
            var g = new Graph();
            new TurtleParser().Load(g, shaclFilePath);
            shapes = new ShapesGraph(g);
            _logger.LogInformation("SHACL Shapes をカスタムパスからロードしました: {Path}", shaclFilePath);
        }
        else
        {
            shapes = _shapesGraph.Value;
            _ = _ontologyGraph.Value;
            _logger.LogInformation("SHACL Shapes をロードして検証します: {ShaclFile}", _shaclFileName);
        }

        var report = shapes.Validate(dataGraph);
        var messages = report.Results
            .Select(r =>
            {
                var focus = r.FocusNode?.ToString() ?? "(unknown)";
                var path = r.ResultPath?.ToString() ?? "(no path)";
                var message = r.Message?.Value ?? "No message";
                return $"{focus} @ {path}: {message}";
            })
            .ToList();

        if (!report.Conforms)
        {
            var detail = string.Join(Environment.NewLine, messages);
            _logger.LogWarning("SHACLバリデーションに失敗しました。{SourceName}{NewLine}{Details}", sourceName, Environment.NewLine, detail);

            if (throwOnFailure)
            {
                throw new InvalidDataException($"SHACL validation failed for {sourceName}:{Environment.NewLine}{detail}");
            }
        }

        return new RdfValidationResult
        {
            Conforms = report.Conforms,
            ReportText = report.ToString(),
            Messages = messages
        };
    }

    private IGraph LoadSchemaGraph(string fileName)
    {
        var path = ResolveSchemaPath(fileName);
        var graph = new Graph();
        new TurtleParser().Load(graph, path);
        _logger.LogInformation("スキーマをロードしました: {Path} (triples: {Triples})", path, graph.Triples.Count);
        return graph;
    }

    private ShapesGraph LoadShapesGraph(string fileName)
    {
        var path = ResolveSchemaPath(fileName);
        var graph = new Graph();
        new TurtleParser().Load(graph, path);
        _logger.LogInformation("SHACL Shapesをロードしました: {Path} (triples: {Triples})", path, graph.Triples.Count);
        return new ShapesGraph(graph);
    }

    private string ResolveSchemaPath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = IOPath.Combine(baseDir, _schemaFolder, fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var devPath = IOPath.Combine(baseDir, "..", "..", "..", _schemaFolder, fileName);
        var fullDevPath = IOPath.GetFullPath(devPath);
        if (File.Exists(fullDevPath))
        {
            return fullDevPath;
        }

        throw new FileNotFoundException($"Schema file not found: {fileName}", fileName);
    }

    private Graph LoadGraphFromFile(string filePath)
    {
        var ext = IOPath.GetExtension(filePath).ToLowerInvariant();
        _logger.LogDebug("拡張子 {Ext} に基づいて RDF パーサーを選択します", ext);

        var g = new Graph();

        switch (ext)
        {
            case ".ttl":
                new TurtleParser().Load(g, filePath);
                return g;
            case ".n3":
                new Notation3Parser().Load(g, filePath);
                return g;
            case ".nt":
                new NTriplesParser().Load(g, filePath);
                return g;
            case ".rdf":
            case ".owl":
            case ".xml":
                new RdfXmlParser().Load(g, filePath);
                return g;
            case ".jsonld":
            case ".json":
            {
                var store = new TripleStore();
                new JsonLdParser().Load(store, filePath);
                return MergeStoreToGraph(store);
            }
            case ".trig":
            {
                var store = new TripleStore();
                new TriGParser().Load(store, filePath);
                return MergeStoreToGraph(store);
            }
            case ".trix":
            {
                var store = new TripleStore();
                new TriXParser().Load(store, filePath);
                return MergeStoreToGraph(store);
            }
            case ".nq":
            {
                var store = new TripleStore();
                new NQuadsParser().Load(store, filePath);
                return MergeStoreToGraph(store);
            }
            default:
                _logger.LogWarning("未対応の拡張子 {Ext}。Turtle として試行します", ext);
                new TurtleParser().Load(g, filePath);
                return g;
        }
    }

    private Graph LoadGraphFromContent(string content, RdfSerializationFormat format)
    {
        _logger.LogDebug("形式 {Format} で RDF コンテンツを読み込みます", format);

        return format switch
        {
            RdfSerializationFormat.Turtle => LoadGraphWithReader(content, (graph, reader) => new TurtleParser().Load(graph, reader)),
            RdfSerializationFormat.Notation3 => LoadGraphWithReader(content, (graph, reader) => new Notation3Parser().Load(graph, reader)),
            RdfSerializationFormat.NTriples => LoadGraphWithReader(content, (graph, reader) => new NTriplesParser().Load(graph, reader)),
            RdfSerializationFormat.RdfXml => LoadGraphWithReader(content, (graph, reader) => new RdfXmlParser().Load(graph, reader)),
            RdfSerializationFormat.JsonLd => LoadStoreWithReader(content, new JsonLdParser()),
            RdfSerializationFormat.TriG => LoadStoreWithReader(content, new TriGParser()),
            RdfSerializationFormat.TriX => LoadStoreWithReader(content, new TriXParser()),
            RdfSerializationFormat.NQuads => LoadStoreWithReader(content, new NQuadsParser()),
            _ => LoadGraphWithReader(content, (graph, reader) => new TurtleParser().Load(graph, reader)),
        };
    }

    private Graph LoadGraphWithReader(string content, Action<IGraph, TextReader> loader)
    {
        var graph = new Graph();

        using var reader = new StringReader(content);
        loader(graph, reader);

        return graph;
    }

    private Graph LoadStoreWithReader(string content, IStoreReader reader)
    {
        var store = new TripleStore();
        using var stringReader = new StringReader(content);
        reader.Load(store, stringReader);
        return MergeStoreToGraph(store);
    }

    private Graph MergeStoreToGraph(ITripleStore store)
    {
        var merged = new Graph();
        foreach (var ng in store.Graphs)
        {
            merged.Assert(ng.Triples);
        }

        _logger.LogDebug(
            "データセットを単一グラフにマージしました。グラフ数: {GraphCount}, トリプル数: {TripleCount}",
            store.Graphs.Count,
            merged.Triples.Count);

        return merged;
    }

    private BuildingDataModel BuildModel(IGraph graph, string sourceName)
    {
        var model = new BuildingDataModel
        {
            Source = sourceName,
            LastUpdated = DateTime.UtcNow
        };

        model.Sites = ExtractSites(graph);
        model.Buildings = ExtractBuildings(graph);
        model.Levels = ExtractLevels(graph);
        model.Areas = ExtractAreas(graph);
        model.Equipment = ExtractEquipment(graph);
        model.Points = ExtractPoints(graph);

        BuildHierarchy(model);
        return model;
    }

    private List<Site> ExtractSites(IGraph graph)
    {
        var sites = new List<Site>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}Site", $"{RecNamespace}Site" });

        foreach (var subject in subjects)
        {
            var site = new Site { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, site);
            site.Documentation.AddRange(ExtractDocuments(graph, subject));
            site.Address.AddRange(ExtractPostalAddresses(graph, subject));

            var hasPartUris = GetObjectUris(graph, subject, new[] { $"{RecNamespace}hasPart", $"{SbcoNamespace}hasPart" });
            if (hasPartUris.Count > 0)
            {
                StoreStringList(site.CustomProperties, "hasPartUris", hasPartUris);
            }

            sites.Add(site);
        }

        return sites;
    }

    private List<Building> ExtractBuildings(IGraph graph)
    {
        var buildings = new List<Building>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}Building", $"{RecNamespace}Building" });

        foreach (var subject in subjects)
        {
            var building = new Building { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, building);
            building.Documentation.AddRange(ExtractDocuments(graph, subject));
            building.Address.AddRange(ExtractPostalAddresses(graph, subject));

            var hasPartUris = GetObjectUris(graph, subject, new[] { $"{RecNamespace}hasPart", $"{SbcoNamespace}hasPart" });
            if (hasPartUris.Count > 0)
            {
                StoreStringList(building.CustomProperties, "hasPartUris", hasPartUris);
            }
            var partOfUris = GetParentUris(graph, subject);
            if (partOfUris.Count > 0)
            {
                StoreStringList(building.CustomProperties, "isPartOfUris", partOfUris);
            }

            buildings.Add(building);
        }

        return buildings;
    }

    private List<Level> ExtractLevels(IGraph graph)
    {
        var levels = new List<Level>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}Level", $"{RecNamespace}Level" });

        foreach (var subject in subjects)
        {
            var level = new Level { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, level);
            level.Documentation.AddRange(ExtractDocuments(graph, subject));
            level.Address.AddRange(ExtractPostalAddresses(graph, subject));

            var levelNumberValue = GetFirstLiteralValue(
                graph,
                subject,
                new[] { $"{SbcoNamespace}levelNumber", $"{RecNamespace}levelNumber" });

            if (!string.IsNullOrWhiteSpace(levelNumberValue) && int.TryParse(levelNumberValue, out var levelNumber))
            {
                level.LevelNumber = levelNumber;
            }

            var hasPartUris = GetObjectUris(graph, subject, new[] { $"{RecNamespace}hasPart", $"{SbcoNamespace}hasPart" });
            if (hasPartUris.Count > 0)
            {
                StoreStringList(level.CustomProperties, "hasPartUris", hasPartUris);
            }
            var partOfUris = GetParentUris(graph, subject);
            if (partOfUris.Count > 0)
            {
                StoreStringList(level.CustomProperties, "isPartOfUris", partOfUris);
            }

            levels.Add(level);
        }

        return levels;
    }

    private List<Area> ExtractAreas(IGraph graph)
    {
        var areas = new List<Area>();
        var subjects = GetSubjectsOfType(graph, new[]
        {
            $"{SbcoNamespace}Space",
            $"{SbcoNamespace}Area",
            $"{SbcoNamespace}Room",
            $"{SbcoNamespace}OutdoorSpace",
            $"{SbcoNamespace}Zone",
            $"{RecNamespace}Space",
            $"{RecNamespace}Area",
            $"{RecNamespace}Room",
            $"{RecNamespace}OutdoorSpace",
            $"{RecNamespace}Zone"
        });

        foreach (var subject in subjects)
        {
            var area = new Area { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, area);
            area.Documentation.AddRange(ExtractDocuments(graph, subject));

            var equipmentUris = GetObjectUris(graph, subject, new[] { $"{RecNamespace}isLocationOf", $"{SbcoNamespace}isLocationOf" });
            if (equipmentUris.Count > 0)
            {
                StoreStringList(area.CustomProperties, "equipmentUris", equipmentUris);
            }
            var partOfUris = GetParentUris(graph, subject);
            if (partOfUris.Count > 0)
            {
                StoreStringList(area.CustomProperties, "isPartOfUris", partOfUris);
            }

            areas.Add(area);
        }

        return areas;
    }

    private List<Equipment> ExtractEquipment(IGraph graph)
    {
        var equipmentList = new List<Equipment>();
        var subjects = GetSubjectsOfType(graph, new[]
        {
            $"{SbcoNamespace}EquipmentExt",
            $"{SbcoNamespace}Equipment",
            $"{BrickNamespace}Equipment"
        });

        foreach (var subject in subjects)
        {
            var equipment = new Equipment { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, equipment);
            ExtractAssetProperties(graph, subject, equipment);
            ExtractGutpEquipmentProperties(graph, subject, equipment);
            ExtractEquipmentExtProperties(graph, subject, equipment);
            equipment.Documentation.AddRange(ExtractDocuments(graph, subject));

            var pointUris = GetObjectUris(graph, subject, new[] { $"{RecNamespace}hasPoint", $"{SbcoNamespace}hasPoint" });
            if (pointUris.Count > 0)
            {
                StoreStringList(equipment.CustomProperties, "pointUris", pointUris);
            }

            var feedUris = GetObjectUris(graph, subject, new[] { $"{BrickNamespace}feeds" })
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct()
                .ToList();
            if (feedUris.Count > 0)
            {
                equipment.Feeds.AddRange(feedUris);
            }

            var fedByUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}isFedBy", $"{RecNamespace}isFedBy" })
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct()
                .ToList();
            if (fedByUris.Count > 0)
            {
                equipment.IsFedBy.AddRange(fedByUris);
            }

            var locatedInUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}locatedIn", $"{RecNamespace}locatedIn" });
            if (locatedInUris.Count > 0)
            {
                equipment.AreaUri = locatedInUris[0];
            }
            var partOfUris = GetParentUris(graph, subject);
            if (partOfUris.Count > 0)
            {
                StoreStringList(equipment.CustomProperties, "isPartOfUris", partOfUris);
            }

            if (string.IsNullOrWhiteSpace(equipment.DeviceId) && !string.IsNullOrWhiteSpace(equipment.SchemaId))
            {
                equipment.DeviceId = equipment.SchemaId;
            }

            equipmentList.Add(equipment);
        }

        return equipmentList;
    }

    private List<Point> ExtractPoints(IGraph graph)
    {
        var points = new List<Point>();
        var subjects = GetSubjectsOfType(graph, new[]
        {
            $"{SbcoNamespace}PointExt",
            $"{SbcoNamespace}Point",
            $"{BrickNamespace}Point"
        });

        foreach (var subject in subjects)
        {
            var point = new Point { Uri = subject.ToString() };

            ExtractCommonProperties(graph, subject, point);
            ExtractPointProperties(graph, subject, point);

            var isPointOfUris = GetObjectUris(graph, subject, new[]
            {
                $"{BrickNamespace}isPointOf",
                $"{RecNamespace}isPointOf",
                $"{SbcoNamespace}isPointOf"
            });
            if (isPointOfUris.Count > 0)
            {
                point.EquipmentUri = isPointOfUris[0];
            }

            var partOfUris = GetParentUris(graph, subject);
            if (partOfUris.Count > 0)
            {
                StoreStringList(point.CustomProperties, "isPartOfUris", partOfUris);
            }

            if (string.IsNullOrWhiteSpace(point.PointId) && !string.IsNullOrWhiteSpace(point.SchemaId))
            {
                point.PointId = point.SchemaId;
            }
            points.Add(point);
        }

        return points;
    }

    private IEnumerable<INode> GetSubjectsOfType(IGraph graph, IEnumerable<string> classUris)
    {
        var rdfType = graph.CreateUriNode(UriFactory.Create(RdfTypeUri));
        var seen = new HashSet<string>();

        foreach (var classUri in classUris)
        {
            var classNode = graph.CreateUriNode(UriFactory.Create(classUri));
            foreach (var triple in graph.GetTriplesWithPredicateObject(rdfType, classNode))
            {
                var subjectKey = triple.Subject.ToString();
                if (seen.Add(subjectKey))
                {
                    yield return triple.Subject;
                }
            }
        }
    }

    private static List<string> GetObjectUris(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        var uris = new List<string>();
        foreach (var predicateUri in predicateUris)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                uris.Add(triple.Object.ToString());
            }
        }

        return uris;
    }

    private static List<string> GetParentUris(IGraph graph, INode subject)
    {
        return GetObjectUris(graph, subject, PartOfPredicates);
    }

    private static string? GetFirstLiteralValue(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        foreach (var predicateUri in predicateUris)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                if (triple.Object is ILiteralNode literalNode)
                {
                    return literalNode.Value;
                }
            }
        }

        return null;
    }

    private static string? GetFirstObjectValue(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        foreach (var predicateUri in predicateUris)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                return triple.Object switch
                {
                    ILiteralNode literalNode => literalNode.Value,
                    IUriNode uriNode => uriNode.Uri.ToString(),
                    _ => triple.Object.ToString()
                };
            }
        }

        return null;
    }

    private void ExtractIdentifiers(IGraph graph, INode subject, RdfResource resource)
    {
        foreach (var predicateUri in new[] { $"{SbcoNamespace}identifiers", $"{RecNamespace}identifiers" })
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                var entryNode = triple.Object;
                var key = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}key" });
                var value = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}value" });

                if (!string.IsNullOrWhiteSpace(key) && value != null)
                {
                    resource.Identifiers[key] = value;
                    continue;
                }

                var dctId = GetFirstLiteralValue(graph, entryNode, new[] { $"{DctNamespace}identifier" });
                if (!string.IsNullOrWhiteSpace(dctId))
                {
                    resource.Identifiers["dtid"] = dctId;
                }
            }
        }
    }

    private void ExtractAssetProperties(IGraph graph, INode subject, Asset asset)
    {
        asset.AssetTag = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}assetTag", $"{RecNamespace}assetTag" }) ?? asset.AssetTag;
        asset.ModelNumber = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}modelNumber" }) ?? asset.ModelNumber;
        asset.SerialNumber = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}serialNumber" }) ?? asset.SerialNumber;
        asset.IPAddress = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}IPAddress" }) ?? asset.IPAddress;
        asset.MACAddress = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}MACAddress" }) ?? asset.MACAddress;
        asset.InitialCost = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}initialCost" }) ?? asset.InitialCost;
        asset.Weight = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}weight" }) ?? asset.Weight;

        asset.CommissioningDate = GetFirstDateValue(graph, subject, new[] { $"{SbcoNamespace}commissioningDate" }) ?? asset.CommissioningDate;
        asset.InstallationDate = GetFirstDateValue(graph, subject, new[] { $"{SbcoNamespace}installationDate" }) ?? asset.InstallationDate;
        asset.TurnoverDate = GetFirstDateValue(graph, subject, new[] { $"{SbcoNamespace}turnoverDate" }) ?? asset.TurnoverDate;
    }

    private static DateTime? GetFirstDateValue(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        var value = GetFirstLiteralValue(graph, subject, predicateUris);
        if (!string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private void ExtractCommonProperties(IGraph graph, INode subject, RdfResource resource)
    {
        var name = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}name", $"{RecNamespace}name" });
        if (!string.IsNullOrWhiteSpace(name))
        {
            resource.Name = name;
        }

        var schemaId = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}id", $"{RecNamespace}id" });
        if (!string.IsNullOrWhiteSpace(schemaId))
        {
            resource.SchemaId = schemaId;
        }

        ExtractIdentifiers(graph, subject, resource);
        ExtractCustomProperties(graph, subject, resource);
        ExtractCustomTags(graph, subject, resource);
    }

    private void ExtractGutpEquipmentProperties(IGraph graph, INode subject, Equipment equipment)
    {
        var properties = new Dictionary<string, string>
        {
            { $"{SbcoNamespace}gateway_id", nameof(Equipment.GatewayId) },
            { $"{SbcoNamespace}device_id", nameof(Equipment.DeviceId) },
            { $"{SbcoNamespace}device_type", nameof(Equipment.DeviceType) },
            { $"{SbcoNamespace}supplier", nameof(Equipment.Supplier) },
            { $"{SbcoNamespace}owner", nameof(Equipment.Owner) }
        };

        foreach (var (predicateUri, propertyName) in properties)
        {
            var value = GetFirstLiteralValue(graph, subject, new[] { predicateUri });
            if (!string.IsNullOrWhiteSpace(value))
            {
                var property = typeof(Equipment).GetProperty(propertyName);
                property?.SetValue(equipment, value);
            }
        }
    }

    private void ExtractEquipmentExtProperties(IGraph graph, INode subject, Equipment equipment)
    {
        var deviceType = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}deviceType" });
        if (!string.IsNullOrWhiteSpace(deviceType))
        {
            equipment.DeviceType = deviceType;
        }

        equipment.InstallationArea = GetFirstLiteralValue(graph, subject, new[]
        {
            $"{SbcoNamespace}installationArea",
            $"{SbcoNamespace}installation_area"
        }) ?? equipment.InstallationArea;

        equipment.TargetArea = GetFirstLiteralValue(graph, subject, new[]
        {
            $"{SbcoNamespace}targetArea",
            $"{SbcoNamespace}target_area"
        }) ?? equipment.TargetArea;

        equipment.Panel = GetFirstLiteralValue(graph, subject, new[]
        {
            $"{SbcoNamespace}panel"
        }) ?? equipment.Panel;
    }

    private void ExtractPointProperties(IGraph graph, INode subject, Point point)
    {
        var stringProperties = new Dictionary<string, string>
        {
            { $"{SbcoNamespace}point_id", nameof(Point.PointId) },
            { $"{SbcoNamespace}point_type", nameof(Point.PointType) },
            { $"{SbcoNamespace}point_specification", nameof(Point.PointSpecification) },
            { $"{SbcoNamespace}local_id", nameof(Point.LocalId) },
            { $"{SbcoNamespace}unit", nameof(Point.Unit) },
            { $"{SbcoNamespace}device_id_bacnet", nameof(Point.DeviceIdBacnet) },
            { $"{SbcoNamespace}instance_no_bacnet", nameof(Point.InstanceNoBacnet) },
            { $"{SbcoNamespace}object_type_bacnet", nameof(Point.ObjectTypeBacnet) },
            { $"{SbcoNamespace}pointType", nameof(Point.PointType) },
            { $"{SbcoNamespace}pointSpecification", nameof(Point.PointSpecification) }
        };

        foreach (var (predicateUri, propertyName) in stringProperties)
        {
            var value = GetFirstObjectValue(graph, subject, new[] { predicateUri });
            if (!string.IsNullOrWhiteSpace(value))
            {
                var property = typeof(Point).GetProperty(propertyName);
                property?.SetValue(point, value);
            }
        }

        var writableValue = GetFirstLiteralValue(graph, subject, new[] { $"{SbcoNamespace}writable" });
        if (!string.IsNullOrWhiteSpace(writableValue) && bool.TryParse(writableValue, out var writable))
        {
            point.Writable = writable;
        }

        ExtractNumericProperty(graph, subject, new[] { $"{SbcoNamespace}interval", $"{SbcoNamespace}intervalCapability" }, value => point.Interval = (int?)value);
        ExtractNumericProperty(graph, subject, new[] { $"{SbcoNamespace}max_pres_value", $"{SbcoNamespace}maxPresValue" }, value => point.MaxPresValue = value);
        ExtractNumericProperty(graph, subject, new[] { $"{SbcoNamespace}min_pres_value", $"{SbcoNamespace}minPresValue" }, value => point.MinPresValue = value);
        ExtractNumericProperty(graph, subject, new[] { $"{SbcoNamespace}scale", $"{SbcoNamespace}scale" }, value => point.Scale = value);

        point.HasQuantity = GetFirstObjectValue(graph, subject, new[] { $"{SbcoNamespace}hasQuantity", $"{BrickNamespace}hasQuantity" });
        point.HasSubstance = GetFirstObjectValue(graph, subject, new[] { $"{SbcoNamespace}hasSubstance", $"{BrickNamespace}hasSubstance" });
    }

    private void ExtractNumericProperty(IGraph graph, INode subject, IEnumerable<string> predicateUris, Action<double> setValue)
    {
        var value = GetFirstLiteralValue(graph, subject, predicateUris);
        if (!string.IsNullOrWhiteSpace(value) && double.TryParse(value, out var numericValue))
        {
            setValue(numericValue);
        }
    }

    private List<Document> ExtractDocuments(IGraph graph, INode subject)
    {
        return GetObjectNodes(graph, subject, DocumentationPredicates)
            .Select(node => BuildDocument(graph, node))
            .ToList();
    }

    private List<PostalAddress> ExtractPostalAddresses(IGraph graph, INode subject)
    {
        return GetObjectNodes(graph, subject, AddressPredicates)
            .Select(node => BuildPostalAddress(graph, node))
            .ToList();
    }

    private Document BuildDocument(IGraph graph, INode node)
    {
        var document = new Document
        {
            Uri = node.ToString()
        };
        ExtractCommonProperties(graph, node, document);

        document.Description = GetFirstLiteralValue(graph, node, DescriptionPredicates);
        document.Format = GetFirstLiteralValue(graph, node, FormatPredicates);
        document.Language = GetFirstLiteralValue(graph, node, LanguagePredicates);
        document.Version = GetFirstLiteralValue(graph, node, VersionPredicates);
        document.Url = GetFirstLiteralValue(graph, node, UrlPredicates);
        document.Checksum = GetFirstLiteralValue(graph, node, ChecksumPredicates);
        document.Size = GetFirstIntegerValue(graph, node, SizePredicates);

        return document;
    }

    private PostalAddress BuildPostalAddress(IGraph graph, INode node)
    {
        var address = new PostalAddress
        {
            Uri = node.ToString()
        };
        ExtractCommonProperties(graph, node, address);
        return address;
    }

    private void ExtractCustomProperties(IGraph graph, INode subject, RdfResource resource)
    {
        foreach (var predicateUri in CustomPropertiesPredicates)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                var entryNode = triple.Object;
                var outerKey = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}key" });
                if (string.IsNullOrWhiteSpace(outerKey))
                {
                    continue;
                }

                var nestedEntries = GetObjectNodes(graph, entryNode, new[] { $"{SbcoNamespace}entries" }).ToList();
                if (nestedEntries.Count > 0)
                {
                    var nested = new Dictionary<string, string>();
                    foreach (var nestedEntry in nestedEntries)
                    {
                        var nestedKey = GetFirstLiteralValue(graph, nestedEntry, new[] { $"{SbcoNamespace}key" });
                        var nestedValue = GetFirstLiteralValue(graph, nestedEntry, new[] { $"{SbcoNamespace}value" });
                        if (string.IsNullOrWhiteSpace(nestedKey) || nestedValue is null)
                        {
                            continue;
                        }

                        nested[nestedKey] = nestedValue;
                    }

                    if (nested.Count > 0)
                    {
                        resource.CustomProperties[outerKey] = JsonSerializer.Serialize(nested);
                        continue;
                    }
                }

                var literalValue = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}value" });
                if (!string.IsNullOrWhiteSpace(literalValue))
                {
                    resource.CustomProperties[outerKey] = literalValue;
                }
            }
        }
    }

    private void ExtractCustomTags(IGraph graph, INode subject, RdfResource resource)
    {
        foreach (var predicateUri in CustomTagsPredicates)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                var entryNode = triple.Object;
                var key = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}key" });
                var flagValue = GetFirstLiteralValue(graph, entryNode, new[] { $"{SbcoNamespace}flag" });
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(flagValue))
                {
                    continue;
                }

                if (bool.TryParse(flagValue, out var flag))
                {
                    resource.CustomTags[key] = flag;
                }
            }
        }
    }

    private static void StoreStringList(IDictionary<string, string> properties, string key, IEnumerable<string> values)
    {
        var filtered = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        properties[key] = JsonSerializer.Serialize(filtered);
    }

    private static bool TryGetStringList(IReadOnlyDictionary<string, string> properties, string key, out List<string> values)
    {
        values = new List<string>();

        if (!properties.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<List<string>>(rawValue);
            if (deserialized != null)
            {
                var filtered = deserialized.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (filtered.Count > 0)
                {
                    values = filtered;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // fall back to single string value
        }

        values = new List<string> { rawValue };
        return true;
    }

    private static IEnumerable<INode> GetObjectNodes(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        var seen = new HashSet<string>();
        foreach (var predicateUri in predicateUris)
        {
            var predicateNode = graph.CreateUriNode(UriFactory.Create(predicateUri));
            foreach (var triple in graph.GetTriplesWithSubjectPredicate(subject, predicateNode))
            {
                var key = triple.Object.ToString();
                if (seen.Add(key))
                {
                    yield return triple.Object;
                }
            }
        }
    }

    private static long? GetFirstIntegerValue(IGraph graph, INode subject, IEnumerable<string> predicateUris)
    {
        var value = GetFirstLiteralValue(graph, subject, predicateUris);
        if (!string.IsNullOrWhiteSpace(value) && long.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private void BuildHierarchy(BuildingDataModel model)
    {
        var siteByUri = model.Sites.ToDictionary(s => s.Uri, s => s);
        var buildingByUri = model.Buildings.ToDictionary(b => b.Uri, b => b);
        var levelByUri = model.Levels.ToDictionary(l => l.Uri, l => l);
        var areaByUri = model.Areas.ToDictionary(a => a.Uri, a => a);
        var equipmentByUri = model.Equipment.ToDictionary(e => e.Uri, e => e);

        foreach (var site in model.Sites)
        {
            if (TryGetStringList(site.CustomProperties, "hasPartUris", out var buildingUris))
            {
                foreach (var bUri in buildingUris)
                {
                    if (buildingByUri.TryGetValue(bUri, out var b))
                    {
                        b.SiteUri = site.Uri;
                        site.Buildings.Add(b);
                    }
                }
            }
        }

        foreach (var building in model.Buildings)
        {
            if (!string.IsNullOrWhiteSpace(building.SiteUri))
            {
                continue;
            }

            if (TryGetStringList(building.CustomProperties, "isPartOfUris", out var parentUris))
            {
                foreach (var parentUri in parentUris)
                {
                    if (siteByUri.TryGetValue(parentUri, out var site))
                    {
                        building.SiteUri = site.Uri;
                        if (!site.Buildings.Contains(building))
                        {
                            site.Buildings.Add(building);
                        }

                        break;
                    }
                }
            }
        }

        foreach (var building in model.Buildings)
        {
            if (TryGetStringList(building.CustomProperties, "hasPartUris", out var levelUris))
            {
                foreach (var lUri in levelUris)
                {
                    if (levelByUri.TryGetValue(lUri, out var l))
                    {
                        l.BuildingUri = building.Uri;
                        building.Levels.Add(l);
                    }
                }
            }
        }

        foreach (var level in model.Levels)
        {
            if (!string.IsNullOrWhiteSpace(level.BuildingUri))
            {
                continue;
            }

            if (TryGetStringList(level.CustomProperties, "isPartOfUris", out var parentUris))
            {
                foreach (var parentUri in parentUris)
                {
                    if (buildingByUri.TryGetValue(parentUri, out var building))
                    {
                        level.BuildingUri = building.Uri;
                        if (!building.Levels.Contains(level))
                        {
                            building.Levels.Add(level);
                        }

                        break;
                    }
                }
            }
        }


        foreach (var level in model.Levels)
        {
            if (TryGetStringList(level.CustomProperties, "hasPartUris", out var areaUris))
            {
                foreach (var aUri in areaUris)
                {
                    if (areaByUri.TryGetValue(aUri, out var a))
                    {
                        a.LevelUri = level.Uri;
                        level.Areas.Add(a);
                    }
                }
            }
        }

        foreach (var area in model.Areas)
        {
            if (!string.IsNullOrWhiteSpace(area.LevelUri))
            {
                continue;
            }

            if (TryGetStringList(area.CustomProperties, "isPartOfUris", out var parentUris))
            {
                foreach (var parentUri in parentUris)
                {
                    if (levelByUri.TryGetValue(parentUri, out var level))
                    {
                        area.LevelUri = level.Uri;
                        if (!level.Areas.Contains(area))
                        {
                            level.Areas.Add(area);
                        }

                        break;
                    }
                }
            }
        }


        foreach (var area in model.Areas)
        {
            if (TryGetStringList(area.CustomProperties, "equipmentUris", out var equipmentUris))
            {
                foreach (var eUri in equipmentUris)
                {
                    if (equipmentByUri.TryGetValue(eUri, out var e))
                    {
                        e.AreaUri = area.Uri;
                        area.Equipment.Add(e);
                        e.LocatedIn.Add(area);
                    }
                }
            }
        }

        foreach (var equipment in model.Equipment)
        {
            if (!string.IsNullOrWhiteSpace(equipment.AreaUri))
            {
                continue;
            }

            if (TryGetStringList(equipment.CustomProperties, "isPartOfUris", out var parentUris))
            {
                foreach (var parentUri in parentUris)
                {
                    if (areaByUri.TryGetValue(parentUri, out var area))
                    {
                        equipment.AreaUri = area.Uri;
                        if (!area.Equipment.Contains(equipment))
                        {
                            area.Equipment.Add(equipment);
                        }
                        if (!equipment.LocatedIn.Any(x => x.Uri == area.Uri))
                        {
                            equipment.LocatedIn.Add(area);
                        }

                        break;
                    }
                }
            }
        }

        foreach (var equipment in model.Equipment)
        {
            if (!string.IsNullOrEmpty(equipment.AreaUri) && areaByUri.TryGetValue(equipment.AreaUri, out var a))
            {
                if (!equipment.LocatedIn.Any(x => x.Uri == a.Uri))
                {
                    equipment.LocatedIn.Add(a);
                }
            }
        }

        foreach (var equipment in model.Equipment)
        {
            if (TryGetStringList(equipment.CustomProperties, "pointUris", out var pointUris))
            {
                foreach (var pUri in pointUris)
                {
                    var p = model.Points.FirstOrDefault(x => x.Uri == pUri);
                    if (p != null)
                    {
                        p.EquipmentUri = equipment.Uri;
                        equipment.Points.Add(p);
                    }
                }
            }
        }

        foreach (var point in model.Points)
        {
            if (!string.IsNullOrWhiteSpace(point.EquipmentUri))
            {
                continue;
            }

            if (TryGetStringList(point.CustomProperties, "isPartOfUris", out var parentUris))
            {
                foreach (var parentUri in parentUris)
                {
                    if (equipmentByUri.TryGetValue(parentUri, out var equipment))
                    {
                        point.EquipmentUri = equipment.Uri;
                        if (!equipment.Points.Contains(point))
                        {
                            equipment.Points.Add(point);
                        }

                        break;
                    }
                }
            }
        }

        foreach (var point in model.Points)
        {
            if (!string.IsNullOrEmpty(point.EquipmentUri) && equipmentByUri.TryGetValue(point.EquipmentUri, out var e))
            {
                if (!e.Points.Any(p => p.Uri == point.Uri))
                {
                    e.Points.Add(point);
                }
            }
        }
    }
}
