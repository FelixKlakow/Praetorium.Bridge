using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Signaling;
using Xunit;

namespace Praetorium.Bridge.Tests.Signaling;

/// <summary>
/// Unit tests for <see cref="SignalRegistry"/>. Each session owns two independent
/// FIFO channels: outbound (agent → external caller) and inbound (external caller →
/// agent). These tests verify correctness and independence of both channels,
/// including queueing before waiter registration, FIFO order, isolation between
/// sessions, disconnect fan-out, and high-volume / concurrent safety.
/// </summary>
public class SignalRegistryTests
{
    private const string SessionA = "session-a";
    private const string SessionB = "session-b";
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(5);

    // ─── Outbound channel ────────────────────────────────────────────────

    [Fact]
    public async Task Outbound_DeliversSignal_WhenSignaledAfterWaiterRegistered()
    {
        var registry = new SignalRegistry();
        var payload = new object();

        var waitTask = registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);
        registry.SignalOutbound(SessionA, SignalResult.Input(payload));

        var result = await waitTask;
        Assert.Equal(SignalType.Input, result.Type);
        Assert.Same(payload, result.Data);
    }

    [Fact]
    public async Task Outbound_TimesOut_WhenNoSignalReceived()
    {
        var registry = new SignalRegistry();
        var result = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        Assert.Equal(SignalType.Timeout, result.Type);
    }

    [Fact]
    public async Task Outbound_ReturnedFromQueue_WhenSignaledBeforeWait()
    {
        var registry = new SignalRegistry();
        registry.SignalOutbound(SessionA, SignalResult.Input("first"));

        var result = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        Assert.Equal(SignalType.Input, result.Type);
        Assert.Equal("first", result.Data);
    }

    [Fact]
    public async Task Outbound_MultipleSignals_QueuedBeforeWait_DeliveredFifo()
    {
        var registry = new SignalRegistry();
        registry.SignalOutbound(SessionA, SignalResult.Input("one"));
        registry.SignalOutbound(SessionA, SignalResult.Input("two"));
        registry.SignalOutbound(SessionA, SignalResult.Input("three"));

        var r1 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var r2 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var r3 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var r4 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);

        Assert.Equal("one", r1.Data);
        Assert.Equal("two", r2.Data);
        Assert.Equal("three", r3.Data);
        Assert.Equal(SignalType.Timeout, r4.Type);
    }

    [Fact]
    public async Task Outbound_InterleavedSignals_NoDataLoss_WhenProducerFasterThanConsumer()
    {
        var registry = new SignalRegistry();

        var wait1 = registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);
        registry.SignalOutbound(SessionA, SignalResult.Input("msg-1"));
        var r1 = await wait1;

        registry.SignalOutbound(SessionA, SignalResult.Input("msg-2"));
        registry.SignalOutbound(SessionA, SignalResult.Input("msg-3"));

        var r2 = await registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
        var r3 = await registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);

        Assert.Equal("msg-1", r1.Data);
        Assert.Equal("msg-2", r2.Data);
        Assert.Equal("msg-3", r3.Data);
    }

    // ─── Inbound channel ─────────────────────────────────────────────────

    [Fact]
    public async Task Inbound_DeliversSignal_WhenSignaledAfterWaiterRegistered()
    {
        var registry = new SignalRegistry();
        var waitTask = registry.WaitInboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);
        registry.SignalInbound(SessionA, SignalResult.Input("reply"));

        var result = await waitTask;
        Assert.Equal("reply", result.Data);
    }

    [Fact]
    public async Task Inbound_QueuedBeforeWait_DeliveredFifo()
    {
        var registry = new SignalRegistry();
        registry.SignalInbound(SessionA, SignalResult.Input("r1"));
        registry.SignalInbound(SessionA, SignalResult.Input("r2"));

        var a = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var b = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);

        Assert.Equal("r1", a.Data);
        Assert.Equal("r2", b.Data);
    }

    // ─── Channel independence ────────────────────────────────────────────

    [Fact]
    public async Task Channels_AreIndependent_OutboundSignalDoesNotSatisfyInboundWaiter()
    {
        var registry = new SignalRegistry();

        var inboundWait = registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        registry.SignalOutbound(SessionA, SignalResult.Input("out-only"));

        var result = await inboundWait;
        Assert.Equal(SignalType.Timeout, result.Type);

        // The outbound signal must still be queued and deliverable.
        var outbound = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        Assert.Equal("out-only", outbound.Data);
    }

    [Fact]
    public async Task Channels_AreIndependent_InboundSignalDoesNotSatisfyOutboundWaiter()
    {
        var registry = new SignalRegistry();

        var outboundWait = registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        registry.SignalInbound(SessionA, SignalResult.Input("in-only"));

        var result = await outboundWait;
        Assert.Equal(SignalType.Timeout, result.Type);

        var inbound = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        Assert.Equal("in-only", inbound.Data);
    }

    [Fact]
    public async Task Channels_HaveIndependentQueues()
    {
        var registry = new SignalRegistry();

        registry.SignalOutbound(SessionA, SignalResult.Input("out-1"));
        registry.SignalInbound(SessionA, SignalResult.Input("in-1"));
        registry.SignalOutbound(SessionA, SignalResult.Input("out-2"));
        registry.SignalInbound(SessionA, SignalResult.Input("in-2"));

        var out1 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var out2 = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var in1 = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var in2 = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);

        Assert.Equal("out-1", out1.Data);
        Assert.Equal("out-2", out2.Data);
        Assert.Equal("in-1", in1.Data);
        Assert.Equal("in-2", in2.Data);
    }

    // ─── Concurrency / waiter rules ──────────────────────────────────────

    [Fact]
    public async Task SecondConcurrentOutboundWaiter_Throws_InvalidOperationException()
    {
        var registry = new SignalRegistry();
        var first = registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None));

        registry.SignalOutbound(SessionA, SignalResult.Input("done"));
        await first;
    }

    [Fact]
    public async Task DifferentSessions_AreIsolated()
    {
        var registry = new SignalRegistry();
        registry.SignalOutbound(SessionA, SignalResult.Input("for-a"));
        registry.SignalOutbound(SessionB, SignalResult.Input("for-b"));

        var ra = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var rb = await registry.WaitOutboundAsync(SessionB, ShortTimeout, CancellationToken.None);

        Assert.Equal("for-a", ra.Data);
        Assert.Equal("for-b", rb.Data);
    }

    [Fact]
    public async Task Cancellation_ReturnsTimeoutSignal()
    {
        var registry = new SignalRegistry();
        using var cts = new CancellationTokenSource();

        var waitTask = registry.WaitOutboundAsync(SessionA, LongTimeout, cts.Token);
        cts.Cancel();

        var result = await waitTask;
        Assert.Equal(SignalType.Timeout, result.Type);
    }

    // ─── Disconnect ──────────────────────────────────────────────────────

    [Fact]
    public async Task SignalDisconnect_UnblocksOutboundWaiter()
    {
        var registry = new SignalRegistry();
        registry.RegisterConnectionBinding(SessionA, "conn-1");

        var waitTask = registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);
        registry.SignalDisconnect("conn-1");

        var result = await waitTask;
        Assert.Equal(SignalType.Disconnect, result.Type);
    }

    [Fact]
    public async Task SignalDisconnect_UnblocksInboundWaiter()
    {
        var registry = new SignalRegistry();
        registry.RegisterConnectionBinding(SessionA, "conn-1");

        var waitTask = registry.WaitInboundAsync(SessionA, LongTimeout, CancellationToken.None);
        await Task.Delay(10);
        registry.SignalDisconnect("conn-1");

        var result = await waitTask;
        Assert.Equal(SignalType.Disconnect, result.Type);
    }

    [Fact]
    public async Task SignalDisconnect_WithNoWaiter_QueuesOnBothChannels()
    {
        var registry = new SignalRegistry();
        registry.RegisterConnectionBinding(SessionA, "conn-1");
        registry.SignalDisconnect("conn-1");

        var outbound = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var inbound = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);

        Assert.Equal(SignalType.Disconnect, outbound.Type);
        Assert.Equal(SignalType.Disconnect, inbound.Type);
    }

    // ─── Removal / validation ────────────────────────────────────────────

    [Fact]
    public async Task RemoveSession_DrainsBothQueues()
    {
        var registry = new SignalRegistry();
        registry.SignalOutbound(SessionA, SignalResult.Input("lost-out"));
        registry.SignalInbound(SessionA, SignalResult.Input("lost-in"));
        registry.RemoveSession(SessionA);

        var outbound = await registry.WaitOutboundAsync(SessionA, ShortTimeout, CancellationToken.None);
        var inbound = await registry.WaitInboundAsync(SessionA, ShortTimeout, CancellationToken.None);

        Assert.Equal(SignalType.Timeout, outbound.Type);
        Assert.Equal(SignalType.Timeout, inbound.Type);
    }

    [Fact]
    public void SignalOutbound_WithNullResult_Throws()
    {
        var registry = new SignalRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.SignalOutbound(SessionA, null!));
    }

    [Fact]
    public void SignalInbound_WithNullResult_Throws()
    {
        var registry = new SignalRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.SignalInbound(SessionA, null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WaitOutbound_WithInvalidSessionId_Throws(string? sessionId)
    {
        var registry = new SignalRegistry();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.WaitOutboundAsync(sessionId!, LongTimeout, CancellationToken.None));
    }

    // ─── Stress ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HighVolumeOutbound_NoLoss_NoReorder()
    {
        const int count = 500;
        var registry = new SignalRegistry();

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
                registry.SignalOutbound(SessionA, SignalResult.Input(i));
        });

        var received = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            var r = await registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
            Assert.Equal(SignalType.Input, r.Type);
            received.Add((int)r.Data!);
        }

        await producer;

        Assert.Equal(count, received.Count);
        for (int i = 0; i < count; i++)
            Assert.Equal(i, received[i]);
    }

    [Fact]
    public async Task ConcurrentProducerAndConsumer_InterleavedWaits_NoLoss()
    {
        const int count = 200;
        var registry = new SignalRegistry();

        var received = new List<int>(count);
        var consumer = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                var r = await registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
                received.Add((int)r.Data!);
            }
        });

        var producer = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                registry.SignalOutbound(SessionA, SignalResult.Input(i));
                if (i % 7 == 0)
                    await Task.Yield();
            }
        });

        await Task.WhenAll(producer, consumer);

        Assert.Equal(count, received.Count);
        for (int i = 0; i < count; i++)
            Assert.Equal(i, received[i]);
    }

    [Fact]
    public async Task ConcurrentBidirectional_NoLoss_OnEitherChannel()
    {
        const int count = 200;
        var registry = new SignalRegistry();

        var outReceived = new List<int>(count);
        var inReceived = new List<int>(count);

        var outConsumer = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                var r = await registry.WaitOutboundAsync(SessionA, LongTimeout, CancellationToken.None);
                outReceived.Add((int)r.Data!);
            }
        });
        var inConsumer = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                var r = await registry.WaitInboundAsync(SessionA, LongTimeout, CancellationToken.None);
                inReceived.Add((int)r.Data!);
            }
        });

        var outProducer = Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
                registry.SignalOutbound(SessionA, SignalResult.Input(i));
        });
        var inProducer = Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
                registry.SignalInbound(SessionA, SignalResult.Input(i));
        });

        await Task.WhenAll(outProducer, inProducer, outConsumer, inConsumer);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, outReceived[i]);
            Assert.Equal(i, inReceived[i]);
        }
    }
}
