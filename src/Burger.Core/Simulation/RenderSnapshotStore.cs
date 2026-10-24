namespace Burger.Core.Simulation;

/// <summary>Atomic publication of completed render snapshots between simulation and presentation threads.</summary>
public sealed class RenderSnapshotStore
{
    private RenderSnapshot _current = RenderSnapshot.Empty;

    public RenderSnapshot Current => Volatile.Read(ref _current);

    public void Publish(RenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _current, snapshot);
    }
}
