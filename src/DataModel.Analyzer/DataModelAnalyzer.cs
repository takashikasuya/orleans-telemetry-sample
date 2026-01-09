using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace DataModel.Analyzer;

/// <summary>
/// データモデル解析ファサードクラス
/// </summary>
public class DataModelAnalyzer
{
    private readonly RdfAnalyzerService _rdfAnalyzer;
    private readonly DataModelExportService _exportService;
    private readonly ILogger<DataModelAnalyzer> _logger;

    public DataModelAnalyzer(
        RdfAnalyzerService rdfAnalyzer,
        DataModelExportService exportService,
        ILogger<DataModelAnalyzer> logger)
    {
        _rdfAnalyzer = rdfAnalyzer;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// RDFファイルを解析してデータモデルを生成する
    /// </summary>
    public async Task<BuildingDataModel> AnalyzeFromFileAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルからデータモデルを解析開始: {FilePath}", rdfFilePath);
        return await _rdfAnalyzer.AnalyzeRdfFileAsync(rdfFilePath);
    }

    /// <summary>
    /// RDFコンテンツを解析してデータモデルを生成する
    /// </summary>
    public async Task<BuildingDataModel> AnalyzeFromContentAsync(string content, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツからデータモデルを解析開始: {SourceName}", sourceName);
        return await _rdfAnalyzer.AnalyzeRdfContentAsync(content, RdfSerializationFormat.Turtle, sourceName);
    }

    /// <summary>
    /// RDFコンテンツを指定形式で解析してデータモデルを生成する
    /// </summary>
    public async Task<BuildingDataModel> AnalyzeFromContentAsync(string content, RdfSerializationFormat format, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツからデータモデルを解析開始: {SourceName} ({Format})", sourceName, format);
        return await _rdfAnalyzer.AnalyzeRdfContentAsync(content, format, sourceName);
    }

    /// <summary>
    /// RDFコンテンツを解析し、SHACLバリデーション結果付きで返す
    /// </summary>
    public async Task<RdfAnalysisResult> AnalyzeFromContentWithValidationAsync(string content, RdfSerializationFormat format, string sourceName = "content", string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFコンテンツからデータモデルを解析 (検証付き): {SourceName} ({Format})", sourceName, format);
        return await _rdfAnalyzer.AnalyzeRdfContentWithValidationAsync(content, format, sourceName, shaclFilePath);
    }

    /// <summary>
    /// RDFファイルを解析し、SHACLバリデーション結果付きで返す
    /// </summary>
    public async Task<RdfAnalysisResult> AnalyzeFromFileWithValidationAsync(string rdfFilePath, string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFファイルからデータモデルを解析 (検証付き): {FilePath}", rdfFilePath);
        return await _rdfAnalyzer.AnalyzeRdfFileWithValidationAsync(rdfFilePath, shaclFilePath);
    }

    /// <summary>
    /// データモデルをJSONとしてエクスポートする
    /// </summary>
    public string ExportToJson(BuildingDataModel model)
    {
        return _exportService.ExportToJson(model);
    }

    /// <summary>
    /// データモデルをJSONファイルとしてエクスポートする
    /// </summary>
    public async Task ExportToJsonFileAsync(BuildingDataModel model, string filePath)
    {
        await _exportService.ExportToJsonFileAsync(model, filePath);
    }

    /// <summary>
    /// データモデルのサマリーを取得する
    /// </summary>
    public BuildingDataSummary GetSummary(BuildingDataModel model)
    {
        return _exportService.ExportSummary(model);
    }

    /// <summary>
    /// Orleans用のデバイスコントラクトを生成する
    /// </summary>
    public List<DeviceContract> ExportToOrleansContracts(BuildingDataModel model)
    {
        return _exportService.ExportToOrleansContracts(model);
    }

    /// <summary>
    /// RDFファイルを解析して処理を実行（解析・エクスポート）
    /// </summary>
    public async Task<AnalysisResult> ProcessRdfFileAsync(string rdfFilePath, string? outputDirectory = null, string? shaclFilePath = null)
    {
        _logger.LogInformation("RDFファイルの処理開始: {FilePath}", rdfFilePath);

        var result = new AnalysisResult
        {
            SourceFile = rdfFilePath,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var analysis = await _rdfAnalyzer.AnalyzeRdfFileWithValidationAsync(rdfFilePath, shaclFilePath);
            var model = analysis.Model;

            result.Model = model;
            result.ShaclConforms = analysis.Validation?.Conforms;
            result.ShaclReport = analysis.Validation?.ReportText;
            if (analysis.Validation?.Messages is { } messages)
            {
                result.ShaclMessages.AddRange(messages);
            }
            result.Summary = GetSummary(model);
            result.OrleansContracts = ExportToOrleansContracts(model);

            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);

                var baseFileName = Path.GetFileNameWithoutExtension(rdfFilePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                var jsonFilePath = Path.Combine(outputDirectory, $"{baseFileName}_{timestamp}.json");
                await ExportToJsonFileAsync(model, jsonFilePath);
                result.OutputFiles.Add("json", jsonFilePath);

                var summaryFilePath = Path.Combine(outputDirectory, $"{baseFileName}_summary_{timestamp}.json");
                var summaryJson = System.Text.Json.JsonSerializer.Serialize(result.Summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(summaryFilePath, summaryJson);
                result.OutputFiles.Add("summary", summaryFilePath);

                if (analysis.Validation != null)
                {
                    var shaclReportPath = Path.Combine(outputDirectory, $"{baseFileName}_shacl_{timestamp}.txt");
                    await WriteShaclReportAsync(shaclReportPath, analysis.Validation, rdfFilePath);
                    result.OutputFiles.Add("shacl", shaclReportPath);
                    result.ShaclReportFile = shaclReportPath;
                }
            }

            result.IsSuccess = true;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation("RDFファイルの処理完了: {FilePath}, 処理時間 {Duration}ms",
                rdfFilePath, (result.EndTime - result.StartTime).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;

            _logger.LogError(ex, "RDFファイルの処理にエラーが発生しました: {FilePath}", rdfFilePath);
            throw;
        }
    }

    private static async Task WriteShaclReportAsync(string filePath, RdfValidationResult validation, string source)
    {
        var lines = new List<string>
        {
            $"Source: {source}",
            $"Conforms: {validation.Conforms}",
            "Messages:"
        };

        if (validation.Messages.Any())
        {
            lines.AddRange(validation.Messages.Select(m => $" - {m}"));
        }
        else
        {
            lines.Add(" - (none)");
        }

        if (!string.IsNullOrWhiteSpace(validation.ReportText))
        {
            lines.Add("Report:");
            lines.Add(validation.ReportText);
        }

        await File.WriteAllLinesAsync(filePath, lines);
    }

    [Obsolete("Use ProcessRdfFileAsync instead.")]
    public Task<AnalysisResult> ProcessTtlFileAsync(string ttlFilePath, string? outputDirectory = null)
        => ProcessRdfFileAsync(ttlFilePath, outputDirectory);
}

/// <summary>
/// 解析結果を表すクラス
/// </summary>
public class AnalysisResult
{
    public string SourceFile { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public BuildingDataModel? Model { get; set; }
    public BuildingDataSummary? Summary { get; set; }
    public List<DeviceContract> OrleansContracts { get; set; } = new();
    public Dictionary<string, string> OutputFiles { get; set; } = new();
    public bool? ShaclConforms { get; set; }
    public string? ShaclReport { get; set; }
    public List<string> ShaclMessages { get; set; } = new();
    public string? ShaclReportFile { get; set; }

    public TimeSpan ProcessingTime => EndTime - StartTime;
}
