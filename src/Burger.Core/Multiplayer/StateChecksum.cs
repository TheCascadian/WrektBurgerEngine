using Burger.Core.Model;

namespace Burger.Core.Multiplayer;

/// <summary>The desync check: a hash of the parts of the state every machine must agree on. Never iterates an unordered collection.</summary>
public static class StateChecksum
{
    /// <summary>Human-readable differences between two games at the same tick (tests and debugging; not sent over the wire).</summary>
    public static List<string> Diff(Game a, Game b)
    {
        var o = new List<string>();
        if (a.World.Date.TotalDays != b.World.Date.TotalDays || a.World.Date.Hour != b.World.Date.Hour) o.Add($"date {a.World.Date.ToDisplay()} {a.World.Date.Hour} vs {b.World.Date.ToDisplay()} {b.World.Date.Hour}");
        foreach (var c in a.World.Countries)
        {
            var x = b.World.GetCountry(c.Tag);
            if (x == null || c.Gold != x.Gold || c.Manpower != x.Manpower || c.IsPlayer != x.IsPlayer || c.Exists != x.Exists)
                o.Add($"{c.Tag}: gold {c.Gold} vs {x?.Gold}, manpower {c.Manpower} vs {x?.Manpower}, player {c.IsPlayer}/{x?.IsPlayer}");
        }
        if (a.World.Divisions.Count != b.World.Divisions.Count) o.Add($"divisions {a.World.Divisions.Count} vs {b.World.Divisions.Count}");
        var bd = b.World.Divisions.ToDictionary(d => d.Id);
        foreach (var d in a.World.Divisions)
            if (!bd.TryGetValue(d.Id, out var y) || y.Location.Key != d.Location.Key || y.Strength != d.Strength) o.Add($"division {d.Id} ({d.Owner.Tag}): {d.Location.Key}/{d.Strength} vs {(y == null ? "missing" : y.Location.Key + "/" + y.Strength)}");
        var rb = b.Rngs.Snapshot();
        foreach (var r in a.Rngs.Snapshot()) if (!rb.Contains(r)) o.Add("rng " + r.Name);
        if (a.Events.PlayerQueue.Count != b.Events.PlayerQueue.Count) o.Add($"event queue {a.Events.PlayerQueue.Count} vs {b.Events.PlayerQueue.Count}");
        if (a.PendingProposals.Count != b.PendingProposals.Count) o.Add($"proposals {a.PendingProposals.Count} vs {b.PendingProposals.Count}");
        return o;
    }

    public static ulong Compute(Game game)
    {
        ulong h = 14695981039346656037UL;
        void U(ulong v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } }
        void S(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 124; h *= 1099511628211UL; }
        void D(double d) => U((ulong)BitConverter.DoubleToInt64Bits(d));
        var w = game.World;
        U((ulong)w.Date.TotalDays); U((ulong)w.Date.Hour);
        foreach (var c in w.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            S(c.Tag); U(c.Exists ? 1UL : 0UL); U(c.IsPlayer ? 1UL : 0UL); D(c.Gold); D(c.Manpower);
        }
        foreach (var d in w.Divisions.OrderBy(d => d.Id))
        {
            U((ulong)d.Id); S(d.Owner.Tag); S(d.Location.Key); D(d.Strength);
        }
        foreach (var (name, st) in game.Rngs.Snapshot()) { S(name); U(st); }
        return h;
    }
}

