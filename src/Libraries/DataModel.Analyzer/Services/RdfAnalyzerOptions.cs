using System;

namespace DataModel.Analyzer.Services
{
    public class RdfAnalyzerOptions
    {
        // デフォルト値は従来と同じファイル名 / フォルダ
        public string OntologyFile { get; set; } = "building_model.owl.ttl";
        public string ShapesFile { get; set; } = "building_model.shacl.ttl";
        public string SchemaFolder { get; set; } = "Schema";
    }
}