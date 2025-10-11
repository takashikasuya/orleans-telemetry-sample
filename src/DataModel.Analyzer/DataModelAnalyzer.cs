using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using Microsoft.Extensions.Logging;

namespace DataModel.Analyzer;

/// <summary>
/// データモデル解析のファサードクラス
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
    /// RDFファイルを解析してデータモデルを生成
    /// </summary>
    /// <param name="rdfFilePath">RDFファイルのパス</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeFromFileAsync(string rdfFilePath)
    {
        _logger.LogInformation("RDFファイルからデータモデルを解析開始: {FilePath}", rdfFilePath);
        return await _rdfAnalyzer.AnalyzeRdfFileAsync(rdfFilePath);
    }

    /// <summary>
    /// RDFコンテンツを解析してデータモデルを生成
    /// </summary>
    /// <param name="content">RDFコンテンツ</param>
    /// <param name="sourceName">ソース名</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeFromContentAsync(string content, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツからデータモデルを解析開始: {SourceName}", sourceName);
        return await _rdfAnalyzer.AnalyzeRdfContentAsync(content, RdfSerializationFormat.Turtle, sourceName);
    }

    /// <summary>
    /// RDFコンテンツを指定形式で解析してデータモデルを生成
    /// </summary>
    /// <param name="content">RDFコンテンツ</param>
    /// <param name="format">コンテンツのフォーマット</param>
    /// <param name="sourceName">ソース名</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeFromContentAsync(string content, RdfSerializationFormat format, string sourceName = "content")
    {
        _logger.LogInformation("RDFコンテンツからデータモデルを解析開始: {SourceName} ({Format})", sourceName, format);
        return await _rdfAnalyzer.AnalyzeRdfContentAsync(content, format, sourceName);
    }

    /// <summary>
    /// データモデルをJSONとしてエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>JSON文字列</returns>
    public string ExportToJson(BuildingDataModel model)
    {
        return _exportService.ExportToJson(model);
    }

    /// <summary>
    /// データモデルをJSONファイルとしてエクスポート
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <param name="filePath">出力ファイルパス</param>
    public async Task ExportToJsonFileAsync(BuildingDataModel model, string filePath)
    {
        await _exportService.ExportToJsonFileAsync(model, filePath);
    }

    /// <summary>
    /// データモデルのサマリーを取得
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>サマリー情報</returns>
    public BuildingDataSummary GetSummary(BuildingDataModel model)
    {
        return _exportService.ExportSummary(model);
    }

    /// <summary>
    /// Orleans用のデバイスコントラクトを生成
    /// </summary>
    /// <param name="model">建物データモデル</param>
    /// <returns>デバイスコントラクトのリスト</returns>
    public List<DeviceContract> ExportToOrleansContracts(BuildingDataModel model)
    {
        return _exportService.ExportToOrleansContracts(model);
    }

    /// <summary>
    /// RDFファイルを解析して完全な処理を実行（解析→エクスポート）
    /// </summary>
    /// <param name="rdfFilePath">RDFファイルのパス</param>
    /// <param name="outputDirectory">出力ディレクトリ</param>
    /// <returns>処理結果</returns>
    public async Task<AnalysisResult> ProcessRdfFileAsync(string rdfFilePath, string? outputDirectory = null)
    {
        _logger.LogInformation("RDFファイルの完全処理を開始: {FilePath}", rdfFilePath);

        var result = new AnalysisResult
        {
            SourceFile = rdfFilePath,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // 解析
            var model = await AnalyzeFromFileAsync(rdfFilePath);
            result.Model = model;
            result.Summary = GetSummary(model);
            result.OrleansContracts = ExportToOrleansContracts(model);

            // 出力ディレクトリが指定されている場合はファイル出力
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);

                var baseFileName = Path.GetFileNameWithoutExtension(rdfFilePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // JSONファイル出力
                var jsonFilePath = Path.Combine(outputDirectory, $"{baseFileName}_{timestamp}.json");
                await ExportToJsonFileAsync(model, jsonFilePath);
                result.OutputFiles.Add("json", jsonFilePath);

                // サマリーファイル出力
                var summaryFilePath = Path.Combine(outputDirectory, $"{baseFileName}_summary_{timestamp}.json");
                var summaryJson = System.Text.Json.JsonSerializer.Serialize(result.Summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(summaryFilePath, summaryJson);
                result.OutputFiles.Add("summary", summaryFilePath);
            }

            result.IsSuccess = true;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation("RDFファイルの完全処理が完了: {FilePath}, 処理時間: {Duration}ms",
                rdfFilePath, (result.EndTime - result.StartTime).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;

            _logger.LogError(ex, "RDFファイルの処理中にエラーが発生しました: {FilePath}", rdfFilePath);
            throw;
        }
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

    public TimeSpan ProcessingTime => EndTime - StartTime;
}