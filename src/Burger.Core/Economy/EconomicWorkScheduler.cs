using System.Collections.Generic;

namespace Burger.Core.Economy;

/// <summary>
/// Thread-safe storage for economic work items. Producers may enqueue from worker threads,
/// but the simulation thread owns execution: only the caller of <see cref="Drain"/> runs callbacks.
/// </summary>
public sealed class EconomicWorkScheduler<T>
{
    private readonly object _gate = new();
    private PriorityQueue<T, WorkOrder> _pending = new();
    private PriorityQueue<T, WorkOrder> _draining = new();
    private long _nextSequence;
    private int _drainActive;

    /// <summary>Gets the number of queued items, including items waiting in a bounded drain.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _pending.Count + _draining.Count;
        }
    }

    /// <summary>Returns queued work in the order it will be drained, without executing it.</summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            var result = new List<T>(_pending.Count + _draining.Count);
            AppendOrdered(_draining, result);
            AppendOrdered(_pending, result);
            return result;
        }
    }

    /// <summary>
    /// Replaces queued work after loading a save. Items are re-keyed deterministically and remain
    /// pending until the simulation thread drains them.
    /// </summary>
    public void Restore(IEnumerable<T> items, Func<T, int> orderKey)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(orderKey);
        if (Volatile.Read(ref _drainActive) != 0)
            throw new InvalidOperationException("Cannot restore economic work while it is being drained.");

        lock (_gate)
        {
            if (_drainActive != 0)
                throw new InvalidOperationException("Cannot restore economic work while it is being drained.");
            _pending.Clear();
            _draining.Clear();
            _nextSequence = 0;
            foreach (var item in items)
                _pending.Enqueue(item, new WorkOrder(orderKey(item), _nextSequence++));
        }
    }

    /// <summary>
    /// Stores an item for later execution. Lower order keys run first; equal keys retain enqueue order.
    /// Enqueuing never invokes the item's callback.
    /// </summary>
    public void Enqueue(int orderKey, T item)
    {
        lock (_gate)
        {
            _pending.Enqueue(item, new WorkOrder(orderKey, _nextSequence++));
        }
    }

    /// <summary>
    /// Removes and executes at most <paramref name="maxItems"/> items in stable order.
    /// Call this from the simulation thread. Producers may enqueue while it runs; those items are
    /// retained for a later drain and cannot cause a worker thread to execute the callback.
    /// </summary>
    /// <returns>The number of callbacks executed.</returns>
    public int Drain(int maxItems, Action<T> execute)
    {
        if (maxItems < 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        ArgumentNullException.ThrowIfNull(execute);
        if (Interlocked.CompareExchange(ref _drainActive, 1, 0) != 0)
            throw new InvalidOperationException("Only one simulation thread may drain economic work at a time.");

        PriorityQueue<T, WorkOrder>? batch = null;
        int limit = 0;
        int executed = 0;
        T failedItem = default!;
        WorkOrder failedOrder = default;
        bool hasDequeuedItem = false;
        try
        {
            if (maxItems == 0) return 0;

            lock (_gate)
            {
                if (_pending.Count == 0) return 0;
                batch = _pending;
                _pending = _draining;
                _draining = batch;
                limit = Math.Min(maxItems, batch.Count);
            }

            while (executed < limit)
            {
                T item;
                lock (_gate)
                {
                    if (!batch.TryDequeue(out item!, out var order)) break;
                    failedItem = item;
                    failedOrder = order;
                    hasDequeuedItem = true;
                }

                execute(item);
                hasDequeuedItem = false;
                executed++;
            }

            return executed;
        }
        finally
        {
            if (batch != null)
            {
                lock (_gate)
                {
                    if (hasDequeuedItem)
                        _pending.Enqueue(failedItem, failedOrder);
                    while (batch.TryDequeue(out var item, out var order))
                        _pending.Enqueue(item, order);
                    batch.Clear();
                }
            }

            Volatile.Write(ref _drainActive, 0);
        }
    }

    /// <summary>Removes all queued items without executing callbacks.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _draining.Clear();
        }
    }

    private static void AppendOrdered(PriorityQueue<T, WorkOrder> source, List<T> target)
    {
        var copy = new PriorityQueue<T, WorkOrder>();
        foreach (var entry in source.UnorderedItems)
            copy.Enqueue(entry.Element, entry.Priority);
        while (copy.TryDequeue(out var item, out _))
            target.Add(item);
    }

    private readonly struct WorkOrder : IComparable<WorkOrder>
    {
        private readonly int _orderKey;
        private readonly long _sequence;

        public WorkOrder(int orderKey, long sequence)
        {
            _orderKey = orderKey;
            _sequence = sequence;
        }

        public int CompareTo(WorkOrder other)
        {
            int orderComparison = _orderKey.CompareTo(other._orderKey);
            return orderComparison != 0 ? orderComparison : _sequence.CompareTo(other._sequence);
        }
    }
}
