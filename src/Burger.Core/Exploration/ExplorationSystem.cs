using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Exploration;

/// <summary>
/// Terra incognita: the lands the player's nation has never seen stay unknown (a blank chart) until its land, armies or ships
/// reach them. The charted set only grows. It is a view of the world for the local player: nothing in the simulation reads it,
/// so it never touches multiplayer sync (like fog of war, it is per machine).
/// </summary>
public sealed class ExplorationSystem
{
    private readonly Game _game;
    private bool[] _seen = Array.Empty<bool>();
    private Country? _for;
    private string? _loadedTag;
    private bool _primed;
    private readonly List<int> _fresh = new();
    private readonly HashSet<int> _scratch = new();
    private int _lastShips = -1;

    /// <summary>Terra incognita is on for this game (chosen when the game starts; turning it off asks for confirmation in the menu).</summary>
    public bool Enabled { get; private set; }
    /// <summary>Changes whenever the charted set changes or the option is switched (the renderer refreshes the map then).</summary>
    public int Version { get; private set; }

    public ExplorationSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "terra_incognita", Write = Save, Read = Load, Reset = Reset });
    }

    /// <summary>The game's files allow the option (defines: terra_incognita = no hides it).</summary>
    public bool Allowed => _game.Data.Defines.GetBool("terra_incognita", true);

    /// <summary>True if the player has been to this location (always true when the option is off or there is no player).</summary>
    public bool IsCharted(Location l)
    {
        if (!Enabled || _game.World.Player == null) return true;
        return l.Index < _seen.Length && _seen[l.Index];
    }

    /// <summary>The charted set belongs to the current player.</summary>
    public bool Ready => _for == _game.World.Player && _seen.Length == _game.World.Locations.Count;

    /// <summary>How many land locations the player has charted, and how many there are.</summary>
    public (int Charted, int Total) LandProgress()
    {
        int c = 0, t = 0;
        foreach (var l in _game.World.Locations)
        {
            if (!l.IsLand) continue;
            t++;
            if (l.Index < _seen.Length && _seen[l.Index]) c++;
        }
        return (c, t);
    }

    public void SetEnabled(bool on)
    {
        if (Enabled == on) return;
        Enabled = on && Allowed;
        if (Enabled && !Ready) _game.UpdateVisibility();
        _fresh.Clear();
        Version++;
    }

    /// <summary>Locations charted since the last call (the renderer plays the reveal for them).</summary>
    public List<int> DrainRevealed()
    {
        var list = new List<int>(_fresh);
        _fresh.Clear();
        return list;
    }

    /// <summary>Daily: everything currently visible, the lands around our own, the horizon of our ships.</summary>
    public void Update(bool[]? visible)
    {
        var player = _game.World.Player;
        int n = _game.World.Locations.Count;
        if (!Enabled || player == null) return;
        if (_for != player || _seen.Length != n)
        {
            bool adopt = _loadedTag != null && _loadedTag == player.Tag && _seen.Length == n;
            _for = player;
            if (!adopt) { _seen = new bool[n]; _primed = false; }
            else _primed = true;
            _loadedTag = null;
        }
        var before = _fresh.Count;
        bool changed = false;
        if (visible != null)
            for (int i = 0; i < n && i < visible.Length; i++)
                if (visible[i]) changed |= Mark(i);
        changed |= Spread(HomeSources(player), _game.Rules.Vision.HomeSight(_game, player));
        changed |= Movers(player);
        if (!_primed)
        {
            // the first look at the world is the start position, not a discovery
            _primed = true;
            _fresh.RemoveRange(before, _fresh.Count - before);
        }
        else Announce(before);
        if (changed) Version++;
    }

    /// <summary>Hourly: only what moves (fleets and divisions), so ships chart the sea as they sail.</summary>
    public void HourlyTick()
    {
        var player = _game.World.Player;
        if (!Enabled || player == null || !Ready) return;
        int before = _fresh.Count;
        if (Movers(player)) { Announce(before); Version++; }
    }

    private bool Movers(Country player)
    {
        bool changed = false;
        var rules = _game.Rules.Vision;
        if (_game.Features.Has(FeatureSet.Navy))
            foreach (var f in _game.Navy.Fleets)
                if (f.Owner == player) changed |= Spread(new[] { f.Location }, rules.FleetSight(_game, f));
        if (_game.Features.Has(FeatureSet.Military))
            foreach (var d in _game.World.Divisions)
                if (d.Owner == player) changed |= Spread(new[] { d.Location }, rules.DivisionSight(_game, d));
        return changed;
    }

    private IEnumerable<Location> HomeSources(Country player)
    {
        foreach (var l in _game.World.Locations)
            if (l.Owner == player || l.Controller == player) yield return l;
    }

    /// <summary>Marks the sources and everything within <paramref name="range"/> steps of them.</summary>
    private bool Spread(IEnumerable<Location> sources, int range)
    {
        bool changed = false;
        var queue = new Queue<(Location L, int R)>();
        _scratch.Clear();
        foreach (var s in sources) if (_scratch.Add(s.Index)) queue.Enqueue((s, range));
        while (queue.Count > 0)
        {
            var (l, r) = queue.Dequeue();
            changed |= Mark(l.Index);
            if (r <= 0) continue;
            foreach (var a in l.Neighbors)
                if (_scratch.Add(a.To.Index)) queue.Enqueue((a.To, r - 1));
        }
        return changed;
    }

    private bool Mark(int index)
    {
        if (_seen[index]) return false;
        _seen[index] = true;
        _fresh.Add(index);
        return true;
    }

    /// <summary>One message per newly charted state, with a click target on the map; at most three, then a count.</summary>
    private void Announce(int from)
    {
        if (_fresh.Count <= from) return;
        var states = new List<(State S, Location L)>();
        var seenStates = new HashSet<State>();
        for (int k = from; k < _fresh.Count; k++)
        {
            var l = _game.World.Locations[_fresh[k]];
            if (!l.IsLand || l.State == null || !seenStates.Add(l.State)) continue;
            states.Add((l.State, l));
        }
        int shown = 0;
        foreach (var (s, l) in states)
        {
            if (shown++ >= 3) break;
            _game.Notify(s.Owner == null ? $"Terra incognita charted: {s.DisplayName}, a land unexplored by your people." : $"Terra incognita charted: {s.DisplayName}, held by {s.Owner.Name}.", NotificationKind.Good, l);
        }
        if (states.Count > 3) _game.Notify($"{states.Count - 3} more lands charted.", NotificationKind.Good);
    }

    private void Save(Node root)
    {
        root.Add("enabled", Enabled ? "1" : "0");
        if (_for == null || _seen.Length == 0) return;
        root.Add("player", _for.Tag);
        var bytes = new byte[(_seen.Length + 7) / 8];
        for (int i = 0; i < _seen.Length; i++) if (_seen[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
        root.Add("seen", Convert.ToBase64String(bytes), true);
    }

    private void Load(Node root)
    {
        Reset();
        Enabled = root.GetString("enabled") == "1" && Allowed;
        _loadedTag = root.GetString("player");
        var text = root.GetString("seen");
        if (string.IsNullOrEmpty(text) || _loadedTag == null) return;
        try
        {
            var bytes = Convert.FromBase64String(text);
            int n = bytes.Length * 8;
            var seen = new bool[_game.World.Locations.Count];
            for (int i = 0; i < seen.Length && i < n; i++) seen[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            _seen = seen;
        }
        catch (FormatException) { _loadedTag = null; }
    }

    private void Reset()
    {
        Enabled = false;
        _seen = Array.Empty<bool>();
        _for = null;
        _loadedTag = null;
        _primed = false;
        _fresh.Clear();
        Version++;
    }
}
