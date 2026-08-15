using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Publishes typed domain events synchronously and independently from the Godot scene tree.
/// </summary>
public interface IDomainEventBus
{
    /// <summary>Subscribes a handler to one typed domain event.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent;

    /// <summary>Publishes one typed domain event to a stable snapshot of current subscribers.</summary>
    void Publish<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent;

    /// <summary>Gets the number of currently active typed subscriptions.</summary>
    int SubscriptionCount { get; }
}

/// <summary>
/// Thread-safe in-process implementation used by single-player domain/application services.
/// </summary>
public sealed class DomainEventBus : IDomainEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public int SubscriptionCount
    {
        get
        {
            lock (_gate)
            {
                return _handlers.Values.Sum(list => list.Count);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Type eventType = typeof(TEvent);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventType, out List<Delegate>? handlers))
            {
                handlers = new List<Delegate>();
                _handlers[eventType] = handlers;
            }
            handlers.Add(handler);
        }
        return new Subscription<TEvent>(this, handler);
    }

    public void Publish<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        Delegate[] snapshot;
        lock (_gate)
        {
            snapshot = _handlers.TryGetValue(typeof(TEvent), out List<Delegate>? handlers)
                ? handlers.ToArray()
                : Array.Empty<Delegate>();
        }

        foreach (Delegate candidate in snapshot)
        {
            ((Action<TEvent>)candidate)(domainEvent);
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate>? handlers))
            {
                return;
            }
            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                _handlers.Remove(typeof(TEvent));
            }
        }
    }

    private sealed class Subscription<TEvent> : IDisposable
        where TEvent : IDomainEvent
    {
        private DomainEventBus? _owner;
        private readonly Action<TEvent> _handler;

        public Subscription(DomainEventBus owner, Action<TEvent> handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Dispose()
        {
            DomainEventBus? owner = _owner;
            if (owner is null)
            {
                return;
            }
            _owner = null;
            owner.Unsubscribe(_handler);
        }
    }
}
