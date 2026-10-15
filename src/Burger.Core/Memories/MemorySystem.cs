using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Politics;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Memories;

/// <summary>What one kind of remembered act is worth: the default weight, how fast it fades (per year, 0 = never) and whether others see it as good.</summary>
public sealed record MemoryKind(string Key, string Label, double Weight, double DecayPerYear, bool Good = false);

/// <summary>
/// Who or what can remember or be remembered: a country, character, party, state or pop group. Identified by a type and a
/// stable key, so it saves and loads without the world being resolved first. <c>default</c> means nobody.
/// </summary>
public readonly record struct Subject(string Type, string Key)
{
    public const string PopType = "Pop";
    public bool IsNone => string.IsNullOrEmpty(Key);

    /// <summary>A country, character, party, state or location by its scope key.</summary>
    public static Subject Of(IScope? s) => s == null ? default : new Subject(s.ScopeType.ToString(), s.ScopeKey);
    public static implicit operator Subject(Country? c) => Of(c);
    public static implicit operator Subject(Character? c) => Of(c);

    /// <summary>A pop group has no identity of its own, so it is named by where it lives and what it is.</summary>
    public static Subject Pop(string stateKey, string culture, string? religion, string? type)
        => new(PopType, $"{stateKey}|{culture}|{religion}|{type}");
}

/// <summary>One remembered act: <see cref="Actor"/> did <see cref="Kind"/> to <see cref="Target"/> on <see cref="Date"/>.</summary>
public sealed class MemoryEntry
{
    public string Kind = "";
    public Subject Actor;
    public Subject? Target;
    public GameDate Date;
    public double Weight;
    /// <summary>Fraction of the weight lost per year (exponential); 0 never fades.</summary>
    public double DecayPerYear;
    public string Note = "";
}

/// <summary>
/// The Memory: a memory of notable acts (broke a treaty, abandoned an ally, ...) kept for every kind of <see cref="Subject"/>:
/// countries, characters, parties, states and pop groups. Other systems and scripts read it to decide what a subject is thought to be. Systems add entries through <see cref="Record"/>; the built-in
/// ones come from <see cref="GameEvents"/>. Saved in the versioned <c>x_memory</c> section; a save without it loads empty.
/// </summary>
public sealed class MemorySystem
{
    public const int SaveVersion = 2;
    private const int MaxPerActor = 200;
    private const double ForgetBelow = 0.05;

    private readonly Game _game;
    private readonly Dictionary<Subject, List<MemoryEntry>> _byActor = new();
    private readonly Dictionary<string, MemoryKind> _kinds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after every recorded act (the Reaction engine, when it exists, listens here).</summary>
    public event Action<MemoryEntry>? Recorded;

    public MemorySystem(Game game)
    {
        _game = game;
        foreach (var k in new[]
        {
            new MemoryKind("broke_treaty", "Broke a treaty", 3, 0.15),
            new MemoryKind("declared_war", "Declared war", 1, 0.4),
            new MemoryKind("abandoned_ally", "Abandoned an ally", 4, 0.1),
            new MemoryKind("honoured_alliance", "Answered the call to arms", 2, 0.2, Good: true),
            new MemoryKind("subjugated", "Made a country a subject", 2, 0.2),
            new MemoryKind("released_subject", "Freed a subject", 2, 0.2, Good: true),
            // characters
            new MemoryKind("crowned", "Took the throne of", 1.5, 0.05, Good: true),
            new MemoryKind("deposed", "Was deposed in", 2, 0.1),
            new MemoryKind("bereaved", "Lost a relative", 1, 0.3),
            new MemoryKind("appointed", "Was given office in", 1, 0.2, Good: true),
            new MemoryKind("dismissed", "Was dismissed from office in", 1.5, 0.2),
            // parties
            new MemoryKind("seized_power", "Seized power in", 2, 0.05),
            new MemoryKind("took_office", "Took office in", 1.5, 0.15, Good: true),
            new MemoryKind("lost_power", "Lost power in", 2, 0.1),
            new MemoryKind("won_election", "Won the election in", 1, 0.3, Good: true),
            new MemoryKind("banned", "Was banned in", 3, 0.05),
            // pop groups and states
            new MemoryKind("occupied", "Lived under occupation by", 1, 0.15),
            new MemoryKind("annexed", "Was taken over by", 2, 0.1),
            new MemoryKind("assimilated", "Was pressed to take the culture of", 0.5, 0.1),
        }) _kinds[k.Key] = k;
        game.SaveSections.Add(new SaveSection { Name = "memory", Write = Save, Read = Load, Reset = () => _byActor.Clear() });
    }

    /// <summary>Wires the built-in acts to the hub. Called once when the game is built.</summary>
    public void Subscribe(GameEvents hub)
    {
        hub.WarDeclared += (a, d, _) => Record(a, "declared_war", d);
        hub.PactBroken += (breaker, other) => Record(breaker, "broke_treaty", other);
        hub.CallAnswered += (ally, caller, joined) => Record(ally, joined ? "honoured_alliance" : "abandoned_ally", caller);
        hub.SubjectMade += (s, o, _) => Record(o, "subjugated", s);
        hub.SubjectReleased += (s, o) => { if (o != null) Record(o, "released_subject", s); };
        hub.RulerChanged += (c, old, now) =>
        {
            Record(now, "crowned", c);
            if (old is { Alive: true } && old != now) Record(old, "deposed", c);
        };
        hub.CharacterDied += (dead, reason) =>
        {
            foreach (var kin in new[] { dead.Spouse, dead.Father, dead.Mother }.Concat(dead.Children))
                if (kin is { Alive: true }) Record(kin, "bereaved", dead, note: reason);
        };
        hub.MinisterChanged += (c, who, appointed) => Record(who, appointed ? "appointed" : "dismissed", c);
        hub.RulingPartyChanged += (c, oldParty, newParty, reason) =>
        {
            Record(PartyOf(newParty), reason is "coup" or "overlord" ? "seized_power" : "took_office", c);
            Record(PartyOf(oldParty), "lost_power", c);
        };
        hub.ElectionHeld += (c, winner) => Record(PartyOf(winner), "won_election", c);
        hub.PartyBanned += (c, party) => Record(PartyOf(party), "banned", c);
        hub.LocationOccupied += (l, old, by) =>
        {
            if (l.State is not { } state) return;
            RecordOnce(Subject.Of(state), "occupied", by, 90);
            foreach (var pop in PopsOf(state)) RecordOnce(pop, "occupied", by, 90);
        };
        hub.StateTransferred += (state, old, now) =>
        {
            if (old == null) return;
            RecordOnce(Subject.Of(state), "annexed", now, 30);
            foreach (var pop in PopsOf(state)) RecordOnce(pop, "annexed", now, 30);
        };
        hub.PopAssimilated += (state, pop, owner) => RecordOnce(PopSubject(state, pop), "assimilated", owner, 1825);
    }

    /// <summary>Every entry of every subject (for diagnostics).</summary>
    /// <summary>Keys of every character that is the actor or the target of a remembered act.</summary>
    public HashSet<string> CharacterKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string type = nameof(ScopeType.Character);
        foreach (var list in _byActor.Values)
            foreach (var e in list)
            {
                if (e.Actor.Type == type) keys.Add(e.Actor.Key);
                if (e.Target is { } t && t.Type == type) keys.Add(t.Key);
            }
        return keys;
    }

    public IEnumerable<MemoryEntry> All => _byActor.Values.SelectMany(l => l);
    public IEnumerable<MemoryKind> Kinds => _kinds.Values;
    public MemoryKind? Kind(string key) => _kinds.GetValueOrDefault(key);
    /// <summary>Adds or replaces a kind (scripts and scenarios define their own).</summary>
    public void DefineKind(MemoryKind k) => _kinds[k.Key] = k;

    private static Subject PopSubject(State state, Burger.Core.Population.Pop p) => Subject.Pop(state.ScopeKey, p.Culture, p.Religion, p.Type);

    private IEnumerable<Subject> PopsOf(State state)
        => _game.Population.Enabled ? _game.Population.Of(state).Pops.Select(p => PopSubject(state, p)) : Enumerable.Empty<Subject>();

    /// <summary>The party a party key names, or nobody when it is only an ideology.</summary>
    private Subject PartyOf(string key)
        => _game.World.GetGenericScope(key) is { } scope && PoliticsSystem.IsParty(scope) ? Subject.Of(scope) : default;

    /// <summary>Records an act unless the same kind against the same target was already recorded within <paramref name="withinDays"/>.</summary>
    public MemoryEntry? RecordOnce(Subject actor, string kind, Subject? target, int withinDays, double? weight = null, string? note = null)
    {
        if (actor.IsNone) return null;
        if (_byActor.TryGetValue(actor, out var list)
            && list.Any(e => e.Kind == kind && e.Target == target && Now.TotalDays - e.Date.TotalDays < withinDays)) return null;
        return Record(actor, kind, target, weight, null, note);
    }

    private GameDate Now => _game.World.Date;

    /// <summary>Records an act. Weight and decay default to the kind's; unknown kinds need both given.</summary>
    public MemoryEntry? Record(Subject actor, string kind, Subject? target = null, double? weight = null, double? decayPerYear = null, string? note = null)
    {
        if (actor.IsNone || (actor.Type == nameof(ScopeType.Country) && _game.World.GetCountry(actor.Key) is not { Exists: true })) return null;
        if (target is { IsNone: true }) target = null;
        _kinds.TryGetValue(kind, out var def);
        if (def == null && weight == null) return null;
        var e = new MemoryEntry
        {
            Kind = kind, Actor = actor, Target = target, Date = Now,
            Weight = weight ?? def!.Weight, DecayPerYear = decayPerYear ?? def?.DecayPerYear ?? 0.2,
            Note = note ?? "",
        };
        if (!_byActor.TryGetValue(actor, out var list)) _byActor[actor] = list = new();
        list.Add(e);
        if (list.Count > MaxPerActor) Trim(list);
        Recorded?.Invoke(e);
        return e;
    }

    /// <summary>The entry's weight now, after fading.</summary>
    public double Current(MemoryEntry e)
    {
        if (e.DecayPerYear <= 0) return e.Weight;
        double years = (Now.TotalDays - e.Date.TotalDays) / 365.0;
        return e.Weight * Math.Exp(-e.DecayPerYear * Math.Max(0, years));
    }

    /// <summary>All of <paramref name="actor"/>'s acts that still count, newest first.</summary>
    public IEnumerable<MemoryEntry> Of(Subject actor, string? kind = null, Subject? target = null)
    {
        if (!_byActor.TryGetValue(actor, out var list)) return Enumerable.Empty<MemoryEntry>();
        return list.Where(e => (kind == null || e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) && (target == null || e.Target == target)
                               && Current(e) >= ForgetBelow).OrderByDescending(e => e.Date.TotalDays);
    }

    /// <summary>Summed current weight of matching acts.</summary>
    public double Weight(Subject actor, string? kind = null, Subject? target = null) => Of(actor, kind, target).Sum(Current);

    public bool Has(Subject actor, string kind, Subject? target = null, double min = 0.05) => Weight(actor, kind, target) >= min;

    /// <summary>Acts done to <paramref name="victim"/> by anyone, newest first.</summary>
    public IEnumerable<MemoryEntry> Against(Subject victim) =>
        _byActor.Values.SelectMany(l => l).Where(e => e.Target == victim && Current(e) >= ForgetBelow).OrderByDescending(e => e.Date.TotalDays);

    public void MonthlyTick()
    {
        foreach (var list in _byActor.Values) list.RemoveAll(e => e.DecayPerYear > 0 && Current(e) < ForgetBelow);
        foreach (var empty in _byActor.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) _byActor.Remove(empty);
    }

    private void Trim(List<MemoryEntry> list)
    {
        var drop = list.OrderBy(Current).Take(list.Count - MaxPerActor).ToHashSet();
        list.RemoveAll(drop.Contains);
    }

    /// <summary>The strongest acts among several subjects (a state and its pop groups), one line per kind and target, strongest first.</summary>
    public IEnumerable<MemoryEntry> Strongest(IEnumerable<Subject> subjects, int count)
        => subjects.SelectMany(s => Of(s)).GroupBy(e => (e.Kind, e.Target)).Select(g => g.OrderByDescending(Current).First())
            .OrderByDescending(Current).Take(count);

    /// <summary>One line for the interface: what was done, to whom, and when.</summary>
    public string Describe(MemoryEntry e)
        => (Kind(e.Kind)?.Label ?? e.Kind.Replace('_', ' ')) + (e.Target is { } t ? $": {NameOf(t)}" : "") + $" · {e.Date.Year}";

    /// <summary>A name for the interface; falls back to the key when the subject no longer exists.</summary>
    public string NameOf(Subject s)
    {
        if (s.IsNone) return "";
        var w = _game.World;
        if (s.Type == Subject.PopType)
        {
            var p = s.Key.Split('|');
            if (p.Length < 4) return s.Key;
            string where = w.GetState(p[0])?.DisplayName ?? p[0];
            return $"{string.Join(' ', new[] { p[1], p[3] }.Where(x => x.Length > 0))} of {where}";
        }
        return s.Type switch
        {
            nameof(ScopeType.Country) => w.GetCountry(s.Key)?.Name,
            nameof(ScopeType.Character) => w.GetCharacter(s.Key)?.DisplayName,
            nameof(ScopeType.State) => w.GetState(s.Key)?.DisplayName,
            _ => w.GetGenericScope(s.Key)?.DisplayName,
        } ?? s.Key;
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        root.Add("version", SaveVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var e in _byActor.Values.SelectMany(l => l))
        {
            var n = root.AddBlock("entry");
            n.Add("kind", e.Kind);
            n.Add("actor", e.Actor.Key);
            n.Add("actor_type", e.Actor.Type);
            if (e.Target is { } t) { n.Add("target", t.Key); n.Add("target_type", t.Type); }
            n.Add("date", e.Date.ToScript());
            n.Add("weight", e.Weight.ToString("R", CultureInfo.InvariantCulture));
            n.Add("decay", e.DecayPerYear.ToString("R", CultureInfo.InvariantCulture));
            if (e.Note.Length > 0) n.Add("note", e.Note, true);
        }
    }

    private void Load(Node root)
    {
        foreach (var n in root.GetAll("entry"))
        {
            if (n.GetString("actor") is not { Length: > 0 } actorKey) continue;
            // Version 1 saves held countries only and have no type.
            var actor = new Subject(n.GetString("actor_type") ?? nameof(ScopeType.Country), actorKey);
            Subject? target = n.GetString("target") is { Length: > 0 } tk ? new Subject(n.GetString("target_type") ?? nameof(ScopeType.Country), tk) : (Subject?)null;
            var e = new MemoryEntry
            {
                Kind = n.GetString("kind") ?? "", Actor = actor, Target = target,
                Date = GameDate.Parse(n.GetString("date") ?? "", Now), Weight = n.GetDouble("weight", 1),
                DecayPerYear = n.GetDouble("decay", 0.2), Note = n.GetString("note") ?? "",
            };
            if (!_byActor.TryGetValue(actor, out var list)) _byActor[actor] = list = new();
            list.Add(e);
        }
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        Subject? With(Node n, ScriptContext ctx) => n.GetString("with") is { } w && s.TryResolveScope(w, ctx, out var o) && o is IScope sc ? (Subject?)Subject.Of(sc) : null;
        s.RegisterTrigger("has_memory", (ctx, n) => ctx.This is IScope self && n.GetString("kind") is { } k
                && Weight(Subject.Of(self), k, With(n, ctx)) >= (n.Has("min") ? s.ResolveNumber(n.GetString("min"), ctx) : 0.05),
            (ctx, n) => $"Is remembered for: {Kind(n.GetString("kind") ?? "")?.Label ?? n.GetString("kind")}{(With(n, ctx) is { } w ? " against " + NameOf(w) : "")}");
        s.RegisterEffect("add_memory", (ctx, n) =>
        {
            if (ctx.This is not IScope self || n.GetString("kind") is not { } k) return;
            Record(Subject.Of(self), k, With(n, ctx), n.Has("weight") ? s.ResolveNumber(n.GetString("weight"), ctx) : null,
                n.Has("decay") ? s.ResolveNumber(n.GetString("decay"), ctx) : null, n.GetString("note"));
        }, (ctx, n) => $"Remembered act: {Kind(n.GetString("kind") ?? "")?.Label ?? n.GetString("kind")}");
    }
}
