using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using Microsoft.Extensions.Logging;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl;

namespace DataModel.Analyzer.Services;

/// <summary>
/// RDFファイルを解析してデータモデルに変換するサービス
/// </summary>
public class RdfAnalyzerService
{
    private readonly ILogger<RdfAnalyzerService> _logger;

    private const string RecNamespace = "https://w3id.org/rec/";
    private const string GutpNamespace = "https://www.gutp.jp/bim-wg#";
    private const string SbcoNamespace = "https://www.sbco.or.jp/ont/";
    private const string BrickNamespace = "https://brickschema.org/schema/Brick#";
    private const string DctNamespace = "http://purl.org/dc/terms/";
    private const string RdfTypeUri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string ShaclFileName = "building_model.shacl.ttl";
    private const string SchemaDirectoryName = "Schema";

    public RdfAnalyzerService(ILogger<RdfAnalyzerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// RDFファイルを読み込んでデータモデルに変換する
    /// </summary>
    public async Task<BuildingDataModel> AnalyzeRdfFileAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルの解析を開始: {FilePath}", rdfFilePath);

        try
        {
            var graph = await Task.Run(() => LoadGraphFromFile(rdfFilePath));

            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

            return BuildModel(graph, rdfFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFファイルの解析中にエラーが発生しました: {FilePath}", rdfFilePath);
            throw;
        }
    }

    /// <summary>
    /// RDFコンテンツを読み込んでデータモデルに変換する
    /// </summary>
    public async Task<BuildingDataModel> AnalyzeRdfContentAsync(string content, RdfSerializationFormat format, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツの解析を開始: {SourceName} ({Format})", sourceName, format);

        try
        {
            var graph = await Task.Run(() => LoadGraphFromContent(content, format));

            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

            return BuildModel(graph, sourceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFコンテンツの解析中にエラーが発生しました: {SourceName}", sourceName);
            throw;
        }
    }

    /// <summary>
    /// RDFファイルを読み込み、SHACLバリデーションを実行してデータモデルに変換する
    /// </summary>
    public async Task<RdfAnalysisResult> AnalyzeRdfFileWithValidationAsync(string rdfFilePath, string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFファイルの解析(検証付き)を開始: {FilePath}", rdfFilePath);

        var graph = await Task.Run(() => LoadGraphFromFile(rdfFilePath));
        _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

        var validation = ValidateGraph(graph, shaclFilePath);
        if (validation is { Conforms: false })
        {
            _logger.LogWarning("SHACLバリデーション失敗: {Report}", validation.ReportText);
        }
        else if (validation is { Conforms: true })
        {
            _logger.LogInformation("SHACLバリデーション成功");
        }

        var model = BuildModel(graph, rdfFilePath);
        return new RdfAnalysisResult { Model = model, Validation = validation };
    }

    /// <summary>
    /// RDFコンテンツを読み込み、SHACLバリデーションを実行してデータモデルに変換する
    /// </summary>
    public async Task<RdfAnalysisResult> AnalyzeRdfContentWithValidationAsync(string content, RdfSerializationFormat format, string sourceName = "content", string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFコンテンツの解析(検証付き)を開始: {SourceName} ({Format})", sourceName, format);

        var graph = await Task.Run(() => LoadGraphFromContent(content, format));
        _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

        var validation = ValidateGraph(graph, shaclFilePath);
        if (validation is { Conforms: false })
        {
            _logger.LogWarning("SHACLバリデーション失敗: {Report}", validation.ReportText);
        }
        else if (validation is { Conforms: true })
        {
            _logger.LogInformation("SHACLバリデーション成功");
        }

        var model = BuildModel(graph, sourceName);
        return new RdfAnalysisResult { Model = model, Validation = validation };
    }

    private BuildingDataModel BuildModel(IGraph graph, string source)
    {
        var model = new BuildingDataModel
        {
            Source = source
        };

        model.Sites = ExtractSites(graph);
        model.Buildings = ExtractBuildings(graph);
        model.Levels = ExtractLevels(graph);
        model.Areas = ExtractAreas(graph);
        model.Equipment = ExtractEquipment(graph);
        model.Points = ExtractPoints(graph);

        BuildHierarchy(model);

        _logger.LogInformation(
            "RDF解析完了: Sites={Sites}, Buildings={Buildings}, Levels={Levels}, Areas={Areas}, Equipment={Equipment}, Points={Points}",
            model.Sites.Count, model.Buildings.Count, model.Levels.Count, model.Areas.Count, model.Equipment.Count, model.Points.Count);

        return model;
    }

    private RdfValidationResult? ValidateGraph(IGraph dataGraph, string? shaclFilePath)
    {
        var resolvedPath = ResolveShaclPath(shaclFilePath);
        if (!File.Exists(resolvedPath))
        {
            _logger.LogWarning("SHACLファイルが見つかりません: {Path}", resolvedPath);
            return null;
        }

        try
        {
            var shapesGraph = new Graph();
            new TurtleParser().Load(shapesGraph, resolvedPath);
            var shapes = new ShapesGraph(shapesGraph);
            var report = shapes.Validate(dataGraph);

            var messages = report.Results?
                .Select(r => r.ToString())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList() ?? new List<string>();

            return new RdfValidationResult
            {
                Conforms = report.Conforms,
                ReportText = report.ToString(),
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SHACLバリデーション中にエラーが発生しました: {Path}", resolvedPath);
            return null;
        }
    }

    private string ResolveShaclPath(string? shaclFilePath)
    {
        if (!string.IsNullOrWhiteSpace(shaclFilePath))
        {
            return shaclFilePath;
        }

        var baseDir = AppContext.BaseDirectory;
        return System.IO.Path.Combine(baseDir, SchemaDirectoryName, ShaclFileName);
    }

    private Graph LoadGraphFromFile(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        _logger.LogDebug("拡張子 {Ext} に基づいて RDF パーサーを選択します", ext);

        IGraph? g = new Graph();

        try
        {
            switch (ext)
            {
                case ".ttl":
                    new TurtleParser().Load(g, filePath);
                    return (Graph)g;
                case ".n3":
                    new Notation3Parser().Load(g, filePath);
                    return (Graph)g;
                case ".nt":
                    new NTriplesParser().Load(g, filePath);
                    return (Graph)g;
                case ".rdf":
                case ".owl":
                case ".xml":
                    new RdfXmlParser().Load(g, filePath);
                    return (Graph)g;
                case ".jsonld":
                case ".json":
                    try
                    {
                        var store = new TripleStore();
                        new JsonLdParser().Load(store, filePath);
                        return MergeStoreToGraph(store);
                    }
                    catch (Exception jex)
                    {
                        _logger.LogWarning(jex, "JSON-LD の解析に失敗しました。ファイル: {Path}", filePath);
                        throw;
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
                    _logger.LogWarning("未対応の拡張子 {Ext}。Turtle として読み込みます", ext);
                    new TurtleParser().Load(g, filePath);
                    return (Graph)g;
            }
        }
        catch
        {
            throw;
        }
    }

    private Graph LoadGraphFromContent(string content, RdfSerializationFormat format)
    {
        _logger.LogDebug("形式 {Format} で RDF コンテンツを読み込みます", format);

        switch (format)
        {
            case RdfSerializationFormat.Turtle:
                return LoadGraphWithReader(content, (graph, reader) => new TurtleParser().Load(graph, reader));
            case RdfSerializationFormat.Notation3:
                return LoadGraphWithReader(content, (graph, reader) => new Notation3Parser().Load(graph, reader));
            case RdfSerializationFormat.NTriples:
                return LoadGraphWithReader(content, (graph, reader) => new NTriplesParser().Load(graph, reader));
            case RdfSerializationFormat.RdfXml:
                return LoadGraphWithReader(content, (graph, reader) => new RdfXmlParser().Load(graph, reader));
            case RdfSerializationFormat.JsonLd:
                return LoadStoreWithReader(content, new JsonLdParser());
            case RdfSerializationFormat.TriG:
                return LoadStoreWithReader(content, new TriGParser());
            case RdfSerializationFormat.TriX:
                return LoadStoreWithReader(content, new TriXParser());
            case RdfSerializationFormat.NQuads:
                return LoadStoreWithReader(content, new NQuadsParser());
            default:
                _logger.LogWarning("未対応の形式 {Format}。Turtle として読み込みます", format);
                return LoadGraphWithReader(content, (graph, reader) => new TurtleParser().Load(graph, reader));
        }
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
        _logger.LogDebug("データセットを単一グラフにマージしました。グラフ数: {GraphCount}, トリプル数: {TripleCount}", store.Graphs.Count, merged.Triples.Count);
        return merged;
    }

    private List<Site> ExtractSites(IGraph graph)
    {
        var sites = new List<Site>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}Site", $"{RecNamespace}Site" });

        foreach (var subject in subjects)
        {
            var site = new Site
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, site);

            var childUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}hasPart", $"{RecNamespace}hasPart" });
            if (childUris.Count > 0)
            {
                site.CustomProperties["hasPartUris"] = childUris;
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
            var building = new Building
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, building);

            var childUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}hasPart", $"{RecNamespace}hasPart" });
            if (childUris.Count > 0)
            {
                building.CustomProperties["hasPartUris"] = childUris;
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
            var level = new Level
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, level);

            var levelNumberValue = GetFirstLiteralValue(graph, subject, new[]
            {
                $"{SbcoNamespace}levelNumber",
                $"{GutpNamespace}levelNumber",
                $"{RecNamespace}levelNumber"
            });

            if (!string.IsNullOrWhiteSpace(levelNumberValue) && int.TryParse(levelNumberValue, out var levelNumber))
            {
                level.LevelNumber = levelNumber;
            }

            var childUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}hasPart", $"{RecNamespace}hasPart" });
            if (childUris.Count > 0)
            {
                level.CustomProperties["hasPartUris"] = childUris;
            }

            levels.Add(level);
        }

        return levels;
    }

    private List<Area> ExtractAreas(IGraph graph)
    {
        var areas = new List<Area>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}Space", $"{RecNamespace}Space", $"{RecNamespace}Area" });

        foreach (var subject in subjects)
        {
            var area = new Area
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, area);

            var equipmentUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}isLocationOf", $"{RecNamespace}isLocationOf" });
            if (equipmentUris.Count > 0)
            {
                area.CustomProperties["equipmentUris"] = equipmentUris;
            }

            areas.Add(area);
        }

        return areas;
    }

    private List<Equipment> ExtractEquipment(IGraph graph)
    {
        var equipmentList = new List<Equipment>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}EquipmentExt", $"{SbcoNamespace}Equipment", $"{GutpNamespace}GUTPEquipment" });

        foreach (var subject in subjects)
        {
            var equipment = new Equipment
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, equipment);
            ExtractAssetProperties(graph, subject, equipment);
            ExtractGutpEquipmentProperties(graph, subject, equipment);

            var pointUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}hasPoint", $"{RecNamespace}hasPoint" });
            if (pointUris.Count > 0)
            {
                equipment.CustomProperties["pointUris"] = pointUris;
            }

            var locatedInUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}locatedIn", $"{RecNamespace}locatedIn" });
            if (locatedInUris.Count > 0)
            {
                equipment.AreaUri = locatedInUris[0];
            }

            equipmentList.Add(equipment);
        }

        return equipmentList;
    }

    private List<Point> ExtractPoints(IGraph graph)
    {
        var points = new List<Point>();
        var subjects = GetSubjectsOfType(graph, new[] { $"{SbcoNamespace}PointExt", $"{SbcoNamespace}Point", $"{GutpNamespace}GUTPPoint" });

        foreach (var subject in subjects)
        {
            var point = new Point
            {
                Uri = subject.ToString()
            };

            ExtractCommonProperties(graph, subject, point);
            ExtractPointProperties(graph, subject, point);

            var isPointOfUris = GetObjectUris(graph, subject, new[] { $"{SbcoNamespace}isPointOf", $"{RecNamespace}isPointOf", $"{BrickNamespace}isPointOf" });
            if (isPointOfUris.Count > 0)
            {
                point.EquipmentUri = isPointOfUris[0];
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
                var key = triple.Subject.ToString();
                if (seen.Add(key))
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

        ExtractIdentifiers(graph, subject, resource);
    }

    private void ExtractGutpEquipmentProperties(IGraph graph, INode subject, Equipment equipment)
    {
        var properties = new Dictionary<string, string>
        {
            { $"{GutpNamespace}gateway_id", nameof(Equipment.GatewayId) },
            { $"{GutpNamespace}device_id", nameof(Equipment.DeviceId) },
            { $"{GutpNamespace}device_type", nameof(Equipment.DeviceType) },
            { $"{GutpNamespace}supplier", nameof(Equipment.Supplier) },
            { $"{GutpNamespace}owner", nameof(Equipment.Owner) }
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

    private void ExtractPointProperties(IGraph graph, INode subject, Point point)
    {
        var stringProperties = new Dictionary<string, string>
        {
            { $"{GutpNamespace}point_id", nameof(Point.PointId) },
            { $"{GutpNamespace}point_type", nameof(Point.PointType) },
            { $"{GutpNamespace}point_specification", nameof(Point.PointSpecification) },
            { $"{GutpNamespace}local_id", nameof(Point.LocalId) },
            { $"{GutpNamespace}unit", nameof(Point.Unit) },
            { $"{GutpNamespace}device_id_bacnet", nameof(Point.DeviceIdBacnet) },
            { $"{GutpNamespace}instance_no_bacnet", nameof(Point.InstanceNoBacnet) },
            { $"{GutpNamespace}object_type_bacnet", nameof(Point.ObjectTypeBacnet) },
            { $"{SbcoNamespace}pointType", nameof(Point.PointType) },
            { $"{SbcoNamespace}pointSpecification", nameof(Point.PointSpecification) },
            { $"{SbcoNamespace}unit", nameof(Point.Unit) }
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

        var writableValue = GetFirstLiteralValue(graph, subject, new[] { $"{GutpNamespace}writable" });
        if (!string.IsNullOrWhiteSpace(writableValue) && bool.TryParse(writableValue, out var writable))
        {
            point.Writable = writable;
        }

        ExtractNumericProperty(graph, subject, new[] { $"{GutpNamespace}interval", $"{SbcoNamespace}intervalCapability" }, value => point.Interval = (int?)value);
        ExtractNumericProperty(graph, subject, new[] { $"{GutpNamespace}max_pres_value", $"{SbcoNamespace}maxPresValue" }, value => point.MaxPresValue = value);
        ExtractNumericProperty(graph, subject, new[] { $"{GutpNamespace}min_pres_value", $"{SbcoNamespace}minPresValue" }, value => point.MinPresValue = value);
        ExtractNumericProperty(graph, subject, new[] { $"{GutpNamespace}scale", $"{SbcoNamespace}scale" }, value => point.Scale = value);

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

    private void BuildHierarchy(BuildingDataModel model)
    {
        var siteByUri = model.Sites.ToDictionary(s => s.Uri, s => s);
        var buildingByUri = model.Buildings.ToDictionary(b => b.Uri, b => b);
        var levelByUri = model.Levels.ToDictionary(l => l.Uri, l => l);
        var areaByUri = model.Areas.ToDictionary(a => a.Uri, a => a);
        var equipmentByUri = model.Equipment.ToDictionary(e => e.Uri, e => e);

        foreach (var site in model.Sites)
        {
            if (site.CustomProperties.TryGetValue("hasPartUris", out var bu) && bu is List<string> buildingUris)
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
            if (building.CustomProperties.TryGetValue("hasPartUris", out var lu) && lu is List<string> levelUris)
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
            if (level.CustomProperties.TryGetValue("hasPartUris", out var au) && au is List<string> areaUris)
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
            if (area.CustomProperties.TryGetValue("equipmentUris", out var eu) && eu is List<string> equipmentUris)
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
            if (equipment.CustomProperties.TryGetValue("pointUris", out var pu) && pu is List<string> pointUris)
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
