using System.Diagnostics;
using System.Threading.Channels;

namespace Burger.Core.Simulation;

/// <summary>
/// Optional single-owner simulation loop. Rendering can consume <see cref="Snapshots"/> without touching Game.
/// Multiplayer, save/load, and UI command integration must use this boundary rather than mutating Game directly.
/// </summary>
public sealed class SimulationWorker : IAsyncDisposable
{
    private enum CommandKind { Pause, Speed, Stop }
    private readonly record struct Command(CommandKind Kind, int Value);

    private readonly Game _game;
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;
    private long _tick;

    public RenderSnapshotStore Snapshots { get; } = new();
    public SimulationWorker(Game game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        Snapshots.Publish(RenderSnapshotBuilder.Build(game, 0, game.Clock.HourFraction));
    }

    public void Start()
    {
        if (_run != null) throw new InvalidOperationException("Simulation worker already started.");
        _run = Task.Run(RunAsync);
    }

    public ValueTask SetPaused(bool paused) => _commands.Writer.WriteAsync(new(CommandKind.Pause, paused ? 1 : 0));
    public ValueTask SetSpeed(int speed) => _commands.Writer.WriteAsync(new(CommandKind.Speed, Math.Clamp(speed, 1, 5)));

    public async ValueTask StopAsync()
    {
        if (_run == null) return;
        await _commands.Writer.WriteAsync(new(CommandKind.Stop, 0));
        await _run.ConfigureAwait(false);
        _run = null;
    }

    private async Task RunAsync()
    {
        var clock = Stopwatch.StartNew();
        long last = clock.ElapsedTicks;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                while (_commands.Reader.TryRead(out var command))
                {
                    switch (command.Kind)
                    {
                        case CommandKind.Pause: _game.Clock.Paused = command.Value != 0; break;
                        case CommandKind.Speed: _game.Clock.Speed = command.Value; break;
                        case CommandKind.Stop: return;
                    }
                }

                long now = clock.ElapsedTicks;
                double dt = Math.Min((now - last) / (double)Stopwatch.Frequency, 0.1);
                last = now;
                int ticks = _game.Clock.Update(dt);
                if (ticks > 0)
                    Snapshots.Publish(RenderSnapshotBuilder.Build(_game, Interlocked.Increment(ref _tick), _game.Clock.HourFraction));
                await Task.Delay(1, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_run != null) await _run.ConfigureAwait(false);
        _stop.Dispose();
    }
}
