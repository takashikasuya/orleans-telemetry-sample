using DataModel.Analyzer.Models;

namespace DataModel.Analyzer.Services;

public sealed class RdfValidationResult
{
    public bool Conforms { get; init; }
    public string ReportText { get; init; } = string.Empty;
    public List<string> Messages { get; init; } = new();
}

public sealed class RdfAnalysisResult
{
    public BuildingDataModel Model { get; init; } = new();
    public RdfValidationResult? Validation { get; init; }
}
