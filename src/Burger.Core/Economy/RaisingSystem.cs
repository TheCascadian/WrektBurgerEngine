using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Economy;

/// <summary>A company of hired soldiers from <c>common/mercenaries/*.txt</c>.</summary>
public sealed class MercenaryDef
{
    public string Key = "", Name = "", Desc = "";
    public int Divisions = 4;
    /// <summary>Division template key.</summary>
    public string Template = "infantry_division";
    /// <summary>Gold a month for each division.</summary>
    public double Cost = 20;
    /// <summary>Their worth in battle against regular soldiers (1 = as good).</summary>
    public double Quality = 0.8;
}

/// <summary>A company hired by a country.</summary>
public sealed class MercenaryCompany
{
    public Country Owner = null!;
    public MercenaryDef Def = null!;
    public int HiredDay;
}

/// <summary>
/// Raising troops (raising_troops feature): beside recruiting from manpower a country can mobilise (2% of its people for 90 days, then 90 days rest), raise levies
/// (up to 1.5% of its people, weak and free, that cost a tenth of its tax and go home after the war), hire mercenary companies by the month, call its subjects'
/// divisions to arms, and keep a foreign legion that grows by a thousandth of its people a month.
/// </summary>
public sealed class RaisingSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, MercenaryDef> Mercenaries = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<MercenaryCompany> Companies = new();
    private readonly Dictionary<Country, int> _mobilisedDay = new();
    private readonly Dictionary<Country, int> _restUntil = new();
    private readonly Dictionary<Country, int> _levyPeaceDay = new();
    private readonly HashSet<Country> _levies = new();
    private readonly HashSet<Country> _legion = new();
    private readonly Dictionary<Country, double> _legionPool = new();

    public const string Mobilised = "mobilised", Levy = "levy", Mercenary = "mercenary", Legion = "legion", Attached = "attached";

    /// <summary>Counts for reports.</summary>
    public int Mobilisations, LeviesRaised, MercenariesHired, CallsToArms, LegionsRaised;

    public RaisingSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "raising_troops", Write = Save, Read = Load, Reset = Reset });
        game.Warfare.CombatFactorSources.Add(d => Enabled && d.Raised == Mercenary && CompanyOf(d) is { } co ? co.Def.Quality : 1);
    }

    public bool Enabled => _game.Features.Has(FeatureSet.RaisingTroops) && _game.Features.Has(FeatureSet.Military);
    private int Today => _game.World.Date.TotalDays;
    private double Define(string key, double def) => _game.Data.Defines.Get(key, def);

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Mercenaries.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/mercenaries"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Mercenaries[n.Key] = new MercenaryDef
                {
                    Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!,
                    Divisions = Math.Max(1, n.GetInt("divisions", 4)), Template = n.GetString("template", "infantry_division")!,
                    Cost = n.GetDouble("cost", 20), Quality = n.GetDouble("quality", 0.8),
                };
            }
    }

    // ------------------------------------------------------------------------------------- common
    private double Population(Country c) => _game.Population.Enabled ? _game.Population.HomeStates(c).Sum(_game.Population.TotalOf) : c.OwnedStates.Sum(s => s.Manpower) * 100;

    /// <summary>Share of its people a country may call up (modifier 'recruitable' on 3%, between 1% and 5%; conscription laws set it).</summary>
    public double RecruitableShare(Country c) => Math.Clamp(Define("recruitable_share", 0.03) + _game.Modifiers.Sum(c, "recruitable"), 0.01, 0.05);

    /// <summary>The most manpower a country can hold in its pool: the recruitable share of its people (more while mobilised), but always enough for two divisions so that no country is left unable to recruit.</summary>
    public double ManpowerCap(Country c)
    {
        double cap = Population(c) * RecruitableShare(c) + (IsMobilised(c) ? MobilisationMen(c) : 0);
        var t = TemplateOf(_game.Data.Defines.Str("mobilisation_template", "infantry_division"), c);
        return Math.Max(cap, t == null ? 0 : t.Manpower * Define("manpower_floor_divisions", 2));
    }

    private DivisionTemplate? TemplateOf(string key, Country c)
        => _game.Data.Templates.TryGetValue(key, out var t) && (string.IsNullOrEmpty(t.OwnerTag) || t.OwnerTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase)) ? t
           : _game.Data.Templates.Values.Where(x => string.IsNullOrEmpty(x.OwnerTag) && x.Units.Count > 0).OrderBy(x => x.Cost).FirstOrDefault();

    /// <summary>Land locations of the country it controls, one per state, richest states first (where raised troops appear).</summary>
    private List<Location> Muster(Country c)
        => c.OwnedStates.OrderByDescending(s => _game.Population.Enabled ? _game.Population.TotalOf(s) : s.Tax)
            .Select(s => s.Locations.FirstOrDefault(l => l.IsLand && l.IsPassable && l.Controller == c)).Where(l => l != null).Cast<Location>().ToList();

    private int Spawn(Country c, DivisionTemplate t, int count, string tag, double strength = 1, double org = 1, MercenaryCompany? company = null)
    {
        var spots = Muster(c);
        if (spots.Count == 0) return 0;
        for (int i = 0; i < count; i++)
        {
            var d = _game.Warfare.CreateDivision(c, t, spots[i % spots.Count]);
            d.Raised = tag;
            d.Strength = d.MaxStrength * strength;
            d.Org = d.MaxOrg * org;
            d.RaisedMark = company?.HiredDay ?? 0;
        }
        return count;
    }

    public IEnumerable<Division> RaisedOf(Country c, string tag) => _game.World.Divisions.Where(d => d.Owner == c && d.Raised == tag);

    private void Disband(Country c, string tag)
    {
        foreach (var d in RaisedOf(c, tag).ToList())
        {
            if (d.InBattle) continue;
            c.Manpower += tag == Mobilised ? d.Template.Manpower * d.StrengthFraction : 0;
            _game.Warfare.DestroyDivision(d);
        }
    }

    // ------------------------------------------------------------------------------------- mobilisation
    public bool IsMobilised(Country c) => _mobilisedDay.ContainsKey(c);
    public int MobilisedDaysLeft(Country c) => _mobilisedDay.TryGetValue(c, out var d) ? Math.Max(0, d + (int)Define("mobilisation_days", 90) - Today) : 0;
    public int RestDaysLeft(Country c) => _restUntil.TryGetValue(c, out var d) ? Math.Max(0, d - Today) : 0;

    /// <summary>The men a mobilisation calls up: 2% of the people.</summary>
    public double MobilisationMen(Country c) => Population(c) * Define("mobilisation_share", 0.02);
    public double MobilisationCost(Country c) => _game.Economy.GoldCosts ? MobilisationMen(c) * Define("mobilisation_gold", 1) : 0;

    public string? CannotMobilise(Country c)
    {
        if (!Enabled) return "Mobilisation is not used in this game";
        if (IsMobilised(c)) return "Already mobilised";
        if (RestDaysLeft(c) > 0) return $"The people need {RestDaysLeft(c)} more days of rest";
        if (MobilisationMen(c) < 100) return "Too few people";
        if (c.Gold < MobilisationCost(c)) return $"Needs {MobilisationCost(c):0} gold";
        if (Muster(c).Count == 0) return "No land to muster on";
        return null;
    }

    /// <summary>Calls up the reserve: the manpower comes at once and becomes divisions in every state; stability drops by 5.</summary>
    public string? Mobilise(Country c)
    {
        var why = CannotMobilise(c);
        if (why != null) return why;
        double men = MobilisationMen(c);
        c.Gold -= MobilisationCost(c);
        c.Manpower += men;
        _mobilisedDay[c] = Today;
        var t = TemplateOf(_game.Data.Defines.Str("mobilisation_template", "infantry_division"), c);
        if (t != null)
        {
            // at least one division: a small country mobilises what it has
            int n = (int)Math.Clamp(Math.Floor(c.Manpower / Math.Max(1, t.Manpower)), 1, Define("mobilisation_max_divisions", 24));
            c.Manpower = Math.Max(0, c.Manpower - Spawn(c, t, n, Mobilised) * t.Manpower);
        }
        if (_game.Resources.Defs.FirstOrDefault(r => r.Key == "stability") is { } st) _game.Resources.Set(c, st, _game.Resources.Get(c, st) - Define("mobilisation_stability", 5));
        Mobilisations++;
        _game.NotifyPlayer($"{c.Name} mobilises.", NotificationKind.War, c);
        return null;
    }

    private void EndMobilisation(Country c)
    {
        _mobilisedDay.Remove(c);
        _restUntil[c] = Today + (int)Define("mobilisation_rest_days", 90);
        Disband(c, Mobilised);
        _game.NotifyPlayer($"{c.Name}'s mobilisation ends.", NotificationKind.Info, c);
    }

    // ------------------------------------------------------------------------------------- levies
    public bool HasLevies(Country c) => _levies.Contains(c);
    public double LevyMen(Country c) => Population(c) * Define("levy_share", 0.015);

    public string? CannotRaiseLevies(Country c)
    {
        if (!Enabled) return "Levies are not used in this game";
        if (HasLevies(c)) return "Levies are already raised";
        if (!_game.World.WarsOf(c).Any()) return "Levies are only raised in a war";
        if (Muster(c).Count == 0) return "No land to muster on";
        return null;
    }

    /// <summary>Raises up to 1.5% of the people as divisions of 70% strength and 30 organisation; for as long as they stand the country's tax is a tenth less.</summary>
    public string? RaiseLevies(Country c)
    {
        var why = CannotRaiseLevies(c);
        if (why != null) return why;
        var t = TemplateOf(_game.Data.Defines.Str("levy_template", "infantry_division"), c);
        if (t == null) return "No division to raise";
        int n = (int)Math.Clamp(Math.Floor(LevyMen(c) / Math.Max(1, t.Manpower)), 1, Define("levy_max_divisions", 30));
        double org = Math.Min(1, Define("levy_org", 30) / Math.Max(1, t.Stat("max_organisation", 50)));
        Spawn(c, t, n, Levy, Define("levy_strength", 0.7), org);
        _levies.Add(c);
        _levyPeaceDay.Remove(c);
        LeviesRaised++;
        _game.NotifyPlayer($"{c.Name} raises {n} levy divisions.", NotificationKind.War, c);
        return null;
    }

    public void DisbandLevies(Country c)
    {
        if (!_levies.Remove(c)) return;
        _levyPeaceDay.Remove(c);
        Disband(c, Levy);
    }

    // ------------------------------------------------------------------------------------- mercenaries
    public IEnumerable<MercenaryCompany> CompaniesOf(Country c) => Companies.Where(x => x.Owner == c);
    public MercenaryCompany? CompanyOf(Division d) => d.Raised == Mercenary ? Companies.FirstOrDefault(c => c.Owner == d.Owner && c.HiredDay == d.RaisedMark) : null;
    public double MercenaryUpkeep(Country c) => Companies.Where(x => x.Owner == c).Sum(x => x.Def.Divisions * x.Def.Cost);
    public int MaxCompanies => (int)Define("mercenary_companies", 3);

    public string? CannotHire(Country c, MercenaryDef def)
    {
        if (!Enabled) return "Mercenaries are not used in this game";
        if (CompaniesOf(c).Count() >= MaxCompanies) return $"At most {MaxCompanies} companies";
        if (CompaniesOf(c).Any(x => x.Def == def)) return "Already hired";
        if (_game.Economy.GoldCosts && c.Gold < def.Divisions * def.Cost) return $"Needs {def.Divisions * def.Cost:0} gold for the first month";
        if (Muster(c).Count == 0) return "No land to muster on";
        return null;
    }

    public string? Hire(Country c, MercenaryDef def)
    {
        var why = CannotHire(c, def);
        if (why != null) return why;
        var t = TemplateOf(def.Template, c);
        if (t == null) return "No division to hire";
        var co = new MercenaryCompany { Owner = c, Def = def, HiredDay = Today };
        // two companies hired the same day get different marks
        while (Companies.Any(x => x.Owner == c && x.HiredDay == co.HiredDay)) co.HiredDay++;
        Companies.Add(co);
        Spawn(c, t, def.Divisions, Mercenary, 1, 1, co);
        MercenariesHired++;
        return null;
    }

    public void Dismiss(MercenaryCompany co)
    {
        Companies.Remove(co);
        foreach (var d in _game.World.Divisions.Where(d => d.Owner == co.Owner && d.Raised == Mercenary && d.RaisedMark == co.HiredDay).ToList()) if (!d.InBattle) _game.Warfare.DestroyDivision(d);
    }

    // ------------------------------------------------------------------------------------- call to arms
    public string? CannotCallToArms(Country overlord)
    {
        if (!Enabled) return "Calls to arms are not used in this game";
        if (!_game.World.WarsOf(overlord).Any()) return "Only in a war";
        if (!_game.World.Relations.SubjectsOf(overlord).Any(s => s.Exists && s.IdleDivisions(_game) > 0)) return "No subject has divisions to send";
        return null;
    }

    /// <summary>Each subject sends a quarter of its divisions to fight under the overlord; its liberty desire is 5 higher while they are away.</summary>
    public int CallToArms(Country overlord)
    {
        if (CannotCallToArms(overlord) != null) return 0;
        int total = 0;
        foreach (var sub in _game.World.Relations.SubjectsOf(overlord).Where(s => s.Exists).ToList())
        {
            var mine = _game.World.Divisions.Where(d => d.Owner == sub && d.VolunteerFrom == null && !d.InBattle).OrderBy(d => d.Army != null).ThenBy(d => d.Army?.FrontEnemy != null).ToList();
            int want = (int)Math.Floor(_game.World.Divisions.Count(d => d.Owner == sub && d.VolunteerFrom == null) * Define("call_to_arms_share", 0.25));
            foreach (var d in mine.Take(want)) if (_game.Support.Attach(sub, overlord, d)) total++;
        }
        if (total > 0) { CallsToArms++; _game.NotifyPlayer($"{total} divisions of {overlord.Name}'s subjects answer the call.", NotificationKind.Good, overlord); }
        return total;
    }

    // ------------------------------------------------------------------------------------- legion
    public bool HasLegion(Country c) => _legion.Contains(c);
    public double LegionPool(Country c) => _legionPool.GetValueOrDefault(c);
    public void SetLegion(Country c, bool on) { if (on) _legion.Add(c); else _legion.Remove(c); }

    /// <summary>Country modifiers: a tenth less income while levies stand, +5 liberty desire for a subject whose divisions are away.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c) return 0;
        if (key.Equals("income", StringComparison.OrdinalIgnoreCase) && _levies.Contains(c)) return -Define("levy_tax", 0.10);
        if (key.Equals("liberty_desire", StringComparison.OrdinalIgnoreCase) && _game.World.Divisions.Any(d => d.VolunteerFrom == c && d.Raised == Attached)) return Define("call_to_arms_liberty", 5);
        return 0;
    }

    // ------------------------------------------------------------------------------------- ticks
    public void DailyTick()
    {
        if (!Enabled) return;
        foreach (var c in _mobilisedDay.Keys.ToList())
            if (!c.Exists || MobilisedDaysLeft(c) == 0 && !_game.World.WarsOf(c).Any() || Today - _mobilisedDay[c] > Define("mobilisation_days", 90) + 365) EndMobilisation(c);
        // a mobilisation that ran out during a war lasts until the war ends
        foreach (var c in _mobilisedDay.Keys.ToList())
            if (MobilisedDaysLeft(c) == 0 && !_game.World.WarsOf(c).Any()) EndMobilisation(c);
        foreach (var c in _levies.ToList())
        {
            if (!c.Exists) { _levies.Remove(c); continue; }
            if (_game.World.WarsOf(c).Any()) { _levyPeaceDay.Remove(c); continue; }
            if (!_levyPeaceDay.TryGetValue(c, out var since)) { _levyPeaceDay[c] = Today; continue; }
            if (Today - since >= (int)Define("levy_peace_days", 30)) DisbandLevies(c);
        }
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            double cap = ManpowerCap(c);
            if (cap > 0 && c.Manpower > cap) c.Manpower = cap;
        }
        // mercenaries are paid in the monthly expenses; a company that cannot be paid goes home
        foreach (var co in Companies.ToList())
        {
            if (!co.Owner.Exists) { Companies.Remove(co); continue; }
            if (_game.Economy.GoldCosts && co.Owner.Gold < 0) { Dismiss(co); _game.NotifyPlayer($"The {co.Def.Name} leave {co.Owner.Name}: their pay is overdue.", NotificationKind.Bad, co.Owner); }
        }
        foreach (var c in _legion.ToList())
        {
            if (!c.Exists) { _legion.Remove(c); continue; }
            double pool = _legionPool.GetValueOrDefault(c) + Population(c) * Define("legion_share", 0.001);
            var t = TemplateOf("foreign_legion", c);
            if (t != null && pool >= t.Manpower && Spawn(c, t, 1, Legion) > 0) { pool -= t.Manpower; LegionsRaised++; _game.NotifyPlayer($"A foreign legion division is ready in {c.Name}.", NotificationKind.Good, c); }
            _legionPool[c] = pool;
        }
        if (_game.Ai.Enabled) AiMonthly();
    }

    private void AiMonthly()
    {
        var rng = _game.Rngs.Combat;
        foreach (var c in _game.World.Countries.Where(c => c.Exists && !c.IsPlayer).ToList())
        {
            var wars = _game.World.WarsOf(c).ToList();
            if (wars.Count == 0) continue;
            double mine = _game.Diplomacy.Strength(c);
            double theirs = wars.SelectMany(w => w.EnemiesOf(c)).Distinct().Sum(e => _game.Diplomacy.Strength(e));
            bool outmatched = theirs > mine * 0.8;
            if (outmatched && CannotRaiseLevies(c) == null && rng.NextDouble() < 0.4) RaiseLevies(c);
            if (outmatched && CannotMobilise(c) == null && rng.NextDouble() < 0.2) Mobilise(c);
            if (c.Gold > 400 && outmatched && CompaniesOf(c).Count() < 1 && Mercenaries.Values.FirstOrDefault() is { } m && CannotHire(c, m) == null && rng.NextDouble() < 0.2) Hire(c, m);
            if (CannotCallToArms(c) == null && !c.Vars.HasFlag("called_arms") && _game.World.Relations.SubjectsOf(c).Any()) { CallToArms(c); c.Vars.SetFlag("called_arms"); }
        }
        foreach (var c in _game.World.Countries) if (c.Vars.HasFlag("called_arms") && !_game.World.WarsOf(c).Any()) c.Vars.ClearFlag("called_arms");
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterEffect("mobilise", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) Mobilise(c); }, (ctx, n) => "Mobilises");
        s.RegisterEffect("raise_levies", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) RaiseLevies(c); }, (ctx, n) => "Raises levies");
        s.RegisterEffect("disband_levies", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) DisbandLevies(c); }, (ctx, n) => "Disbands levies");
        s.RegisterEffect("hire_mercenaries", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c && Mercenaries.TryGetValue(n.Value ?? "", out var def)) Hire(c, def); }, (ctx, n) => $"Hires the {n.Value}");
        s.RegisterEffect("call_to_arms_subjects", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) CallToArms(c); }, (ctx, n) => "Calls its subjects' divisions to arms");
        s.RegisterEffect("set_foreign_legion", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) SetLegion(c, n.Value != "no"); }, (ctx, n) => "Starts or stops the foreign legion");
        s.RegisterTrigger("is_mobilised", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && IsMobilised(c), (ctx, n) => "Is mobilised");
        s.RegisterTrigger("has_levies", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && HasLevies(c), (ctx, n) => "Has levies raised");
        s.RegisterTrigger("can_mobilise", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && CannotMobilise(c) == null, (ctx, n) => "Can mobilise");
        s.RegisterTrigger("can_raise_levies", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && CannotRaiseLevies(c) == null, (ctx, n) => "Can raise levies");
        s.RegisterTrigger("can_call_to_arms", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && CannotCallToArms(c) == null, (ctx, n) => "Can call its subjects' divisions to arms");
        s.RegisterValue("recruitable_share", (ctx, scope) => scope is Country c && Enabled ? RecruitableShare(c) : null);
        s.RegisterValue("mercenary_upkeep", (ctx, scope) => scope is Country c && Enabled ? MercenaryUpkeep(c) : null);
    }

    // ------------------------------------------------------------------------------------- save
    private void Reset()
    {
        Companies.Clear(); _mobilisedDay.Clear(); _restUntil.Clear(); _levyPeaceDay.Clear(); _levies.Clear(); _legion.Clear(); _legionPool.Clear();
    }

    private void Save(Node root)
    {
        foreach (var c in _game.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            if (!_mobilisedDay.ContainsKey(c) && !_restUntil.ContainsKey(c) && !_levies.Contains(c) && !_legion.Contains(c)) continue;
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            if (_mobilisedDay.TryGetValue(c, out var md)) n.Add("mobilised", md.ToString(CultureInfo.InvariantCulture));
            if (_restUntil.TryGetValue(c, out var ru)) n.Add("rest", ru.ToString(CultureInfo.InvariantCulture));
            if (_levies.Contains(c)) n.Add("levies", "yes");
            if (_levyPeaceDay.TryGetValue(c, out var lp)) n.Add("levy_peace", lp.ToString(CultureInfo.InvariantCulture));
            if (_legion.Contains(c)) n.Add("legion", "yes");
            n.Add("legion_pool", _legionPool.GetValueOrDefault(c).ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (var co in Companies)
        {
            var n = root.AddBlock("company");
            n.Add("owner", co.Owner.Tag); n.Add("def", co.Def.Key); n.Add("day", co.HiredDay.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        Reset();
        foreach (var b in root.GetAll("country"))
            if (_game.World.GetCountry(b.GetString("tag")) is { } c)
            {
                if (b.Has("mobilised")) _mobilisedDay[c] = (int)b.GetDouble("mobilised");
                if (b.Has("rest")) _restUntil[c] = (int)b.GetDouble("rest");
                if (b.GetBool("levies")) _levies.Add(c);
                if (b.Has("levy_peace")) _levyPeaceDay[c] = (int)b.GetDouble("levy_peace");
                if (b.GetBool("legion")) _legion.Add(c);
                _legionPool[c] = b.GetDouble("legion_pool");
            }
        foreach (var b in root.GetAll("company"))
            if (_game.World.GetCountry(b.GetString("owner")) is { } o && Mercenaries.TryGetValue(b.GetString("def") ?? "", out var def))
                Companies.Add(new MercenaryCompany { Owner = o, Def = def, HiredDay = (int)b.GetDouble("day") });
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 61;
        foreach (var c in _mobilisedDay.Keys) h += c.Index * 7L;
        foreach (var c in _levies) h += c.Index * 13L;
        return h * 31 + Companies.Count;
    }
}

internal static class RaisingExtensions
{
    /// <summary>Divisions of a country that could be sent (not volunteers, not in battle).</summary>
    public static int IdleDivisions(this Country c, Game g) => g.World.Divisions.Count(d => d.Owner == c && d.VolunteerFrom == null && !d.InBattle);
}
