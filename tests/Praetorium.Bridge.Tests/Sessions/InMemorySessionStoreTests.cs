using System;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Sessions;
using Xunit;

namespace Praetorium.Bridge.Tests.Sessions;

public class InMemorySessionStoreTests
{
    private readonly InMemorySessionStore _store = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static SessionInfo Make(
        string id,
        string tool = "t",
        string? referenceId = null,
        string? connectionId = null,
        SessionState state = SessionState.Active)
    {
        return new SessionInfo(id, tool, state, DateTimeOffset.UtcNow, referenceId, connectionId);
    }

    [Fact]
    public async Task Set_Get_RoundTrip()
    {
        var s = Make("s1");
        await _store.SetAsync(s, _ct);
        var loaded = await _store.GetAsync("s1", _ct);
        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded!.SessionId);
    }

    [Fact]
    public async Task GetByReference_FindsMatch()
    {
        await _store.SetAsync(Make("s1", "tool-a", referenceId: "ref-1"), _ct);
        await _store.SetAsync(Make("s2", "tool-a", referenceId: "ref-2"), _ct);

        var found = await _store.GetByReferenceAsync("tool-a", "ref-2", _ct);
        Assert.NotNull(found);
        Assert.Equal("s2", found!.SessionId);
    }

    [Fact]
    public async Task GetByConnection_FindsMatch()
    {
        await _store.SetAsync(Make("s1", "tool-a", connectionId: "c-1"), _ct);
        var found = await _store.GetByConnectionAsync("tool-a", "c-1", _ct);
        Assert.NotNull(found);
        Assert.Equal("s1", found!.SessionId);
    }

    [Fact]
    public async Task GetGlobal_ReturnsSessionWithoutRefOrConn()
    {
        await _store.SetAsync(Make("s1", "tool-a"), _ct);
        await _store.SetAsync(Make("s2", "tool-a", referenceId: "r"), _ct);

        var found = await _store.GetGlobalAsync("tool-a", _ct);
        Assert.NotNull(found);
        Assert.Equal("s1", found!.SessionId);
    }

    [Fact]
    public async Task Remove_DeletesSession()
    {
        await _store.SetAsync(Make("s1"), _ct);
        await _store.RemoveAsync("s1", _ct);
        Assert.Null(await _store.GetAsync("s1", _ct));
    }

    [Fact]
    public async Task GetAll_ReturnsAll()
    {
        await _store.SetAsync(Make("s1"), _ct);
        await _store.SetAsync(Make("s2"), _ct);
        var all = await _store.GetAllAsync(_ct);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetByState_FiltersCorrectly()
    {
        await _store.SetAsync(Make("s1", state: SessionState.Active), _ct);
        await _store.SetAsync(Make("s2", state: SessionState.Pooled), _ct);
        var pooled = await _store.GetByStateAsync(SessionState.Pooled, _ct);
        Assert.Single(pooled);
        Assert.Equal("s2", pooled[0].SessionId);
    }
}
