using DataModel.Analyzer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataModel.Analyzer.Extensions;

/// <summary>
/// 依存性注入の拡張メソッド
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// DataModel.Analyzerサービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddDataModelAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<TtlAnalyzerService>();
        services.AddSingleton<DataModelExportService>();
        services.AddSingleton<DataModelAnalyzer>();
        services.AddSingleton<Integration.OrleansIntegrationService>();

        return services;
    }

    /// <summary>
    /// DataModel.Analyzerサービスをロギング付きで登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configureLogging">ロギング設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddDataModelAnalyzer(
        this IServiceCollection services,
        Action<ILoggingBuilder> configureLogging)
    {
        services.AddLogging(configureLogging);
        return services.AddDataModelAnalyzer();
    }
}