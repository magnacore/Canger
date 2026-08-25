// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Signals;

/// <summary>
/// A priority-ordered publish/subscribe bus, the backbone for loose coupling between the model,
/// the UI and plugins.
/// </summary>
/// <remarks>
/// <para>
/// This is the C# counterpart of ranger's <c>SignalDispatcher</c>
/// (<c>ranger/ext/signals.py:112</c>). Two of its behaviours are load-bearing and are preserved:
/// handlers run in descending priority order, and a handler may mutate the signal so that later
/// handlers observe the change.
/// </para>
/// <para>
/// Ranger's weak-binding support is deliberately not reproduced. It exists there only so the
/// garbage collector can reclaim <c>Directory</c> objects that subscribed to settings changes,
/// and its own documentation warns it misbehaves on PyPy. Canger instead returns an
/// <see cref="IDisposable"/> from <see cref="Subscribe"/>, so ownership of a subscription is
/// explicit and a disposed directory unsubscribes deterministically.
/// </para>
/// </remarks>
public sealed class SignalBus
{
    private readonly Dictionary<string, List<Subscription>> _subscriptions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>
    /// Registers a handler for a signal name.
    /// </summary>
    /// <param name="name">The signal name, for example <c>setopt.sort</c>.</param>
    /// <param name="handler">The handler. It may mutate the signal for later handlers.</param>
    /// <param name="priority">
    /// Handlers run from highest priority to lowest. See <see cref="SignalPriority"/> for the
    /// conventional values. Handlers of equal priority run in registration order.
    /// </param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public IDisposable Subscribe(string name, Action<Signal> handler,
                                 double priority = SignalPriority.Normal)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);

        Subscription subscription = new(this, name, handler, priority);

        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(name, out List<Subscription>? handlers))
            {
                handlers = [];
                _subscriptions[name] = handlers;
            }

            // Insert so that the list stays sorted by descending priority while keeping
            // registration order within one priority. A linear insert is right here: handler
            // lists are short and subscribing is far rarer than emitting.
            int index = handlers.FindIndex(existing => existing.Priority < priority);
            handlers.Insert(index < 0 ? handlers.Count : index, subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Publishes a signal to every handler registered for its name.
    /// </summary>
    /// <param name="signal">The signal, which handlers may mutate as it travels.</param>
    /// <returns>
    /// <see langword="true"/> when every handler ran, <see langword="false"/> when one called
    /// <see cref="Signal.Stop"/>.
    /// </returns>
    public bool Emit(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        Subscription[] snapshot;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(signal.Name, out List<Subscription>? handlers))
            {
                return true;
            }

            // Snapshot so a handler may subscribe or unsubscribe while the signal is in flight.
            snapshot = [.. handlers];
        }

        foreach (Subscription subscription in snapshot)
        {
            if (signal.IsStopped)
            {
                return false;
            }

            if (subscription.IsActive)
            {
                subscription.Handler(signal);
            }
        }

        return !signal.IsStopped;
    }

    /// <summary>
    /// Publishes one signal to the handlers of several names at once, as a single
    /// priority-ordered sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because a setting change is announced under two names — <c>setopt</c> for
    /// handlers watching any setting, and <c>setopt.&lt;name&gt;</c> for handlers watching one —
    /// yet both sets of handlers take part in a single pipeline. Emitting the names one after
    /// the other would run every handler of the first name, including the commit, before any
    /// handler of the second name got to sanitise anything.
    /// </para>
    /// <para>
    /// Merging instead means priority is what orders handlers, not which name they happened to
    /// subscribe to. Handlers of equal priority run in name order, then registration order.
    /// </para>
    /// </remarks>
    /// <param name="signal">The signal, which handlers may mutate as it travels.</param>
    /// <param name="names">The names whose handlers should receive it.</param>
    /// <returns>
    /// <see langword="true"/> when every handler ran, <see langword="false"/> when one called
    /// <see cref="Signal.Stop"/>.
    /// </returns>
    public bool EmitTo(Signal signal, params IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(names);

        List<Subscription> merged = [];
        lock (_gate)
        {
            foreach (string name in names)
            {
                if (_subscriptions.TryGetValue(name, out List<Subscription>? handlers))
                {
                    merged.AddRange(handlers);
                }
            }
        }

        // OrderByDescending is a stable sort, so handlers of equal priority keep the order they
        // were gathered in: by name, then by registration.
        foreach (Subscription subscription in merged.OrderByDescending(s => s.Priority))
        {
            if (signal.IsStopped)
            {
                return false;
            }

            if (subscription.IsActive)
            {
                subscription.Handler(signal);
            }
        }

        return !signal.IsStopped;
    }

    /// <summary>Whether any handler is currently registered for a name. Intended for tests.</summary>
    /// <param name="name">The signal name.</param>
    /// <returns><see langword="true"/> when at least one handler is registered.</returns>
    public bool HasSubscribers(string name)
    {
        lock (_gate)
        {
            return _subscriptions.TryGetValue(name, out List<Subscription>? handlers)
                   && handlers.Count > 0;
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(subscription.Name, out List<Subscription>? handlers))
            {
                handlers.Remove(subscription);
            }
        }
    }

    /// <summary>One registration, which unsubscribes itself when disposed.</summary>
    private sealed class Subscription(SignalBus bus, string name, Action<Signal> handler, double priority)
        : IDisposable
    {
        internal string Name { get; } = name;

        internal Action<Signal> Handler { get; } = handler;

        internal double Priority { get; } = priority;

        internal bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            if (!IsActive)
            {
                return;
            }

            // Deactivating before removal means a signal already in flight skips this handler
            // rather than calling into a disposed owner.
            IsActive = false;
            bus.Unsubscribe(this);
        }
    }
}
