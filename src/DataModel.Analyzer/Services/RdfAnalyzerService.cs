using System;
using System.IO;
using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using Microsoft.Extensions.Logging;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace DataModel.Analyzer.Services;

/// <summary>
/// RDFファイルを解析してデータモデルに変換するサービス
/// </summary>
public class RdfAnalyzerService
{
    private readonly ILogger<RdfAnalyzerService> _logger;

    // 名前空間の定義
    private const string RecNamespace = "https://w3id.org/rec#";
    private const string GutpNamespace = "https://www.gutp.jp/bim-wg#";
    private const string DctNamespace = "http://purl.org/dc/terms/";

    public RdfAnalyzerService(ILogger<RdfAnalyzerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// RDFファイルを読み込んでデータモデルに変換する
    /// </summary>
    /// <param name="rdfFilePath">RDFファイルのパス</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeRdfFileAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルの解析を開始: {FilePath}", rdfFilePath);

        var model = new BuildingDataModel
        {
            Source = rdfFilePath
        };

        try
        {
            // 拡張子に応じて適切なパーサーで読み込み
            var graph = await Task.Run(() => LoadGraphFromFile(rdfFilePath));

            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

            // 各リソースタイプを解析
            model.Sites = ExtractSites(graph);
            model.Buildings = ExtractBuildings(graph);
            model.Levels = ExtractLevels(graph);
            model.Areas = ExtractAreas(graph);
            model.Equipment = ExtractEquipment(graph);
            model.Points = ExtractPoints(graph);

            // 階層関係を構築
            BuildHierarchy(model);

            _logger.LogInformation("RDFファイルの解析完了。Sites: {Sites}, Buildings: {Buildings}, Levels: {Levels}, Areas: {Areas}, Equipment: {Equipment}, Points: {Points}",
                model.Sites.Count, model.Buildings.Count, model.Levels.Count, model.Areas.Count, model.Equipment.Count, model.Points.Count);

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFファイルの解析中にエラーが発生しました: {FilePath}", rdfFilePath);
            throw;
        }
    }

    /// <summary>
    /// RDFコンテンツ文字列を解析してデータモデルに変換する
    /// </summary>
    /// <param name="content">RDFコンテンツ</param>
    /// <param name="format">コンテンツのフォーマット</param>
    /// <param name="sourceName">ソース名</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeRdfContentAsync(string content, RdfSerializationFormat format, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツの解析を開始: {SourceName} ({Format})", sourceName, format);

        var model = new BuildingDataModel
        {
            Source = sourceName
        };

        try
        {
            var graph = await Task.Run(() => LoadGraphFromContent(content, format));

            _logger.LogInformation("RDFグラフの読み込み完了。トリプル数: {TripleCount}", graph.Triples.Count);

            model.Sites = ExtractSites(graph);
            model.Buildings = ExtractBuildings(graph);
            model.Levels = ExtractLevels(graph);
            model.Areas = ExtractAreas(graph);
            model.Equipment = ExtractEquipment(graph);
            model.Points = ExtractPoints(graph);

            BuildHierarchy(model);

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDFコンテンツの解析中にエラーが発生しました: {SourceName}", sourceName);
            throw;
        }
    }

    private Graph LoadGraphFromFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
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
                    _logger.LogWarning("未対応の拡張子 {Ext}。Turtle として試行します。", ext);
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
        _logger.LogDebug("形式 {Format} として RDF コンテンツを解析します", format);

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
                _logger.LogWarning("未対応の形式 {Format}。Turtle として試行します。", format);
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
        _logger.LogDebug("データセット形式を単一グラフにマージしました。グラフ数: {GraphCount}, トリプル数: {TripleCount}", store.Graphs.Count, merged.Triples.Count);
        return merged;
    }

    private List<Site> ExtractSites(IGraph graph)
    {
        var sites = new List<Site>();
        var siteClass = graph.CreateUriNode(UriFactory.Create($"{RecNamespace}Site"));

        var siteTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            siteClass);

        foreach (var triple in siteTriples)
        {
            var site = new Site
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, site);

            // rec:hasPart で Building の URI を収集（後続で階層を組み立て）
            var hasPartTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}hasPart")));

            var childUris = new List<string>();
            foreach (var hp in hasPartTriples)
            {
                childUris.Add(hp.Object.ToString());
            }
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
        var buildingClass = graph.CreateUriNode(UriFactory.Create($"{RecNamespace}Building"));

        var buildingTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            buildingClass);

        foreach (var triple in buildingTriples)
        {
            var building = new Building
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, building);

            // rec:hasPart で Level の URI を収集
            var hasPartTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}hasPart")));
            var childUris = new List<string>();
            foreach (var hp in hasPartTriples)
            {
                childUris.Add(hp.Object.ToString());
            }
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
        var levelClass = graph.CreateUriNode(UriFactory.Create($"{RecNamespace}Level"));

        var levelTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            levelClass);

        foreach (var triple in levelTriples)
        {
            var level = new Level
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, level);

            // レベル番号の抽出
            var levelNumberTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{GutpNamespace}levelNumber")));

            foreach (var levelNumberTriple in levelNumberTriples)
            {
                if (levelNumberTriple.Object is ILiteralNode literalNode &&
                    int.TryParse(literalNode.Value, out var levelNumber))
                {
                    level.LevelNumber = levelNumber;
                    break;
                }
            }

            // rec:hasPart で Area の URI を収集
            var hasPartTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}hasPart")));
            var childUris = new List<string>();
            foreach (var hp in hasPartTriples)
            {
                childUris.Add(hp.Object.ToString());
            }
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
        var areaClass = graph.CreateUriNode(UriFactory.Create($"{RecNamespace}Area"));

        var areaTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            areaClass);

        foreach (var triple in areaTriples)
        {
            var area = new Area
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, area);

            // rec:isLocationOf で Area が保持する Equipment の URI を収集
            var isLocationOfTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}isLocationOf")));
            var equipmentUris = new List<string>();
            foreach (var ilof in isLocationOfTriples)
            {
                equipmentUris.Add(ilof.Object.ToString());
            }
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
        var equipmentClass = graph.CreateUriNode(UriFactory.Create($"{GutpNamespace}GUTPEquipment"));

        var equipmentTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            equipmentClass);

        foreach (var triple in equipmentTriples)
        {
            var equipment = new Equipment
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, equipment);
            ExtractGutpEquipmentProperties(graph, triple.Subject, equipment);

            // rec:hasPoint で Point の URI を収集
            var hasPointTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}hasPoint")));
            var pointUris = new List<string>();
            foreach (var hp in hasPointTriples)
            {
                pointUris.Add(hp.Object.ToString());
            }
            if (pointUris.Count > 0)
            {
                equipment.CustomProperties["pointUris"] = pointUris;
            }
            equipmentList.Add(equipment);
        }

        return equipmentList;
    }

    private List<Point> ExtractPoints(IGraph graph)
    {
        var points = new List<Point>();
        var pointClass = graph.CreateUriNode(UriFactory.Create($"{GutpNamespace}GUTPPoint"));

        var pointTriples = graph.GetTriplesWithPredicateObject(
            graph.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")),
            pointClass);

        foreach (var triple in pointTriples)
        {
            var point = new Point
            {
                Uri = triple.Subject.ToString()
            };

            ExtractCommonProperties(graph, triple.Subject, point);
            ExtractGutpPointProperties(graph, triple.Subject, point);

            // rec:isPointOf で親 Equipment の URI を特定
            var isPointOfTriples = graph.GetTriplesWithSubjectPredicate(
                triple.Subject,
                graph.CreateUriNode(UriFactory.Create($"{RecNamespace}isPointOf")));
            foreach (var ipof in isPointOfTriples)
            {
                point.EquipmentUri = ipof.Object.ToString();
                break;
            }
            points.Add(point);
        }

        return points;
    }

    private void ExtractCommonProperties(IGraph graph, INode subject, RdfResource resource)
    {
        // 名前の抽出
        var nameTriples = graph.GetTriplesWithSubjectPredicate(
            subject, graph.CreateUriNode(UriFactory.Create($"{RecNamespace}name")));

        foreach (var nameTriple in nameTriples)
        {
            if (nameTriple.Object is ILiteralNode literalNode)
            {
                resource.Name = literalNode.Value;
                break;
            }
        }

        // 識別子の抽出
        var identifierTriples = graph.GetTriplesWithSubjectPredicate(
            subject, graph.CreateUriNode(UriFactory.Create($"{RecNamespace}identifiers")));

        foreach (var identifierTriple in identifierTriples)
        {
            var identifierNode = identifierTriple.Object;
            var dctIdentifierTriples = graph.GetTriplesWithSubjectPredicate(
                identifierNode, graph.CreateUriNode(UriFactory.Create($"{DctNamespace}identifier")));

            foreach (var dctTriple in dctIdentifierTriples)
            {
                if (dctTriple.Object is ILiteralNode dctLiteralNode)
                {
                    resource.Identifiers["dtid"] = dctLiteralNode.Value;
                }
            }
        }
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
            var triples = graph.GetTriplesWithSubjectPredicate(
                subject, graph.CreateUriNode(UriFactory.Create(predicateUri)));

            foreach (var triple in triples)
            {
                if (triple.Object is ILiteralNode literalNode)
                {
                    var property = typeof(Equipment).GetProperty(propertyName);
                    property?.SetValue(equipment, literalNode.Value);
                    break;
                }
            }
        }
    }

    private void ExtractGutpPointProperties(IGraph graph, INode subject, Point point)
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
            { $"{GutpNamespace}object_type_bacnet", nameof(Point.ObjectTypeBacnet) }
        };

        foreach (var (predicateUri, propertyName) in stringProperties)
        {
            var triples = graph.GetTriplesWithSubjectPredicate(
                subject, graph.CreateUriNode(UriFactory.Create(predicateUri)));

            foreach (var triple in triples)
            {
                if (triple.Object is ILiteralNode literalNode)
                {
                    var property = typeof(Point).GetProperty(propertyName);
                    property?.SetValue(point, literalNode.Value);
                    break;
                }
            }
        }

        // Boolean properties
        var writableTriples = graph.GetTriplesWithSubjectPredicate(
            subject, graph.CreateUriNode(UriFactory.Create($"{GutpNamespace}writable")));
        foreach (var triple in writableTriples)
        {
            if (triple.Object is ILiteralNode literalNode && bool.TryParse(literalNode.Value, out var writable))
            {
                point.Writable = writable;
                break;
            }
        }

        // Numeric properties
        ExtractNumericProperty(graph, subject, $"{GutpNamespace}interval", value => point.Interval = (int?)value);
        ExtractNumericProperty(graph, subject, $"{GutpNamespace}max_pres_value", value => point.MaxPresValue = value);
        ExtractNumericProperty(graph, subject, $"{GutpNamespace}min_pres_value", value => point.MinPresValue = value);
        ExtractNumericProperty(graph, subject, $"{GutpNamespace}scale", value => point.Scale = value);
    }

    private void ExtractNumericProperty(IGraph graph, INode subject, string predicateUri, Action<double> setValue)
    {
        var triples = graph.GetTriplesWithSubjectPredicate(
            subject, graph.CreateUriNode(UriFactory.Create(predicateUri)));

        foreach (var triple in triples)
        {
            if (triple.Object is ILiteralNode literalNode &&
                double.TryParse(literalNode.Value, out var numericValue))
            {
                setValue(numericValue);
                break;
            }
        }
    }

    private void BuildHierarchy(BuildingDataModel model)
    {
        // URI辞書を作成
        var siteByUri = model.Sites.ToDictionary(s => s.Uri, s => s);
        var buildingByUri = model.Buildings.ToDictionary(b => b.Uri, b => b);
        var levelByUri = model.Levels.ToDictionary(l => l.Uri, l => l);
        var areaByUri = model.Areas.ToDictionary(a => a.Uri, a => a);
        var equipmentByUri = model.Equipment.ToDictionary(e => e.Uri, e => e);

        // Site -> Buildings
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

        // Building -> Levels
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

        // Level -> Areas
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

        // Area -> Equipment
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
                    }
                }
            }
        }

        // Equipment -> Points
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

        // Points with EquipmentUri set via rec:isPointOf but not linked yet
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