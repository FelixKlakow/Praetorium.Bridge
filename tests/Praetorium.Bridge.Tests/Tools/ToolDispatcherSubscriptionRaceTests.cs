using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Regression tests for the dispatcher's outbound-signal tracking subscription.
/// The previous implementation subscribed to <see cref="ISignalRegistry.Signaled"/>
/// AFTER invoking <c>SendAsync</c>. An agent that emits a signal synchronously \u2014
/// before any <c>await</c> yields \u2014 would slip past the subscription, leaving
/// <c>outboundCount</c> at zero and producing a spurious "Agent ended its turn
/// without calling any signaling tool" error even though the turn was productive.
/// </summary>
public class ToolDispatcherSubscriptionRaceTests
{
    private const string ToolName = "test_tool";
    private const string ReferenceId = "ref-race";

    [Fact]
    public async Task SynchronousOutboundSignal_BeforeFirstAwait_IsCountedAndDelivered()
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
                ResponseTimeoutSeconds = 5,
            },
            Signaling = new SignalingConfiguration(),
        };

        var config = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition> { [ToolName] = toolDef },
        };

        var store = new InMemorySessionStore();
        var registry = new SignalRegistry();
        var agentProvider = new SyncSignalingAgentProvider(registry, store);
        var sessionManager = new SessionManager(
            store, registry, agentProvider, new NullBridgeHooks(), NullLogger<SessionManager>.Instance);

        var dispatcher = new ToolDispatcher(
            new StubConfigProvider(config),
            sessionManager,
            registry,
            new StubPromptResolver(),
            agentProvider,
            new NullBridgeHooks(),
            NullLogger<ToolDispatcher>.Instance);

        var args = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["referenceId"] = ReferenceId,
        });

        var response = await dispatcher.DispatchAsync(ToolName, args, connectionId: null, progress: null, CancellationToken.None);

        // The synchronously-emitted outbound signal must be counted so the dispatcher
        // delivers it rather than injecting the "ended without signaling" error.
        Assert.Equal("complete", response.Status);
        Assert.Equal("sync-hello", response.Message);
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class StubConfigProvider : IConfigurationProvider
    {
        public StubConfigProvider(BridgeConfiguration config) { Configuration = config; }
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
            => Task.FromResult("prompt");
    }

    /// <summary>
    /// An agent provider whose session emits an outbound signal SYNCHRONOUSLY inside
    /// <c>SendAsync</c> \u2014 before the first <c>await</c> \u2014 then returns a completed
    /// task. This reproduces the production race observed with the Copilot SDK.
    /// </summary>
    private sealed class SyncSignalingAgentProvider : IAgentProvider
    {
        private readonly ISignalRegistry _registry;
        private readonly ISessionStore _store;

        public SyncSignalingAgentProvider(ISignalRegistry registry, ISessionStore store)
        {
            _registry = registry;
            _store = store;
        }

        public string Name => "sync-signaling";

        public Task<IAgentSession> CreateAgentAsync(AgentContext context, CancellationToken ct) =>
            Task.FromResult<IAgentSession>(new Session(_registry, _store));

        public bool SupportsModel(string model) => true;
        public bool SupportsReasoningEffort(string model) => false;
        public Task<AgentCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct) =>
            Task.FromResult<AgentCapabilities?>(null);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        private sealed class Session : IAgentSession
        {
            private readonly ISignalRegistry _registry;
            private readonly ISessionStore _store;

            public Session(ISignalRegistry registry, ISessionStore store)
            {
                _registry = registry;
                _store = store;
            }

            public Task<string> SendAsync(string message, CancellationToken ct)
            {
                // Synchronously resolve the current session id \u2014 there is exactly one
                // in-flight session in this test \u2014 and emit the outbound signal BEFORE
                // returning a Task. If the dispatcher subscribes after SendAsync returns,
                // this signal is missed.
                var sessions = _store.GetAllAsync(ct).GetAwaiter().GetResult();
                if (sessions.Count != 1)
                    throw new InvalidOperationException($"Expected exactly one session, got {sessions.Count}.");
                var sessionId = sessions[0].SessionId;

                _registry.SignalOutbound(sessionId, SignalResult.Input(ToolResponse.Complete("sync-hello")));
                return Task.FromResult("turn-done");
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
