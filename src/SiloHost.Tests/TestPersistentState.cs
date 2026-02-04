using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace SiloHost.Tests;

internal sealed class TestPersistentState<TState> : IPersistentState<TState>
    where TState : class, new()
{
    private readonly Func<TState> _stateFactory;

    public TestPersistentState(Func<TState>? stateFactory = null)
    {
        _stateFactory = stateFactory ?? (() => new TState());
        State = _stateFactory();
        Configuration = new TestPersistentStateConfiguration();
    }

    public TState State { get; set; }

    public string Etag { get; set; } = string.Empty;

    public bool RecordExists { get; set; }

    public Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        State = _stateFactory();
        RecordExists = false;
        return Task.CompletedTask;
    }

    public Task ClearStateAsync()
    {
        return ClearStateAsync(CancellationToken.None);
    }

    public Task ReadStateAsync(CancellationToken cancellationToken = default)
    {
        RecordExists = true;
        return Task.CompletedTask;
    }

    public Task ReadStateAsync()
    {
        return ReadStateAsync(CancellationToken.None);
    }

    public Task WriteStateAsync(CancellationToken cancellationToken = default)
    {
        RecordExists = true;
        return Task.CompletedTask;
    }

    public Task WriteStateAsync()
    {
        return WriteStateAsync(CancellationToken.None);
    }

    public IPersistentStateConfiguration Configuration { get; }

    private sealed class TestPersistentStateConfiguration : IPersistentStateConfiguration
    {
        public string StateName { get; set; } = "test-state";
        public string StorageName { get; set; } = "TestStore";
    }
}
