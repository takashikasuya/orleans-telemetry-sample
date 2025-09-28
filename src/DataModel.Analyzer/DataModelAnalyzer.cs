using DataModel.Analyzer.Models;
using DataModel.Analyzer.Services;
using Microsoft.Extensions.Logging;

namespace DataModel.Analyzer;

/// <summary>
/// データモデル解析のファサードクラス
/// </summary>
public class DataModelAnalyzer
{
    private readonly TtlAnalyzerService _ttlAnalyzer;
    private readonly DataModelExportService _exportService;
    private readonly ILogger<DataModelAnalyzer> _logger;

    public DataModelAnalyzer(
        TtlAnalyzerService ttlAnalyzer,
        DataModelExportService exportService,
        ILogger<DataModelAnalyzer> logger)
    {
        _ttlAnalyzer = ttlAnalyzer;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// TTLファイルを解析してデータモデルを生成
    /// </summary>
    /// <param name="ttlFilePath">TTLファイルのパス</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeFromFileAsync(string ttlFilePath)
    {
        _logger.LogInformation("TTLファイルからデータモデルを解析開始: {FilePath}", ttlFilePath);
        return await _ttlAnalyzer.AnalyzeTtlFileAsync(ttlFilePath);
    }

    /// <summary>
    /// TTLコンテンツを解析してデータモデルを生成
    /// </summary>
    /// <param name="ttlContent">TTLコンテンツ</param>
    /// <param name="sourceName">ソース名</param>
    /// <returns>建物データモデル</returns>
    public async Task<BuildingDataModel> AnalyzeFromContentAsync(string ttlContent, string sourceName = "content")
    {
        _logger.LogInformation("TTLコンテンツからデータモデルを解析開始: {SourceName}", sourceName);
        return await _ttlAnalyzer.AnalyzeTtlContentAsync(ttlContent, sourceName);
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
    /// TTLファイルを解析して完全な処理を実行（解析→エクスポート）
    /// </summary>
    /// <param name="ttlFilePath">TTLファイルのパス</param>
    /// <param name="outputDirectory">出力ディレクトリ</param>
    /// <returns>処理結果</returns>
    public async Task<AnalysisResult> ProcessTtlFileAsync(string ttlFilePath, string? outputDirectory = null)
    {
        _logger.LogInformation("TTLファイルの完全処理を開始: {FilePath}", ttlFilePath);

        var result = new AnalysisResult
        {
            SourceFile = ttlFilePath,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // 解析
            var model = await AnalyzeFromFileAsync(ttlFilePath);
            result.Model = model;
            result.Summary = GetSummary(model);
            result.OrleansContracts = ExportToOrleansContracts(model);

            // 出力ディレクトリが指定されている場合はファイル出力
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);

                var baseFileName = Path.GetFileNameWithoutExtension(ttlFilePath);
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

            _logger.LogInformation("TTLファイルの完全処理が完了: {FilePath}, 処理時間: {Duration}ms",
                ttlFilePath, (result.EndTime - result.StartTime).TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;

            _logger.LogError(ex, "TTLファイルの処理中にエラーが発生しました: {FilePath}", ttlFilePath);
            throw;
        }
    }
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