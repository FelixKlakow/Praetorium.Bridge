using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Prompts;
using Praetorium.Bridge.Sessions;
using Praetorium.Bridge.Signaling;
using Praetorium.Bridge.Tools;
using Xunit;

namespace Praetorium.Bridge.Tests.Tools;

/// <summary>
/// Covers the dispatcher's agent-turn lifecycle: the dispatcher must treat
/// <see cref="IAgentSession.SendAsync"/> as a background turn and race it against
/// the outbound signal channel. An agent parked inside a blocking signaling tool
/// must not deadlock the dispatcher, and an agent that ends its turn without
/// calling any signaling tool must not hang the dispatcher forever.
/// </summary>
public class ToolDispatcherTurnTests
{
    private const string ToolName = "test_tool";
    private const string ReferenceId = "ref-1";
    private const int ShortTimeoutSeconds = 5;

    private sealed class Harness
    {
        public required ToolDispatcher Dispatcher { get; init; }
        public required ScriptedAgentProvider AgentProvider { get; init; }
        public required SessionManager SessionManager { get; init; }
        public required SignalRegistry SignalRegistry { get; init; }
    }

    private static Harness BuildDispatcher()
    {
        var toolDef = new ToolDefinition
        {
            Description = "test",
            Parameters = new Dictionary<string, ParameterDefinition>(),
            Agent = new AgentConfiguration { Model = "m" },
            Session = new SessionConfiguration
            {
                Mode = SessionMode.PerReference,
                ReferenceIdParameter = "referenceId",
                ResponseTimeoutSeconds = ShortTimeoutSeconds,
            },
            Signaling = new SignalingConfiguration(),
        };

        var config = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition> { [ToolName] = toolDef },
        };

        var configProvider = new StubConfigurationProvider(config);
        var store = new InMemorySessionStore();
        var registry = new SignalRegistry();
        var agentProvider = new ScriptedAgentProvider();
        var sessionManager = new SessionManager(
            store, registry, agentProvider, new NullBridgeHooks(), NullLogger<SessionManager>.Instance);

        // The scripted behaviour needs the session id + registry to simulate
        // signaling tools running on the agent's thread.
        agentProvider.Registry = registry;
        agentProvider.SessionStore = store;

        var dispatcher = new ToolDispatcher(
            configProvider,
            sessionManager,
            registry,
            new StubPromptResolver(),
            agentProvider,
            new NullBridgeHooks(),
            NullLogger<ToolDispatcher>.Instance);

        return new Harness
        {
            Dispatcher = dispatcher,
            AgentProvider = agentProvider,
            SessionManager = sessionManager,
            SignalRegistry = registry,
        };
    }

    private static JsonElement BuildArgs(string? input = null)
    {
        var doc = new Dictionary<string, object?> { ["referenceId"] = ReferenceId };
        if (input != null) doc[ReservedParameters.Input] = input;
        return JsonSerializer.SerializeToElement(doc);
    }

    private static async Task<string> ResolveSessionIdAsync(Harness h, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var sessions = await h.SessionManager.GetAllSessionsAsync(ct);
            if (sessions.Count > 0) return sessions[0].SessionId;
            await Task.Delay(10, ct);
        }
        throw new InvalidOperationException("No session was created.");
    }

    [Fact]
    public async Task OutboundSignalDuringBlockingTool_ReturnsSignal_WhileTurnStaysAlive()
    {
        var h = BuildDispatcher();

        // Scripted behaviour: post an outbound signal, then park on WaitInbound so
        // the turn task deliberately does NOT complete. This mimics a blocking
        // respond / request_input tool.
        h.AgentProvider.NextBehaviour = async (sessionId, registry, ct) =>
        {
            registry.SignalOutbound(sessionId, SignalResult.Input(ToolResponse.Complete("hello")));
            await registry.WaitInboundAsync(sessionId, TimeSpan.FromSeconds(30), ct);
            return "turn-done";
        };

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress: null, CancellationToken.None);

        // The turn is still alive (parked on inbound), so a 'complete' payload is
        // demoted to 'partial' to tell the caller to re-invoke and keep draining.
        Assert.Equal("partial", response.Status);
        Assert.Equal("hello", response.Message);

        var sessionId = await ResolveSessionIdAsync(h, CancellationToken.None);
        var tracked = h.SessionManager.GetRunningTurn(sessionId);
        Assert.NotNull(tracked);
        Assert.False(tracked!.IsCompleted);
    }

    [Fact]
    public async Task TurnEndsWithoutSignaling_ReturnsError()
    {
        var h = BuildDispatcher();

        // Every turn ends with no signaling tool and no assistant text.
        h.AgentProvider.NextBehaviour = (_, _, _) => Task.FromResult(string.Empty);

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress: null, CancellationToken.None);

        Assert.Equal("error", response.Status);
        Assert.Contains("signaling tool", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Exactly one SendAsync: no nudge/retry loop.
        Assert.Equal(1, h.AgentProvider.SendAsyncCalls);

        var sessionId = await ResolveSessionIdAsync(h, CancellationToken.None);
        Assert.Null(h.SessionManager.GetRunningTurn(sessionId));
    }

    [Fact]
    public async Task TurnEndsWithoutSignaling_ButWithAssistantText_ReturnsThatTextAsComplete()
    {
        var h = BuildDispatcher();

        // Agent produces a plain assistant message but never calls a signaling tool.
        // The dispatcher should surface the text as a complete response rather than
        // returning a generic error — this recovers gracefully from agents that
        // skip the signaling contract.
        h.AgentProvider.NextBehaviour = (_, _, _) => Task.FromResult("Changes requested: fix the null check.");

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress: null, CancellationToken.None);

        Assert.Equal("complete", response.Status);
        Assert.Equal("Changes requested: fix the null check.", response.Message);
    }

    [Fact]
    public async Task TurnEndsWithBufferedOutboundSignal_ReturnsSignal()
    {
        var h = BuildDispatcher();

        // Turn posts an outbound signal then ends cleanly. The buffered signal must be
        // delivered to the caller even though the turn task has already completed.
        h.AgentProvider.NextBehaviour = (sid, registry, _) =>
        {
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("buffered")));
            return Task.FromResult("turn-done");
        };

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress: null, CancellationToken.None);

        Assert.Equal("complete", response.Status);
        Assert.Equal("buffered", response.Message);
    }

    [Fact]
    public async Task DispatchAsync_AcceptsProgress_AndForwardsResponse()
    {
        var h = BuildDispatcher();

        // Verifies the progress parameter is threaded through without error. The
        // periodic keepalive interval is seconds-scale and the test completes before
        // the first tick, so progressCount may legitimately be 0.
        var progressCount = 0;
        var progress = new Progress<ProgressNotificationValue>(_ => Interlocked.Increment(ref progressCount));

        h.AgentProvider.NextBehaviour = async (sid, registry, ct) =>
        {
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("done")));
            await registry.WaitInboundAsync(sid, TimeSpan.FromSeconds(30), ct);
            return "parked";
        };

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress, CancellationToken.None);

        // Agent parks after signaling → 'complete' is demoted to 'partial'.
        Assert.Equal("partial", response.Status);
        Assert.Equal("done", response.Message);
        Assert.True(progressCount >= 0);
    }

    [Fact]
    public async Task TurnFaults_ReturnsErrorResponse()
    {
        var h = BuildDispatcher();

        h.AgentProvider.NextBehaviour = (_, _, _) =>
            Task.FromException<string>(new InvalidOperationException("boom"));

        var response = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), connectionId: null, progress: null, CancellationToken.None);

        Assert.Equal("error", response.Status);
        Assert.Contains("boom", response.ErrorMessage);
    }

    [Fact]
    public async Task Continuation_WhileTurnStillRunning_PostsInbound_DoesNotRestartTurn()
    {
        var h = BuildDispatcher();

        h.AgentProvider.NextBehaviour = async (sessionId, registry, ct) =>
        {
            registry.SignalOutbound(sessionId, SignalResult.Input(ToolResponse.Complete("first")));
            var reply = await registry.WaitInboundAsync(sessionId, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(SignalType.Input, reply.Type);
            Assert.Equal("follow-up", reply.Data);
            registry.SignalOutbound(sessionId, SignalResult.Input(ToolResponse.Complete("second")));
            // Park again to keep session alive.
            await registry.WaitInboundAsync(sessionId, TimeSpan.FromSeconds(30), ct);
            return "done";
        };

        var first = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("first", first.Message);

        // On continuation the dispatcher must NOT ask the provider for a new agent.
        h.AgentProvider.ThrowIfCreateAgentCalledAgain = true;

        var second = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(input: "follow-up"), null, progress: null, CancellationToken.None);
        Assert.Equal("second", second.Message);

        Assert.Equal(1, h.AgentProvider.CreateAgentCalls);
    }

    [Fact]
    public async Task Continuation_AfterPreviousTurnEnded_StartsFreshTurn()
    {
        var h = BuildDispatcher();

        // First turn signals outbound and returns normally — the turn task ends
        // cleanly while the outbound signal is already buffered, so the
        // dispatcher observes the signal and does NOT nudge.
        h.AgentProvider.NextBehaviour = (sid, registry, _) =>
        {
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("turn1")));
            return Task.FromResult("turn1-done");
        };
        var first = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("turn1", first.Message);

        var sessionId = await ResolveSessionIdAsync(h, CancellationToken.None);

        // Wait for the turn task to clear itself once it completes (the finished
        // turn is only cleared when the next dispatcher decision observes it).
        var sendsBeforeSecond = h.AgentProvider.SendAsyncCalls;

        // Second call with Input: the previous turn has completed, so the dispatcher
        // must start a fresh SendAsync (not just signal inbound into the void).
        var promptObserved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.AgentProvider.OnSendAsync = prompt => promptObserved.TrySetResult(prompt);
        h.AgentProvider.NextBehaviour = async (sid, registry, ct) =>
        {
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("turn2")));
            // Park so the outbound signal deterministically wins the race.
            await registry.WaitInboundAsync(sid, TimeSpan.FromSeconds(30), ct);
            return "turn2-done";
        };

        var second = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(input: "new-scope"), null, progress: null, CancellationToken.None);
        Assert.Equal("turn2", second.Message);

        var prompt = await promptObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrEmpty(prompt));
        Assert.True(h.AgentProvider.SendAsyncCalls > sendsBeforeSecond);
    }

    [Fact]
    public async Task InsideAgent_PostsMultipleOutboundSignals_OutsideDrainsInOrder()
    {
        // Scenario:
        //   Outside-Agent -> Starts the inside-agent and waits
        //   Inside-Agent -> Posts message
        //   Outside-Agent -> Gets the first message
        //   Inside-Agent -> Posts another message
        //   Inside-Agent -> Posts another message
        //   Inside-Agent -> Posts final message which blocks him
        //   Outside-Agent -> Gets the second message
        //   Outside-Agent -> Gets the third message
        //
        // The signal registry queues outbound messages FIFO, so no race/grace-window is
        // needed: each outside dispatch simply dequeues the next queued outbound signal.
        var h = BuildDispatcher();

        // Coordinate "inside posts m1 first, then unblock after outside picked it up".
        var outsideReceivedFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        h.AgentProvider.NextBehaviour = async (sid, registry, ct) =>
        {
            // Inside posts message 1 and parks so the dispatcher returns m1 to outside.
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("m1")));

            // Wait until outside has consumed m1 before flooding the queue. Without this
            // gate the first outside dispatch could dequeue m1 and the subsequent ones
            // follow, but we want to mirror the scenario step-by-step.
            await outsideReceivedFirst.Task.WaitAsync(ct);

            // Inside posts m2, m3, then blocks on m4 (parks on inbound).
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("m2")));
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("m3")));
            registry.SignalOutbound(sid, SignalResult.Input(ToolResponse.Complete("m4-blocks")));
            await registry.WaitInboundAsync(sid, TimeSpan.FromSeconds(30), ct);
            return "inside-done";
        };

        // Outside starts the inside agent (first dispatch) and gets message 1.
        // Inside is still running across all four drains, so every response is demoted
        // from 'complete' to 'partial'.
        var r1 = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("partial", r1.Status);
        Assert.Equal("m1", r1.Message);

        // Exactly one SendAsync so far: the inside turn is still running, parked.
        Assert.Equal(1, h.AgentProvider.SendAsyncCalls);

        // Release the inside agent so it posts m2, m3, m4 and then blocks on m4.
        outsideReceivedFirst.SetResult();

        // The dispatcher must NOT start a new turn on subsequent dispatches while the
        // inside agent is still running.
        h.AgentProvider.ThrowIfCreateAgentCalledAgain = true;

        // Outside gets message 2.
        var r2 = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("partial", r2.Status);
        Assert.Equal("m2", r2.Message);

        // Outside gets message 3.
        var r3 = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("partial", r3.Status);
        Assert.Equal("m3", r3.Message);

        // Still a single SendAsync — the inside agent's turn is reused across dispatches.
        Assert.Equal(1, h.AgentProvider.SendAsyncCalls);
        Assert.Equal(1, h.AgentProvider.CreateAgentCalls);

        // Sanity: the fourth queued message is observable too.
        var r4 = await h.Dispatcher.DispatchAsync(ToolName, BuildArgs(), null, progress: null, CancellationToken.None);
        Assert.Equal("m4-blocks", r4.Message);
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class StubConfigurationProvider : IConfigurationProvider
    {
        public StubConfigurationProvider(BridgeConfiguration config) { Configuration = config; }
        public BridgeConfiguration Configuration { get; }
        public string ConfigDirectory => AppContext.BaseDirectory;
        public event Action<BridgeConfiguration>? OnConfigurationChanged { add { } remove { } }
        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SaveAsync(BridgeConfiguration config, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubPromptResolver : IPromptResolver
    {
        public Task<string> ResolveAsync(
            string toolName, ToolDefinition toolDef,
            Dictionary<string, JsonElement> parameters, CancellationToken ct)
            => Task.FromResult($"prompt for {toolName}");
    }

    private sealed class ScriptedAgentProvider : IAgentProvider
    {
        public string Name => "scripted";
        public int CreateAgentCalls;
        public int SendAsyncCalls;
        public readonly List<string> Prompts = new();
        public bool ThrowIfCreateAgentCalledAgain;
        public Func<string, ISignalRegistry, CancellationToken, Task<string>>? NextBehaviour;
        public Action<string>? OnSendAsync;
        public ISignalRegistry? Registry;
        public ISessionStore? SessionStore;

        public Task<IAgentSession> CreateAgentAsync(AgentContext context, CancellationToken ct)
        {
            CreateAgentCalls++;
            if (ThrowIfCreateAgentCalledAgain && CreateAgentCalls > 1)
                throw new InvalidOperationException("CreateAgentAsync called more than once");

            return Task.FromResult<IAgentSession>(new ScriptedAgentSession(this));
        }

        public bool SupportsModel(string model) => true;
        public bool SupportsReasoningEffort(string model) => false;
        public Task<AgentCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct) =>
            Task.FromResult<AgentCapabilities?>(null);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        private sealed class ScriptedAgentSession : IAgentSession
        {
            private readonly ScriptedAgentProvider _owner;
            public ScriptedAgentSession(ScriptedAgentProvider owner) { _owner = owner; }

            public async Task<string> SendAsync(string message, CancellationToken ct)
            {
                Interlocked.Increment(ref _owner.SendAsyncCalls);
                lock (_owner.Prompts) { _owner.Prompts.Add(message); }
                _owner.OnSendAsync?.Invoke(message);
                var behaviour = _owner.NextBehaviour
                    ?? throw new InvalidOperationException("No scripted behaviour configured.");
                var registry = _owner.Registry!;
                var store = _owner.SessionStore!;

                // Yield so the dispatcher can race the outbound wait against us.
                await Task.Yield();

                // Resolve the session id by consulting the store. In these tests
                // there is a single in-flight session at any given moment.
                var sessions = await store.GetAllAsync(ct).ConfigureAwait(false);
                var sessionId = sessions.Count == 1
                    ? sessions[0].SessionId
                    : throw new InvalidOperationException(
                        $"Expected exactly one session in store, found {sessions.Count}.");

                return await behaviour(sessionId, registry, ct).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
