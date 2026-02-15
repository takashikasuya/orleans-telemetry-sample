# Telemetry Real-time Viewer Design

## Purpose

Admin UI の「Load Telemetry」機能を拡張し、データの鮮度に応じた階層的な取得とリアルタイム更新を実現する。

## 現状の課題

1. **Cold Start 問題**: Parquet ファイルは 15 分バケット単位で生成されるため、直近数分のデータが見えない
2. **手動リロード**: Load Telemetry ボタンを押さないとチャートが更新されない
3. **Hot Data 取得なし**: Grain から直接最新データを取得する仕組みがない

## 設計ゴール

1. **データ階層化**: Hot/Warm/Cold データソースを統合して表示
2. **リアルタイム更新**: SignalR 経由で DeviceUpdates ストリームを購読し、チャートを自動更新
3. **パフォーマンス**: 過去データは Parquet、最新データは Grain/Stage から取得
4. **UX 改善**: ローディング UX とエラーハンドリングの改善

## データ階層設計

### 2層アーキテクチャ + ブラウザキャッシュ

```
┌─────────────────────────────────────────────────────────────┐
│ Browser-Side Hot Cache (0-2分)                               │
│ - SignalR 経由で受信したサンプルをブラウザ側で蓄積              │
│ - 用途: リアルタイムチャート、最新値表示                        │
│ - メモリ効率: クライアント側でのみ保持、サーバーは state-less   │
└─────────────────────────────────────────────────────────────┘
                           ↓ fallback
┌─────────────────────────────────────────────────────────────┐
│ Cold Data (2分以上前)                                          │
│ - Parquet + Index (既存実装)                                  │
│ - 用途: 長期データ、トレンド分析                               │
│ - Stage files (15分バケット生成前) も Parquet query 経由で取得 │
└─────────────────────────────────────────────────────────────┘

[Note] PointGrain ring buffer removed for memory efficiency.
       Hot data caching shifted to browser-side via SignalR streaming.
```

## 実装計画

### Phase 1: Hot Data 取得（Grain API 拡張）

**PointGrain に追加:**

```csharp
public interface IPointGrain : IGrainWithStringKey
{
    // 既存メソッド...
    
    // 新規: 直近 N サンプル取得（メモリ効率のため空を返す）
    Task<IReadOnlyList<PointSample>> GetRecentSamplesAsync(int maxSamples = 100);
}

public record PointSample(DateTimeOffset Timestamp, object? Value);
```

**実装:**
- ~~PointGrain 内部でリングバッファ（最大 200-500 サンプル）を保持~~
- **[REMOVED]** リングバッファはメモリ効率の問題で削除済み
- `GetRecentSamplesAsync()` は空のリストを返す（インターフェース互換性のため保持）
- **Hot data はブラウザ側で SignalR ストリーム経由で蓄積**

**AdminMetricsService に追加:**

```csharp
// QueryHotDataAsync は空リストを返すため、実質的に使用されない
// QueryPointTrendHybridAsync は Parquet データのみを返す
public async Task<IReadOnlyList<PointTrendSample>> QueryHotDataAsync(
    string tenantId,
    string deviceId,
    string pointId,
    int maxSamples = 100,
    CancellationToken ct = default)
{
    var grainKey = $"{tenantId}/{deviceId}/{pointId}";
    var grain = _client.GetGrain<IPointGrain>(grainKey);
    var samples = await grain.GetRecentSamplesAsync(maxSamples);
    return samples.Select(s => new PointTrendSample(s.Timestamp, NormalizeValue(s.Value), ...)).ToList();
}
```

### Phase 2: ~~Warm Data 取得（Stage ファイル読み取り）~~

**[SIMPLIFIED]** Stage ファイルは ParquetTelemetryStorageQuery 経由で取得可能。専用 API は不要。

### Phase 3: 統合クエリ（Hot + Cold → Cold のみ + SignalR ストリーム）

**AdminMetricsService に統合メソッド:**

```csharp
public async Task<IReadOnlyList<PointTrendSample>> QueryPointTrendHybridAsync(
    string tenantId,
    string deviceId,
    string pointId,
    TimeSpan duration,
```csharp
public async Task<IReadOnlyList<PointTrendSample>> QueryPointTrendHybridAsync(
    string tenantId,
    string deviceId,
    string pointId,
    TimeSpan duration,
    int maxSamples = 240,
    CancellationToken ct = default)
{
    var to = DateTimeOffset.UtcNow;
    var from = to - duration;
    
    var results = new List<PointTrendSample>();
    
    // Cold Data (Parquet + Stage files)
    var coldRequest = new TelemetryQueryRequest(tenantId, deviceId, from, to, pointId, maxSamples);
    var coldData = await _storageQuery.QueryAsync(coldRequest, ct);
    results.AddRange(coldData.Select(Map));
    
    // [REMOVED] Hot Data query - ring buffer removed for memory efficiency
    // Real-time updates handled by SignalR streaming + browser-side cache
    
    return results.OrderBy(s => s.Timestamp).ToList();
}
```

**[ARCHITECTURE CHANGE]**
- 統合クエリは Parquet (Cold) データのみを返す
- 直近 2 分のデータは SignalR 経由でブラウザ側に push
- ブラウザ側で受信したサンプルをローカルキャッシュに蓄積
- チャート描画時にブラウザキャッシュ + Parquet データをマージ

### Phase 4: SignalR リアルタイム更新

**AdminGateway/Hubs/TelemetryHub.cs を追加:**

```csharp
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Streams;

public class TelemetryHub : Hub
{
    private readonly IClusterClient _client;
    
    public async Task SubscribeToPoint(string tenantId, string deviceId, string pointId)
    {
        var grainKey = $"{tenantId}/{deviceId}";
        var streamProvider = _client.GetStreamProvider("DeviceUpdates");
        var stream = streamProvider.GetStream<DeviceSnapshot>(StreamId.Create("DeviceUpdatesNs", grainKey));
        
        var subscription = await stream.SubscribeAsync(async (snapshot, token) =>
        {
            // フィルタリング: 指定された pointId のみ
            if (snapshot.Points.TryGetValue(pointId, out var point))
            {
                await Clients.Caller.SendAsync("ReceivePointUpdate", new
                {
                    Timestamp = point.UpdatedAt,
                    Value = point.LatestValue
                });
            }
        });
        
        // cleanup on disconnect
        Context.ConnectionAborted.Register(() => subscription.UnsubscribeAsync());
    }
}
```

**Program.cs に追加:**

```csharp
builder.Services.AddSignalR();
app.MapHub<TelemetryHub>("/telemetryHub");
```

**Admin.razor に SignalR クライアント追加:**

```razor
@inject IJSRuntime JS

<script src="_framework/blazor.server.js"></script>
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@latest/dist/browser/signalr.min.js"></script>
<script>
let telemetryConnection = null;

window.subscribeToPointUpdates = async (tenantId, deviceId, pointId, dotNetHelper) => {
    if (telemetryConnection) {
        await telemetryConnection.stop();
    }
    
    telemetryConnection = new signalR.HubConnectionBuilder()
        .withUrl("/telemetryHub")
        .build();
    
    telemetryConnection.on("ReceivePointUpdate", (update) => {
        dotNetHelper.invokeMethodAsync('OnPointUpdate', update);
    });
    
    await telemetryConnection.start();
    await telemetryConnection.invoke("SubscribeToPoint", tenantId, deviceId, pointId);
};

window.unsubscribeFromPointUpdates = async () => {
    if (telemetryConnection) {
        await telemetryConnection.stop();
        telemetryConnection = null;
    }
};
</script>
```

**Admin.razor.cs に追加:**

```csharp
private DotNetObjectReference<Admin>? _dotNetRef;

protected override void OnInitialized()
{
    _dotNetRef = DotNetObjectReference.Create(this);
}

private async Task LoadPointTrendAsync()
{
    // ... 既存の履歴データ取得 ...
    
    // リアルタイム更新開始
    await JS.InvokeVoidAsync("subscribeToPointUpdates", tenantId, deviceId, pointId, _dotNetRef);
}

[JSInvokable]
public async Task OnPointUpdate(PointUpdateDto update)
{
    // チャートデータに追加
    _pointTrendSamples = _pointTrendSamples
        .Append(new PointTrendSample(update.Timestamp, update.Value, ...))
        .OrderBy(s => s.Timestamp)
        .TakeLast(300) // 最大 300 サンプル保持
        .ToList();
    
    await InvokeAsync(StateHasChanged);
}

public void Dispose()
{
    _dotNetRef?.Dispose();
    JS.InvokeVoidAsync("unsubscribeFromPointUpdates");
}
```

## UI 改善案

### チャート更新モード切り替え

```razor
<label>
    <input type="checkbox" @bind="_enableRealTimeUpdate" />
    Real-time update
</label>
```

- チェックON: SignalR購読、自動更新
- チェックOFF: 静的チャート、手動リロードのみ

### インジケーター表示

```razor
@if (_enableRealTimeUpdate && _isSubscribed)
{
    <span class="badge badge-success">
        <span class="pulse-dot"></span> Live
    </span>
}
```

### データソース表示

```text
Loaded 240 samples (Hot: 23, Warm: 67, Cold: 150)
```

## パフォーマンス考慮事項

### メモリ使用量

- Hot Data: PointGrain あたり最大 500 サンプル × 20 bytes ≈ 10 KB
- 10,000 Points → 100 MB（許容範囲）

### SignalR スケーラビリティ

- Orleans Stream はメモリストリーム → 単一 Silo で動作
- Multi-Silo 構成では Redis Backplane or Azure SignalR Service 検討

### クエリ最適化

- 1時間以上のクエリ: Cold Data のみ (Hot/Warm スキップ)
- リアルタイム更新: Hot Data のみ購読

## 実装優先順位

1. **Phase 1 (High)**: PointGrain.GetRecentSamplesAsync 実装
2. **Phase 2 (Medium)**: Stage ファイル読み取り実装
3. **Phase 3 (High)**: QueryPointTrendHybridAsync 統合
4. **Phase 4 (High)**: SignalR リアルタイム更新

## 検証方法

1. **Cold Start テスト**: システム起動直後に Load Telemetry → Hot Data が表示される
2. **リアルタイム更新テスト**: Publisher 起動中にチャートが自動更新される
3. **長期データテスト**: 6時間クエリ → Cold/Warm/Hot が統合される
4. **パフォーマンステスト**: 100 同時購読 → CPU/Memory 監視

## 参考実装

- ApiGateway の DeviceService.SubscribeToDeviceUpdatesAsync (既存)
- PROJECT_OVERVIEW.md の DeviceUpdates ストリーム説明
