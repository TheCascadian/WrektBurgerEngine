using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Warfare;

/// <summary>Armies under the orders of a field marshal: orders given once to the group go to all its armies.</summary>
public sealed class ArmyGroup
{
    public int Id;
    public string Name = "";
    public Country Owner = null!;
    public Character? Marshal;
    public List<Army> Armies = new();
    /// <summary>The theatre: the part of the border the group's armies hold (own or enemy border locations). Empty = the whole border.</summary>
    public HashSet<Location> Theatre = new();
    public Country? Enemy;
    public ArmyMode Mode = ArmyMode.Hold;
}

/// <summary>What an army is made of, by arm.</summary>
public sealed class Composition
{
    /// <summary>Regiments (battalions) of each arm, weighted by the strength of their divisions.</summary>
    public readonly Dictionary<string, double> Arms = new(StringComparer.OrdinalIgnoreCase);
    public double Total => Arms.Values.Sum();
    public double ShareOf(string arm) => Total > 0 ? Arms.GetValueOrDefault(arm) / Total : 0;
}

/// <summary>
/// Army organization (army_organization feature): the regiments of a division's template make up the stack of an army, and an army that mixes arms (infantry, cavalry,
/// support such as artillery) fights better: +6% for each arm beyond the first that makes up a tenth of it, at most +12%. Armies join army groups under a field marshal
/// (+0.5% a point of martial to every army in it); orders and the theatre are given once for the group.
/// </summary>
public sealed class ArmyOrganizationSystem
{
    private readonly Game _game;
    public readonly List<ArmyGroup> Groups = new();
    private int _nextId = 1;
    private readonly Dictionary<object, double> _bonusCache = new();
    private long _cacheHour = long.MinValue;

    public ArmyOrganizationSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "army_groups", Write = Save, Read = Load, Reset = () => Groups.Clear() });
        game.Warfare.CombatFactorSources.Add(d => Enabled ? 1 + CombinedArmsBonusOf(d) + MarshalBonusOf(d) : 1);
    }

    public bool Enabled => _game.Features.Has(FeatureSet.ArmyOrganization) && _game.Features.Has(FeatureSet.Military);

    // ------------------------------------------------------------------------------------- composition and combined arms
    /// <summary>The arm of a unit type: its category (infantry, cavalry, support).</summary>
    public static string ArmOf(UnitType u) => string.IsNullOrEmpty(u.Category) ? "infantry" : u.Category.ToLowerInvariant();

    public Composition CompositionOf(IEnumerable<Division> divisions)
    {
        var c = new Composition();
        foreach (var d in divisions)
            foreach (var (type, count) in d.Template.Units)
                c.Arms[ArmOf(type)] = c.Arms.GetValueOrDefault(ArmOf(type)) + count * Math.Max(0.1, d.StrengthFraction);
        return c;
    }

    public Composition CompositionOf(Army a) => CompositionOf(a.Divisions);

    /// <summary>The divisions a division fights beside for the combined-arms bonus: its army, else the divisions of its owner in the same place.</summary>
    public IEnumerable<Division> StackOf(Division d)
        => d.Army != null ? d.Army.Divisions : _game.World.Divisions.Where(x => x.Owner == d.Owner && x.Location == d.Location);

    /// <summary>Combined arms: +6% a mixed arm (an arm that is a tenth of the stack) beyond the first, at most two of them.</summary>
    public double CombinedArmsBonus(Composition c)
    {
        var d = _game.Data.Defines;
        double min = d.Get("combined_arms_min_share", 0.10), per = d.Get("combined_arms_bonus", 0.06);
        int arms = c.Arms.Keys.Count(k => c.ShareOf(k) >= min);
        return per * Math.Min(2, Math.Max(0, arms - 1));
    }

    public double CombinedArmsBonus(Army a) => CombinedArmsBonus(CompositionOf(a));

    private void FreshCache()
    {
        long hour = _game.World.Date.TotalDays * 24L + _game.World.Date.Hour;
        if (hour != _cacheHour) { _cacheHour = hour; _bonusCache.Clear(); }
    }

    private double CombinedArmsBonusOf(Division d)
    {
        FreshCache();
        object key = d.Army != null ? d.Army : (object)(d.Owner, d.Location);
        if (_bonusCache.TryGetValue(key, out var v)) return v;
        return _bonusCache[key] = CombinedArmsBonus(CompositionOf(StackOf(d)));
    }

    /// <summary>Text for tooltips: what an army gets from mixing arms.</summary>
    public string DescribeBonus(Army a)
    {
        double b = CombinedArmsBonus(a);
        var c = CompositionOf(a);
        string arms = string.Join(", ", c.Arms.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {c.ShareOf(kv.Key):P0}"));
        return $"Combined arms {b:+0%;-0%;+0%}\n{arms}";
    }

    // ------------------------------------------------------------------------------------- groups
    public ArmyGroup? GroupOf(Army a) => Groups.FirstOrDefault(g => g.Armies.Contains(a));
    public IEnumerable<ArmyGroup> GroupsOf(Country c) => Groups.Where(g => g.Owner == c);

    public ArmyGroup CreateGroup(Country owner, string? name = null, Character? marshal = null)
    {
        int n = Groups.Count(g => g.Owner == owner) + 1;
        var g = new ArmyGroup { Id = _nextId++, Owner = owner, Name = name ?? $"{WarfareSystem.Ordinal(n)} Army Group" };
        Groups.Add(g);
        if (marshal != null) SetMarshal(g, marshal);
        return g;
    }

    public void DisbandGroup(ArmyGroup g) => Groups.Remove(g);

    public string? CannotAdd(ArmyGroup g, Army a)
    {
        if (a.Owner != g.Owner) return "Not one of our armies";
        if (GroupOf(a) is { } other && other != g) return $"Already in the {other.Name}";
        if (g.Armies.Contains(a)) return "Already in the group";
        return null;
    }

    public string? Add(ArmyGroup g, Army a)
    {
        var why = CannotAdd(g, a);
        if (why != null) return why;
        g.Armies.Add(a);
        Apply(g, a);
        return null;
    }

    public void Remove(ArmyGroup g, Army a) => g.Armies.Remove(a);

    public string? CannotSetMarshal(ArmyGroup g, Character ch)
    {
        if (!ch.Alive || ch.Country != g.Owner) return "Not one of our characters";
        if (ch.IsRuler) return "The ruler leads the realm";
        if (ch.Age(_game.World.Date) < _game.Data.Defines.AdultAge) return "Too young";
        if (Groups.Any(x => x != g && x.Marshal == ch)) return "Commands another group";
        if (_game.World.Armies.Any(a => a.General == ch)) return "Commands an army";
        return null;
    }

    public string? SetMarshal(ArmyGroup g, Character? ch)
    {
        if (ch != null)
        {
            var why = CannotSetMarshal(g, ch);
            if (why != null) return why;
            ch.IsGeneral = true;
        }
        g.Marshal = ch;
        return null;
    }

    /// <summary>The group's orders go to every army: the front against an enemy and how it is held.</summary>
    public void SetOrders(ArmyGroup g, Country? enemy, ArmyMode mode)
    {
        g.Enemy = enemy;
        g.Mode = mode;
        foreach (var a in g.Armies) { _game.Warfare.SetFront(a, enemy, mode); a.FrontLimit = new HashSet<Location>(g.Theatre); a.FrontHash = 0; }
    }

    /// <summary>Draws the group's theatre: the armies hold only that part of the border.</summary>
    public void SetTheatre(ArmyGroup g, IEnumerable<Location> locations)
    {
        g.Theatre = new HashSet<Location>(locations);
        foreach (var a in g.Armies) { a.FrontLimit = new HashSet<Location>(g.Theatre); a.FrontHash = 0; }
    }

    private void Apply(ArmyGroup g, Army a)
    {
        if (g.Enemy != null) _game.Warfare.SetFront(a, g.Enemy, g.Mode);
        a.FrontLimit = new HashSet<Location>(g.Theatre);
        a.FrontHash = 0;
    }

    public double MarshalSkill(ArmyGroup g) => g.Marshal is { Alive: true } m ? m.GetStat(_game.Data.Defines.GeneralStat) : 0;
    public double MarshalBonus(ArmyGroup g) => MarshalSkill(g) * _game.Data.Defines.Get("marshal_bonus", 0.005);

    private double MarshalBonusOf(Division d) => d.Army != null && GroupOf(d.Army) is { } g ? MarshalBonus(g) : 0;

    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var g in Groups.ToList())
        {
            if (!g.Owner.Exists) { Groups.Remove(g); continue; }
            g.Armies.RemoveAll(a => !_game.World.Armies.Contains(a));
            if (g.Marshal != null && (!g.Marshal.Alive || g.Marshal.Country != g.Owner)) g.Marshal = null;
        }
    }

    public void RegisterScript()
    {
        _game.Script.RegisterValue("army_group_count", (ctx, scope) => scope is Country c && Enabled ? GroupsOf(c).Count() : null);
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        root.Add("next_id", _nextId.ToString(CultureInfo.InvariantCulture));
        foreach (var g in Groups)
        {
            var n = root.AddBlock("group");
            n.Add("id", g.Id.ToString(CultureInfo.InvariantCulture)); n.Add("name", g.Name, quoted: true); n.Add("owner", g.Owner.Tag);
            if (g.Marshal != null) n.Add("marshal", g.Marshal.Key, quoted: true);
            n.Add("armies", string.Join(' ', g.Armies.Select(a => a.Id)), quoted: true);
            n.Add("theatre", string.Join(' ', g.Theatre.OrderBy(l => l.Index).Select(l => l.Key)), quoted: true);
            if (g.Enemy != null) n.Add("enemy", g.Enemy.Tag);
            n.Add("mode", g.Mode.ToString());
        }
    }

    private void Load(Node root)
    {
        Groups.Clear();
        _nextId = Math.Max(1, (int)root.GetDouble("next_id", 1));
        var w = _game.World;
        foreach (var b in root.GetAll("group"))
        {
            if (w.GetCountry(b.GetString("owner")) is not { } owner) continue;
            var g = new ArmyGroup { Id = (int)b.GetDouble("id"), Name = b.GetString("name") ?? "Army Group", Owner = owner, Marshal = w.GetCharacter(b.GetString("marshal")), Enemy = w.GetCountry(b.GetString("enemy")) };
            Enum.TryParse(b.GetString("mode"), out g.Mode);
            foreach (var id in (b.GetString("armies") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(id, out var aid) && w.Armies.FirstOrDefault(a => a.Id == aid) is { } army) g.Armies.Add(army);
            foreach (var key in (b.GetString("theatre") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (w.GetLocation(key) is { } loc) g.Theatre.Add(loc);
            Groups.Add(g);
        }
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 67;
        foreach (var g in Groups) h = h * 31 + g.Id + g.Armies.Count * 7 + g.Theatre.Count;
        return h;
    }
}
