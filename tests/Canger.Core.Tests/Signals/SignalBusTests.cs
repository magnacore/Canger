// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Signals;

namespace Canger.Core.Tests.Signals;

public class SignalBusTests
{
    /// <summary>A signal carrying a mutable payload, standing in for a real one.</summary>
    private sealed class Probe(string name, int value) : Signal(name)
    {
        public int Value { get; set; } = value;
    }

    [Fact]
    public void Emit_RunsHandlersFromHighestPriorityToLowest()
    {
        SignalBus bus = new();
        List<string> order = [];

        bus.Subscribe("s", _ => order.Add("sync"), SignalPriority.Sync);
        bus.Subscribe("s", _ => order.Add("sanitize"), SignalPriority.Sanitize);
        bus.Subscribe("s", _ => order.Add("after"), SignalPriority.AfterSync);
        bus.Subscribe("s", _ => order.Add("normal"), SignalPriority.Normal);

        bus.Emit(new Probe("s", 0));

        Assert.Equal(["sanitize", "normal", "sync", "after"], order);
    }

    [Fact]
    public void Emit_RunsEqualPrioritiesInRegistrationOrder()
    {
        SignalBus bus = new();
        List<string> order = [];

        bus.Subscribe("s", _ => order.Add("first"));
        bus.Subscribe("s", _ => order.Add("second"));
        bus.Subscribe("s", _ => order.Add("third"));

        bus.Emit(new Probe("s", 0));

        Assert.Equal(["first", "second", "third"], order);
    }

    [Fact]
    public void Emit_LetsAnEarlierHandlerRewriteThePayloadForLaterOnes()
    {
        // This is the behaviour the settings pipeline is built on: sanitise, then commit.
        SignalBus bus = new();
        int observed = -1;

        bus.Subscribe("s", s => ((Probe)s).Value *= 2, SignalPriority.Sanitize);
        bus.Subscribe("s", s => observed = ((Probe)s).Value, SignalPriority.Sync);

        bus.Emit(new Probe("s", 21));

        Assert.Equal(42, observed);
    }

    [Fact]
    public void Emit_StopsAndReportsFailureWhenAHandlerStopsTheSignal()
    {
        SignalBus bus = new();
        bool laterRan = false;

        bus.Subscribe("s", s => s.Stop(), SignalPriority.Sanitize);
        bus.Subscribe("s", _ => laterRan = true, SignalPriority.Sync);

        Assert.False(bus.Emit(new Probe("s", 0)));
        Assert.False(laterRan);
    }

    [Fact]
    public void Emit_IgnoresNamesWithNoSubscribers() =>
        Assert.True(new SignalBus().Emit(new Probe("nobody-listening", 0)));

    [Fact]
    public void Dispose_UnsubscribesAHandler()
    {
        SignalBus bus = new();
        int calls = 0;
        IDisposable subscription = bus.Subscribe("s", _ => calls++);

        bus.Emit(new Probe("s", 0));
        subscription.Dispose();
        bus.Emit(new Probe("s", 0));

        Assert.Equal(1, calls);
        Assert.False(bus.HasSubscribers("s"));
    }

    [Fact]
    public void Subscribe_DuringEmission_DoesNotAffectTheSignalInFlight()
    {
        // Handlers are snapshotted before dispatch, so a handler that subscribes does not
        // receive the signal that caused it to subscribe.
        SignalBus bus = new();
        int lateCalls = 0;

        bus.Subscribe("s", _ => bus.Subscribe("s", _ => lateCalls++, SignalPriority.AfterSync));

        bus.Emit(new Probe("s", 0));
        Assert.Equal(0, lateCalls);

        bus.Emit(new Probe("s", 0));
        Assert.Equal(1, lateCalls);
    }

    [Fact]
    public void Dispose_DuringEmission_SkipsTheDisposedHandler()
    {
        SignalBus bus = new();
        bool laterRan = false;
        IDisposable? later = null;

        bus.Subscribe("s", _ => later!.Dispose(), SignalPriority.Sanitize);
        later = bus.Subscribe("s", _ => laterRan = true, SignalPriority.Sync);

        bus.Emit(new Probe("s", 0));

        Assert.False(laterRan);
    }
}
