using System.Diagnostics;

namespace Burger.Core.Simulation;

/// <summary>
/// Runs the game clock on a thread of its own, so the frame rate does not depend on how long a tick takes (on any machine with more than one core).
/// The world belongs to whoever holds <see cref="Enter"/>: this thread holds it for one tick at a time, the interface asks for it only for the parts of a frame that read the game.
/// The interface wins the race: after every tick this thread waits while the interface is asking.
/// </summary>
public sealed class SimulationThread : IDisposable
{
    private readonly Game _game;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private volatile bool _stop;
    private int _waiting;
    private const double PublishSeconds = 1.0 / 30;   // how often what the renderer shows is refreshed while the clock runs

    /// <summary>While true no tick starts (a background build is reading the world).</summary>
    public volatile bool Hold;
    /// <summary>Called on this thread with the world held, after ticks, at most 30 times a second.</summary>
    public Action? Publish;
    /// <summary>How even the clock is since this was last read: the longest tick in milliseconds and the ticks over 33 ms.</summary>
    public (double LongestMs, int Over33) TakeTickStats()
    {
        var stats = (Volatile.Read(ref _longestTickMs), Volatile.Read(ref _ticksOver33));
        Volatile.Write(ref _longestTickMs, 0);
        Volatile.Write(ref _ticksOver33, 0);
        return stats;
    }
    private double _longestTickMs;
    private int _ticksOver33;

    /// <summary>What stopped the clock, if a tick threw (the clock is paused then). Read and clear it on the interface's side.</summary>
    public volatile Exception? Failure;
    /// <summary>Called with the world held right after <see cref="Failure"/> is set.</summary>

    public SimulationThread(Game game)
    {
        _game = game;
        _thread = new Thread(Run, 16 * 1024 * 1024)
        {
            IsBackground = true,
            Name = "Simulation",
            // on a machine with few cores the picture comes first; the clock takes what is left
            Priority = Environment.ProcessorCount <= 4 ? ThreadPriority.BelowNormal : ThreadPriority.Normal,
        };
        _thread.Start();
    }

    /// <summary>Asks for the world. Timeout in milliseconds (-1: wait for the tick running now to end). Returns false if it was not free in time; on true call <see cref="Exit"/>.</summary>
    public bool Enter(int timeoutMs)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            bool taken = false;
            if (timeoutMs < 0) Monitor.Enter(_gate, ref taken);
            else Monitor.TryEnter(_gate, timeoutMs, ref taken);
            return taken;
        }
        finally { Interlocked.Decrement(ref _waiting); }
    }

    public void Exit() => Monitor.Exit(_gate);

    /// <summary>The clock was told to run again: do not wait for the next look at it.</summary>
    public void Nudge() => _wake.Set();

    private void GiveWay()
    {
        var spin = new SpinWait();
        while (Volatile.Read(ref _waiting) > 0 && !_stop) spin.SpinOnce();
    }

    private void Run()
    {
        var clock = _game.Clock;
        long last = Stopwatch.GetTimestamp(), lastPublish = last;
        bool unpublished = false;
        double Seconds(long from, long to) => (to - from) / (double)Stopwatch.Frequency;
        while (!_stop)
        {
            long now = Stopwatch.GetTimestamp();
            double dt = Seconds(last, now);
            last = now;
            if (Hold || clock.Paused || !_game.Started || _game.Development.CatchingUp)
            {
                if (unpublished && !_stop) { GiveWay(); lock (_gate) Publish?.Invoke(); unpublished = false; }
                _wake.Wait(5);
                _wake.Reset();
                last = Stopwatch.GetTimestamp();
                continue;
            }
            clock.AddTime(Math.Min(dt, 0.25));
            while (!_stop && !Hold && !clock.Paused && clock.TickDue)
            {
                GiveWay();
                lock (_gate)
                {
                    if (clock.Paused || Hold) break;
                    long tickStart = Stopwatch.GetTimestamp();
                    try { clock.RunTick(); }
                    catch (Exception ex)
                    {
                        clock.Paused = true;
                        Failure = ex;
                        break;
                    }
                    unpublished = true;
                    now = Stopwatch.GetTimestamp();
                    double tickMs = Seconds(tickStart, now) * 1000;
                    if (tickMs > _longestTickMs) Volatile.Write(ref _longestTickMs, tickMs);
                    if (tickMs > 33) Interlocked.Increment(ref _ticksOver33);
                    if (Seconds(lastPublish, now) >= PublishSeconds)
                    {
                        Publish?.Invoke();
                        unpublished = false;
                        lastPublish = Stopwatch.GetTimestamp();
                    }
                }
                now = Stopwatch.GetTimestamp();
                clock.AddTime(Math.Min(Seconds(last, now), 0.25));
                last = now;
            }
            if (_stop) break;
            if (unpublished && Seconds(lastPublish, Stopwatch.GetTimestamp()) >= PublishSeconds)
            {
                GiveWay();
                lock (_gate) Publish?.Invoke();
                unpublished = false;
                lastPublish = Stopwatch.GetTimestamp();
            }
            if (!clock.TickDue) Thread.Sleep(1);
        }
    }

    /// <summary>Stops the clock's thread and waits for it (the tick it is in finishes).</summary>
    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join();
        _wake.Dispose();
    }
}
