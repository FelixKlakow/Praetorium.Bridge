using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Sessions;
using Praetorium.Bridge.Signaling;
using Xunit;

namespace Praetorium.Bridge.Tests.Sessions;

public class SessionManagerTests
{
    private readonly CancellationToken _ct = CancellationToken.None;

    private static SessionManager BuildManager(
        out InMemorySessionStore store,
        out SignalRegistry registry,
        out FakeAgentProvider agentProvider)
    {
        store = new InMemorySessionStore();
        registry = new SignalRegistry();
        agentProvider = new FakeAgentProvider();
        return new SessionManager(
            store,
            registry,
            agentProvider,
            new NullBridgeHooks(),
            NullLogger<SessionManager>.Instance);
    }

    [Fact]
    public async Task GetOrCreate_PerReference_CreatesAndReuses()
    {
        var mgr = BuildManager(out _, out _, out _);
        var agentCfg = new AgentConfiguration { Model = "m" };

        var (s1, isNew1) = await mgr.GetOrCreateSessionAsync(
            "tool", "ref-1", null, SessionMode.PerReference, agentCfg, _ct);
        var (s2, isNew2) = await mgr.GetOrCreateSessionAsync(
            "tool", "ref-1", null, SessionMode.PerReference, agentCfg, _ct);

        Assert.True(isNew1);
        Assert.False(isNew2);
        Assert.Equal(s1.SessionId, s2.SessionId);
    }

    [Fact]
    public async Task GetOrCreate_PerReference_WithoutReferenceId_Throws()
    {
        var mgr = BuildManager(out _, out _, out _);
        var agentCfg = new AgentConfiguration();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mgr.GetOrCreateSessionAsync("tool", null, null, SessionMode.PerReference, agentCfg, _ct));
    }

    [Fact]
    public async Task PooledSession_IsReactivated_OnNextGetOrCreate()
    {
        var mgr = BuildManager(out var store, out _, out _);
        var agentCfg = new AgentConfiguration();

        var (s, _) = await mgr.GetOrCreateSessionAsync(
            "tool", "r-1", null, SessionMode.PerReference, agentCfg, _ct);
        await mgr.PoolSessionAsync(s.SessionId, _ct);

        var pooled = await store.GetAsync(s.SessionId, _ct);
        Assert.Equal(SessionState.Pooled, pooled!.State);

        var (s2, isNew) = await mgr.GetOrCreateSessionAsync(
            "tool", "r-1", null, SessionMode.PerReference, agentCfg, _ct);

        Assert.False(isNew);
        Assert.Equal(s.SessionId, s2.SessionId);
        Assert.Equal(SessionState.Active, s2.State);
    }

    [Fact]
    public async Task CrashedSession_IsReplaced_OnNextGetOrCreate()
    {
        var mgr = BuildManager(out _, out _, out _);
        var agentCfg = new AgentConfiguration();

        var (s1, _) = await mgr.GetOrCreateSessionAsync(
            "tool", "r", null, SessionMode.PerReference, agentCfg, _ct);
        await mgr.MarkCrashedAsync(s1.SessionId, new Exception("boom"), _ct);

        var (s2, isNew) = await mgr.GetOrCreateSessionAsync(
            "tool", "r", null, SessionMode.PerReference, agentCfg, _ct);

        Assert.True(isNew);
        Assert.NotEqual(s1.SessionId, s2.SessionId);
    }

    [Fact]
    public async Task NotifyDisconnect_MarksSessionCrashed_AndSignalsWaiters()
    {
        var mgr = BuildManager(out _, out var registry, out _);
        var agentCfg = new AgentConfiguration();

        var (s, _) = await mgr.GetOrCreateSessionAsync(
            "tool", null, "conn-1", SessionMode.PerConnection, agentCfg, _ct);

        var waitTask = registry.WaitOutboundAsync(s.SessionId, TimeSpan.FromSeconds(5), _ct);
        await mgr.NotifyDisconnectAsync("conn-1", _ct);

        var sig = await waitTask;
        Assert.Equal(SignalType.Disconnect, sig.Type);
    }

    [Fact]
    public async Task ResetSession_MovesToPooled_AndCleansWaiter()
    {
        var mgr = BuildManager(out var store, out var registry, out _);
        var agentCfg = new AgentConfiguration();

        var (s, _) = await mgr.GetOrCreateSessionAsync(
            "tool", "r-1", null, SessionMode.PerReference, agentCfg, _ct);
        await mgr.ResetSessionAsync(s.SessionId, _ct);

        var pooled = await store.GetAsync(s.SessionId, _ct);
        Assert.Equal(SessionState.Pooled, pooled!.State);

        // Queue should be clear after reset; wait times out.
        var result = await registry.WaitOutboundAsync(
            s.SessionId, TimeSpan.FromMilliseconds(100), _ct);
        Assert.Equal(SignalType.Timeout, result.Type);
    }

    private sealed class FakeAgentProvider : IAgentProvider
    {
        public string Name => "fake";
        public Task<IAgentSession> CreateAgentAsync(AgentContext context, CancellationToken ct) =>
            Task.FromResult<IAgentSession>(new FakeAgentSession());
        public bool SupportsModel(string model) => true;
        public bool SupportsReasoningEffort(string model) => false;
        public Task<AgentCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct) =>
            Task.FromResult<AgentCapabilities?>(null);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class FakeAgentSession : IAgentSession
    {
        public Task<string> SendAsync(string message, CancellationToken ct) => Task.FromResult("ok");
    }
}
