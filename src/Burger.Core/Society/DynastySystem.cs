using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Society;

/// <summary>An illness from <c>common/diseases/*.txt</c> (injury, plague, fever ...).</summary>
public sealed class DiseaseDef
{
    public string Key = "", Name = "", Desc = "";
    /// <summary>Relative chance among the illnesses.</summary>
    public double Weight = 1;
    /// <summary>Months it lasts (0 = the define disease_months, 6).</summary>
    public int Months;
    /// <summary>Chance of dying of it (0 = the define disease_death_chance, 0.30).</summary>
    public double DeathChance;
    public int Order;
}

/// <summary>What a family has earned: its prestige.</summary>
public sealed class DynastyState
{
    public string Family = "";
    public double Prestige;
}

/// <summary>
/// Dynasties (dynasties feature): families that earn prestige while they rule; bastards and concubines in the household;
/// illness and injury that may kill a character. The ruler's family is the dynasty of a country.
/// </summary>
public sealed class DynastySystem
{
    private readonly Game _game;
    public readonly List<DiseaseDef> Diseases = new();
    private readonly Dictionary<string, DynastyState> _dynasties = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Counts for reports.</summary>
    public int BastardsBorn, ConcubinesTaken, Illnesses, IllnessDeaths;

    public const string Bastard = "bastard", Concubine = "concubine", ConcubineChild = "concubine_child", Sick = "sick";

    public DynastySystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "dynasties", Write = Save, Read = Load, Reset = () => _dynasties.Clear() });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Dynasties) && _game.Features.Has(FeatureSet.Characters);
    private SocietyRules Rules => _game.Rules.Society;

    internal void Subscribe(GameEvents hub) => hub.RulerChanged += OnRulerChanged;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Diseases.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/diseases"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Diseases.Add(new DiseaseDef
                {
                    Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!,
                    Weight = n.GetDouble("weight", 1), Months = n.GetInt("months", 0), DeathChance = n.GetDouble("death_chance", 0), Order = Diseases.Count,
                });
            }
    }

    // ------------------------------------------------------------------------------------- household
    public bool IsBastard(Character c) => c.Vars.HasFlag(Bastard);
    public bool IsConcubine(Character c) => c.Vars.HasFlag(Concubine);
    public bool IsConcubineChild(Character c) => c.Vars.HasFlag(ConcubineChild);
    public bool IsSick(Character c) => c.Vars.HasFlag(Sick);

    /// <summary>Claim to the throne: 1 for the born heirs of a marriage, half for bastards and children of concubines, none for a concubine herself.</summary>
    public double Claim(Character c) => IsConcubine(c) ? 0 : IsBastard(c) || IsConcubineChild(c) ? 0.5 : 1;

    public string ClaimLabel(Character c) => IsConcubine(c) ? "Concubine" : IsBastard(c) ? "Bastard" : IsConcubineChild(c) ? "Child of a concubine" : "Legitimate";

    public DiseaseDef? DiseaseOf(Character c) => IsSick(c) && Diseases.Count > 0 ? Diseases[(int)Math.Clamp(c.Vars.Get("sick_disease"), 0, Diseases.Count - 1)] : null;
    public int SickUntil(Character c) => (int)c.Vars.Get("sick_until");

    public IEnumerable<Character> Concubines(Character ruler)
        => _game.World.Characters.Where(c => c.Alive && c.Vars.HasFlag(Concubine) && (int)c.Vars.Get("concubine_of") == ruler.Id);

    public bool ConcubinageAllowed(Country c) => _game.Politics.LawsEnabled && _game.Politics.HasLaw(c, "concubinage");

    // ------------------------------------------------------------------------------------- dynasty
    public DynastyState StateOf(string family)
    {
        if (!_dynasties.TryGetValue(family, out var d)) _dynasties[family] = d = new DynastyState { Family = family };
        return d;
    }

    public DynastyState? DynastyOf(Country c) => c.Ruler is { Family.Length: > 0 } r ? StateOf(r.Family) : null;

    public IEnumerable<Character> Members(string family) => _game.World.Characters.Where(c => c.Alive && c.Family == family && !IsConcubine(c));

    /// <summary>The living members of a family grouped by the country they live in: its houses.</summary>
    public List<(Country? Country, List<Character> Members)> Houses(string family)
        => Members(family).GroupBy(c => c.Country).Select(g => (g.Key, g.OrderByDescending(m => m.Age(_game.World.Date)).ToList()))
            .OrderByDescending(h => h.Item2.Any(m => m.IsRuler)).ThenByDescending(h => h.Item2.Count).ToList();

    /// <summary>Thrones a family holds.</summary>
    public int Thrones(string family) => _game.World.Countries.Count(c => c.Exists && c.Ruler is { } r && r.Family == family);

    /// <summary>A character's standing, used by elections: stats, age, rank and the prestige of the family.</summary>
    public double Prestige(Character c)
    {
        double v = _game.Data.Defines.CharacterStats.Sum(s => c.GetStat(s)) * 2 + Math.Min(10, c.Age(_game.World.Date) / 5.0);
        if (c.IsRuler) v += 20; else if (c.IsHeir) v += 10;
        if (c.Family.Length > 0 && _dynasties.TryGetValue(c.Family, out var d)) v += d.Prestige / 100;
        return v;
    }

    public void AddPrestige(string family, double amount)
    {
        if (!Enabled || family.Length == 0 || amount == 0) return;
        var d = StateOf(family);
        d.Prestige += amount;
    }

    private void OnRulerChanged(Country c, Character? old, Character now)
    {
        if (!Enabled || now.Family.Length == 0) return;
        AddPrestige(now.Family, Rules.PrestigePerRuler(_game));
    }

    /// <summary>A child was born: a ruler's child adds an event's worth of prestige to the family.</summary>
    internal void OnBirth(Character child)
    {
        if (!Enabled || child.Family.Length == 0) return;
        if (child.Father is { IsRuler: true } || child.Mother is { IsRuler: true }) AddPrestige(child.Family, Rules.PrestigePerEvent(_game));
    }

    // ------------------------------------------------------------------------------------- yearly: thrones, household, illness
    public void YearlyTick()
    {
        if (!Enabled) return;
        var rng = _game.Rngs.Characters;
        foreach (var fam in _game.World.Countries.Where(c => c.Exists && c.Ruler != null).Select(c => c.Ruler!.Family).Where(f => f.Length > 0))
            AddPrestige(fam, Rules.PrestigePerThroneYear(_game));
        foreach (var c in _game.World.Countries.Where(c => c.Exists).ToList())
        {
            if (c.Ruler is not { Alive: true } ruler || ruler.Female) continue;
            int age = ruler.Age(_game.World.Date);
            if (age < _game.Data.Defines.AdultAge || age > 60) continue;
            if (ConcubinageAllowed(c))
            {
                if (Concubines(ruler).Count() < 3 && rng.NextDouble() < Rules.ConcubineChance(_game)) TakeConcubine(c, ruler);
                foreach (var con in Concubines(ruler).ToList())
                    if (con.Age(_game.World.Date) <= _game.Data.Defines.MaxFertileAge && rng.NextDouble() < Rules.ConcubineBirthChance(_game)) Born(c, ruler, con, ConcubineChild);
            }
            else if (ruler.Children.Count(x => IsBastard(x)) < 4 && rng.NextDouble() < Rules.BastardChance(_game))
            {
                var lover = _game.Characters.Generate(c, true, 18 + rng.Next(15));
                lover.Vars.SetFlag("lover");
                Born(c, ruler, lover, Bastard);
            }
        }
        foreach (var ch in _game.World.Characters.Where(x => x.Alive && !IsSick(x) && x.Country != null).ToList())
            if (rng.NextDouble() < Rules.DiseaseChance(_game, ch)) Fall(ch);
    }

    private void TakeConcubine(Country c, Character ruler)
    {
        var con = _game.Characters.Generate(c, true, 18 + _game.Rngs.Characters.Next(12));
        con.Vars.SetFlag(Concubine);
        con.Vars.Set("concubine_of", ruler.Id);
        ConcubinesTaken++;
        if (c.IsPlayer) _game.Notify($"{ruler.DisplayName} takes {con.DisplayName} as a concubine.", NotificationKind.Character);
    }

    private Character Born(Country c, Character father, Character mother, string flag)
    {
        var rng = _game.Rngs.Characters;
        var child = new Character { Country = c, Female = rng.NextDouble() < 0.5, Mother = mother, Father = father, BirthDate = _game.World.Date, Family = father.Family };
        child.FirstName = _game.Characters.RandomName(c.Names, child.Female);
        foreach (var stat in _game.Data.Defines.CharacterStats)
            child.Stats[stat] = Math.Clamp(Math.Round(((father.Stats.GetValueOrDefault(stat)) + mother.Stats.GetValueOrDefault(stat)) / 2 + rng.Next(-2, 3)), 0, 20);
        _game.Characters.Register(child);
        child.Vars.SetFlag(flag);
        BastardsBorn++;
        father.Children.Add(child);
        mother.Children.Add(child);
        if (c.IsPlayer) _game.Notify($"{child.DisplayName}, {(flag == Bastard ? "a bastard" : "a child of the household")}, was born to {father.DisplayName}.", NotificationKind.Character);
        return child;
    }

    private void Fall(Character ch)
    {
        if (Diseases.Count == 0) return;
        var rng = _game.Rngs.Characters;
        double total = Diseases.Sum(d => d.Weight), r = rng.NextDouble() * total;
        var pick = Diseases[^1];
        foreach (var d in Diseases) { r -= d.Weight; if (r <= 0) { pick = d; break; } }
        int months = pick.Months > 0 ? pick.Months : (int)_game.Data.Defines.Get("disease_months", 6);
        ch.Vars.SetFlag(Sick);
        ch.Vars.Set("sick_disease", pick.Order);
        ch.Vars.Set("sick_until", _game.World.Date.TotalDays + months * 30);
        Illnesses++;
        if (ch.Country?.IsPlayer == true && (ch.IsRuler || ch.IsHeir || ch.Country.Ruler?.Spouse == ch))
            _game.Notify($"{ch.DisplayName} is struck by {pick.Name.ToLowerInvariant()}.", NotificationKind.Bad);
    }

    /// <summary>Monthly: an illness that has run its time kills or is survived.</summary>
    public void MonthlyTick()
    {
        if (!Enabled) return;
        int today = _game.World.Date.TotalDays;
        foreach (var ch in _game.World.Characters.Where(x => x.Alive && IsSick(x) && SickUntil(x) <= today).ToList())
        {
            var disease = DiseaseOf(ch) ?? new DiseaseDef { Name = "illness" };
            ch.Vars.ClearFlag(Sick);
            if (_game.Rngs.Characters.NextDouble() < Rules.DiseaseDeathChance(_game, ch, disease)) { IllnessDeaths++; _game.Characters.Kill(ch, disease.Name.ToLowerInvariant()); }
            else if (ch.Country?.IsPlayer == true && (ch.IsRuler || ch.IsHeir)) _game.Notify($"{ch.DisplayName} recovers from {disease.Name.ToLowerInvariant()}.", NotificationKind.Good);
        }
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var d in _dynasties.Values.OrderBy(x => x.Family, StringComparer.Ordinal))
        {
            var n = root.AddBlock("dynasty");
            n.Add("family", d.Family, quoted: true);
            n.Add("prestige", F(d.Prestige));
        }
    }

    private void Load(Node root)
    {
        _dynasties.Clear();
        foreach (var b in root.GetAll("dynasty"))
        {
            string fam = b.GetString("family") ?? "";
            if (fam.Length == 0) continue;
            var d = new DynastyState { Family = fam, Prestige = b.GetDouble("prestige") };
            _dynasties[fam] = d;
        }
    }

    /// <summary>Hash of prestige for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 37;
        foreach (var d in _dynasties.Values.OrderBy(x => x.Family, StringComparer.Ordinal)) { h = h * 31 + (long)Math.Round(d.Prestige); }
        return h;
    }
}
