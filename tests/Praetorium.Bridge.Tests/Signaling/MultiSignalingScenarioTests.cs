using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Signaling;
using Praetorium.Bridge.Tools;
using Xunit;

namespace Praetorium.Bridge.Tests.Signaling;

/// <summary>
/// Integration-style tests that reproduce the multi-signaling scenarios described
/// in the user prompt. They drive the real <see cref="SignalingToolFactory"/>
/// handlers against a real <see cref="SignalRegistry"/> and a simulated external
/// caller loop, asserting that every signal is delivered exactly once and in order.
/// </summary>
public class MultiSignalingScenarioTests : IDisposable
{
    private const string SessionId = "session-1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _promptDir;
    private readonly SignalRegistry _registry = new();

    public MultiSignalingScenarioTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "praetorium-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_promptDir, recursive: true); } catch { /* best-effort */ }
    }

    private static JsonElement JsonObject(string raw) => JsonDocument.Parse(raw).RootElement;

    private SignalingToolDefinition BuildRespond()
    {
        var entry = new SignalingToolEntry
        {
            Name = SignalingToolFactory.RespondToolName,
            Description = "respond",
            IsBlocking = false,
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["message"] = new() { Type = "string", Required = true }
            }
        };
        var tools = SignalingToolFactory.CreateSignalingTools(
            SessionId,
            new SignalingConfiguration { Tools = { entry } },
            _registry,
            _promptDir);
        return tools[0];
    }

    private SignalingToolDefinition BuildRequestInput()
    {
        var entry = new SignalingToolEntry
        {
            Name = SignalingToolFactory.RequestInputToolName,
            Description = "request_input",
            IsBlocking = true,
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["question"] = new() { Type = "string", Required = true }
            }
        };
        var tools = SignalingToolFactory.CreateSignalingTools(
            SessionId,
            new SignalingConfiguration { Tools = { entry } },
            _registry,
            _promptDir);
        return tools[0];
    }

    /// <summary>
    /// Simulates an external caller polling for a signal. Mirrors what
    /// ToolDispatcher does: wait for a signal, then translate it to a ToolResponse.
    /// </summary>
    private async Task<ToolResponse> ExternalPollAsync(CancellationToken ct = default)
    {
        var signal = await _registry.WaitOutboundAsync(SessionId, Timeout, ct);
        return signal.Type switch
        {
            SignalType.Input when signal.Data is ToolResponse r => r,
            SignalType.Input => ToolResponse.Complete(signal.Data?.ToString()),
            SignalType.Timeout => ToolResponse.Error("timeout"),
            SignalType.Disconnect => ToolResponse.Error("disconnect"),
            SignalType.Reset => ToolResponse.Error("reset"),
            _ => ToolResponse.Error("unknown")
        };
    }

    // ---------------------------------------------------------------------
    //  Scenario 1 (user prompt) — non-blocking multi-signal:
    //    External call        → starts waiting
    //    Agent startup        → signals msg-1
    //    External gets msg-1
    //    Agent signals msg-2  (no waiter)
    //    Agent signals msg-3  (no waiter)
    //    External gets msg-2
    //    External gets msg-3
    // ---------------------------------------------------------------------
    [Fact]
    public async Task NonBlocking_MultipleSignals_NoDataLoss_InOrder()
    {
        var respond = BuildRespond();

        // External call arrives first and begins polling.
        var poll1 = ExternalPollAsync();

        // Agent emits first response while external is waiting.
        var send1 = respond.Handler(JsonObject("""{"message":"msg-1"}"""), null, CancellationToken.None);
        var r1 = await poll1;

        // Agent emits two more while NO external caller is waiting.
        var send2 = respond.Handler(JsonObject("""{"message":"msg-2"}"""), null, CancellationToken.None);
        var send3 = respond.Handler(JsonObject("""{"message":"msg-3"}"""), null, CancellationToken.None);

        await Task.WhenAll(send1, send2, send3);

        // External now comes back twice in a row.
        var r2 = await ExternalPollAsync();
        var r3 = await ExternalPollAsync();

        Assert.Equal("msg-1", r1.Message);
        Assert.Equal("msg-2", r2.Message);
        Assert.Equal("msg-3", r3.Message);
    }

    // ---------------------------------------------------------------------
    //  Scenario 2 — non-blocking signals emitted before any external call.
    //  All three must still arrive, in order, once the external caller polls.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task NonBlocking_SignalsBeforePoll_AllDeliveredInOrder()
    {
        var respond = BuildRespond();

        await respond.Handler(JsonObject("""{"message":"a"}"""), null, CancellationToken.None);
        await respond.Handler(JsonObject("""{"message":"b"}"""), null, CancellationToken.None);
        await respond.Handler(JsonObject("""{"message":"c"}"""), null, CancellationToken.None);

        var r1 = await ExternalPollAsync();
        var r2 = await ExternalPollAsync();
        var r3 = await ExternalPollAsync();

        Assert.Equal("a", r1.Message);
        Assert.Equal("b", r2.Message);
        Assert.Equal("c", r3.Message);
    }

    // ---------------------------------------------------------------------
    //  Scenario 3 — blocking request_input round-trip.
    //  External polls, agent asks question (blocks), external replies via _input,
    //  agent resumes with the answer. Nothing dropped.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Blocking_RequestInput_DeliversQuestion_AndReceivesAnswer()
    {
        var requestInput = BuildRequestInput();

        // External begins polling.
        var externalWait = ExternalPollAsync();

        // Agent calls blocking request_input — dispatches outgoing question
        // and then blocks waiting for the caller's reply.
        var agentSide = requestInput.Handler(
            JsonObject("""{"question":"Which option?","options":["a","b"]}"""),
            null,
            CancellationToken.None);

        var externalResponse = await externalWait;
        Assert.Equal("input_requested", externalResponse.Status);
        Assert.Equal("Which option?", externalResponse.Question);

        // Agent is still blocked — simulate external providing input (inbound channel).
        _registry.SignalInbound(SessionId, SignalResult.Input("a"));

        var agentAnswer = await agentSide;
        Assert.Contains("a", agentAnswer);
    }

    // ---------------------------------------------------------------------
    //  Scenario 4 — mixed blocking + non-blocking with the agent running ahead
    //  of the external caller. Outbound and inbound channels are independent,
    //  so the blocking request_input never re-consumes its own outgoing signal
    //  even when no caller is polling at dispatch time.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Mixed_BlockingAndNonBlocking_AgentAhead_NoDataLoss()
    {
        var respond = BuildRespond();
        var requestInput = BuildRequestInput();

        // Agent produces step-1 with no caller waiting yet.
        await respond.Handler(JsonObject("""{"message":"step-1"}"""), null, CancellationToken.None);

        // Agent immediately issues a blocking request_input with still no caller.
        // The question must be queued on the outbound channel; the tool must wait
        // only on the inbound channel.
        var blockingCall = requestInput.Handler(
            JsonObject("""{"question":"continue?"}"""),
            null,
            CancellationToken.None);

        // External caller arrives late and drains signals in order.
        var r1 = await ExternalPollAsync();
        Assert.Equal("step-1", r1.Message);

        var r2 = await ExternalPollAsync();
        Assert.Equal("input_requested", r2.Status);
        Assert.Equal("continue?", r2.Question);

        // External replies on the inbound channel.
        _registry.SignalInbound(SessionId, SignalResult.Input("yes"));
        var agentAnswer = await blockingCall;
        Assert.Contains("yes", agentAnswer);

        // Agent continues with another non-blocking respond.
        await respond.Handler(JsonObject("""{"message":"step-2"}"""), null, CancellationToken.None);
        var r3 = await ExternalPollAsync();
        Assert.Equal("step-2", r3.Message);
    }

    // ---------------------------------------------------------------------
    //  Scenario 5 — stress: agent emits 50 non-blocking signals while
    //  external caller polls at its own pace. Every message arrives in order.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task NonBlocking_HighVolume_NoLossNoReorder()
    {
        const int count = 50;
        var respond = BuildRespond();

        var agent = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                await respond.Handler(JsonObject($$"""{"message":"m-{{i}}"}"""), null, CancellationToken.None);
            }
        });

        var received = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var r = await ExternalPollAsync();
            Assert.NotNull(r.Message);
            received.Add(r.Message!);
        }
        await agent;

        for (int i = 0; i < count; i++)
        {
            Assert.Equal($"m-{i}", received[i]);
        }
    }

    // ---------------------------------------------------------------------
    //  Scenario 6 — disconnect while queued signals are pending.
    //  Disconnect must surface to the external caller even if input signals
    //  are already queued ahead of it (FIFO).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Disconnect_AfterQueuedSignals_DeliveredInOrder()
    {
        var respond = BuildRespond();
        _registry.RegisterConnectionBinding(SessionId, "conn-1");

        await respond.Handler(JsonObject("""{"message":"pre-disconnect"}"""), null, CancellationToken.None);
        _registry.SignalDisconnect("conn-1");

        var r1 = await ExternalPollAsync();
        var r2 = await ExternalPollAsync();

        Assert.Equal("pre-disconnect", r1.Message);
        Assert.Equal("error", r2.Status);
        Assert.Equal("disconnect", r2.ErrorMessage);
    }
}
