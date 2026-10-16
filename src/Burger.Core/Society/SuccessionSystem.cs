using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Society;

/// <summary>A succession law from <c>common/succession_laws/*.txt</c>.</summary>
public sealed class SuccessionLawDef
{
    public string Key = "", Name = "", Desc = "";
    /// <summary>Who inherits, in turn: eldest_son, eldest_daughter, youngest_son, youngest_daughter, eldest_child, youngest_child, brother, youngest_brother, sister,
    /// nephew, grandchild, uncle, dynasty. Bastards and then the rest of the family follow whatever is listed.</summary>
    public List<string> Order = new();
    /// <summary>A council of the first ruling-class characters votes instead of the order (electors, 5).</summary>
    public bool Elective;
    /// <summary>The realm is shared between the sons (gavelkind).</summary>
    public bool Split;
    /// <summary>The law can only be used while this is true (country scope).</summary>
    public Node? Requires;
}

/// <summary>
/// Succession (succession feature): the law that picks the heir and the next ruler (primogeniture, ultimogeniture, election, gavelkind),
/// regents for rulers under age, and the realm that is divided among sons under gavelkind.
/// </summary>
public sealed class SuccessionSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, SuccessionLawDef> Laws = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, int> _lastSuccession = new();
    private readonly Dictionary<Country, int> _regencySince = new();

    public const string LawGroupKey = "succession";

    /// <summary>Counts for reports.</summary>
    public int Successions, Regencies, Partitions;
    public readonly Dictionary<string, int> SuccessionsByLaw = new(StringComparer.OrdinalIgnoreCase);

    public SuccessionSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "succession", Write = Save, Read = Load, Reset = () => { _lastSuccession.Clear(); _regencySince.Clear(); } });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Succession) && _game.Features.Has(FeatureSet.Characters) && Laws.Count > 0;
    private SocietyRules Rules => _game.Rules.Society;
    private DynastySystem Dyn => _game.Dynasties;

    private readonly Dictionary<Country, Character> _dying = new();

    internal void Subscribe(GameEvents hub)
    {
        hub.RulerChanged += OnRulerChanged;
        // the engine clears the throne before it crowns the next ruler, so the one who died is noted here
        hub.CharacterDied += (ch, _) => { if (ch.Country is { } c && c.Ruler == ch) _dying[c] = ch; };
    }

    public void LoadDefinitions(GameData data)
    {
        Laws.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/succession_laws"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Laws[n.Key] = new SuccessionLawDef
                {
                    Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!,
                    Order = (n.Get("order")?.Items().ToList() ?? new List<string>()), Elective = n.GetBool("elective"), Split = n.GetBool("split"), Requires = n.Get("requires"),
                };
            }
    }

    // ------------------------------------------------------------------------------------- the law
    /// <summary>The law in force: the succession law group (laws feature) if the game has one, else the country's succession_law, else primogeniture.</summary>
    public SuccessionLawDef LawOf(Country c)
    {
        string? key = null;
        if (_game.Politics.LawsEnabled && _game.Politics.LawGroups.FirstOrDefault(g => g.Key == LawGroupKey) is { } g && _game.Politics.LawOf(c, g) is { } l) key = l.Key;
        key ??= c.Definition.GetString("succession_law");
        if (key != null && Laws.TryGetValue(key, out var def) && (def.Requires == null || _game.Script.EvalTrigger(def.Requires, _game.Context(c)))) return def;
        return Laws.GetValueOrDefault("primogeniture") ?? Laws.Values.First();
    }

    public double YearsSinceSuccession(Country c) => _lastSuccession.TryGetValue(c, out var d) ? (_game.World.Date.TotalDays - d) / 365.0 : 99;

    // ------------------------------------------------------------------------------------- who comes next
    private bool Eligible(Country c, Character x, Character? exclude)
        => x.Alive && x != exclude && x.Country == c && !Dyn.IsConcubine(x) && !x.Vars.HasFlag("lover");

    private int Age(Character x) => x.Age(_game.World.Date);

    /// <summary>The order of heirs to <paramref name="ruler"/> under the law of <paramref name="c"/>, best claim first. Evaluated lazily.</summary>
    public IEnumerable<Character> Line(Country c, Character? ruler = null)
    {
        ruler ??= c.Ruler;
        var law = LawOf(c);
        if (law.Elective) return ElectiveLine(c, ruler);
        return OrderedLine(c, ruler, law);
    }

    private IEnumerable<Character> OrderedLine(Country c, Character? ruler, SuccessionLawDef law)
    {
        var seen = new HashSet<Character>();
        if (ruler != null)
        {
            foreach (var step in law.Order)
                foreach (var x in Step(c, ruler, step))
                    if (seen.Add(x)) yield return x;
            // children born outside marriage come after every lawful heir
            foreach (var x in ruler.Children.Where(k => Eligible(c, k, ruler) && Dyn.Claim(k) < 1).OrderByDescending(Age))
                if (seen.Add(x)) yield return x;
            foreach (var x in Dyn.Members(ruler.Family).Where(k => Eligible(c, k, ruler) && Age(k) >= _game.Data.Defines.AdultAge).OrderByDescending(Dyn.Prestige))
                if (seen.Add(x)) yield return x;
        }
        foreach (var x in _game.World.CharactersOf(c).Where(k => Eligible(c, k, ruler) && Age(k) >= _game.Data.Defines.AdultAge).OrderByDescending(Dyn.Prestige))
            if (seen.Add(x)) yield return x;
    }

    private IEnumerable<Character> Step(Country c, Character ruler, string step)
    {
        IEnumerable<Character> legit(IEnumerable<Character> src) => src.Where(k => Eligible(c, k, ruler) && Dyn.Claim(k) >= 1);
        IEnumerable<Character> sibs() => new[] { ruler.Father, ruler.Mother }.Where(p => p != null).SelectMany(p => p!.Children).Distinct().Where(k => k != ruler);
        IEnumerable<Character> grand() => ruler.Children.Where(k => !k.Alive).OrderBy(k => k.BirthDate.TotalDays).SelectMany(k => legit(k.Children).OrderByDescending(Age));
        switch (step.ToLowerInvariant())
        {
            case "eldest_son": return legit(ruler.Children).Where(k => !k.Female).OrderByDescending(Age);
            case "eldest_daughter": return legit(ruler.Children).Where(k => k.Female).OrderByDescending(Age);
            case "youngest_son": return legit(ruler.Children).Where(k => !k.Female).OrderBy(Age);
            case "youngest_daughter": return legit(ruler.Children).Where(k => k.Female).OrderBy(Age);
            case "eldest_child": return legit(ruler.Children).OrderByDescending(Age);
            case "youngest_child": return legit(ruler.Children).OrderBy(Age);
            case "grandchild": return grand();
            case "brother": return legit(sibs()).Where(k => !k.Female).OrderByDescending(Age);
            case "youngest_brother": return legit(sibs()).Where(k => !k.Female).OrderBy(Age);
            case "sister": return legit(sibs()).Where(k => k.Female).OrderByDescending(Age);
            case "nephew": return legit(sibs().SelectMany(k => k.Children)).OrderByDescending(Age);
            case "uncle": return legit(new[] { ruler.Father, ruler.Mother }.Where(p => p != null).SelectMany(p => new[] { p!.Father, p.Mother }).Where(g => g != null).SelectMany(g => g!.Children)).Where(k => k != ruler.Father && k != ruler.Mother).OrderByDescending(Age);
            case "dynasty": return Dyn.Members(ruler.Family).Where(k => Eligible(c, k, ruler)).OrderByDescending(Dyn.Prestige);
            default: Log.ErrorOnce("succ:" + step, $"Unknown succession step '{step}'"); return Array.Empty<Character>();
        }
    }

    /// <summary>The council of an elective crown: the first ruling-class characters of the country.</summary>
    public List<Character> Electors(Country c, Character? ruler)
        => _game.World.CharactersOf(c).Where(k => Eligible(c, k, ruler) && Age(k) >= _game.Data.Defines.AdultAge).OrderByDescending(Dyn.Prestige).ThenBy(k => k.Id).Take(Rules.ElectorCount(_game)).ToList();

    /// <summary>How well an elector thinks of a candidate: family, charisma and the loyalty the candidate has built.</summary>
    public double Opinion(Character elector, Character cand)
    {
        double v = (cand.GetStat("charisma") - 5) * 3 + (cand.Loyalty - 50) / 5;
        if (elector == cand) v += 10;
        if (elector.Family.Length > 0 && elector.Family == cand.Family) v += 15;
        if (cand.Father == elector || cand.Mother == elector || elector.Father == cand.Father && cand.Father != null) v += 10;
        return v;
    }

    public List<(Character Candidate, int Votes, double Score)> Vote(Country c, Character? ruler)
    {
        var electors = Electors(c, ruler);
        int adult = _game.Data.Defines.AdultAge;
        var cands = (ruler != null ? Dyn.Members(ruler.Family).Where(k => Eligible(c, k, ruler) && Age(k) >= adult && Dyn.Claim(k) > 0).ToList() : new List<Character>());
        if (cands.Count == 0) cands = electors.ToList();
        var votes = cands.ToDictionary(k => k, _ => 0);
        double Score(Character e, Character k) => Dyn.Prestige(k) + Opinion(e, k) + Dyn.Claim(k) * 10;
        foreach (var e in electors)
            if (cands.Count > 0) votes[cands.OrderByDescending(k => Score(e, k)).ThenBy(k => k.Id).First()]++;
        return cands.Select(k => (k, votes[k], Dyn.Prestige(k))).OrderByDescending(t => t.Item2).ThenByDescending(t => t.Item3).ThenBy(t => t.k.Id).ToList();
    }

    private IEnumerable<Character> ElectiveLine(Country c, Character? ruler)
    {
        foreach (var (cand, _, _) in Vote(c, ruler)) yield return cand;
    }

    /// <summary>The next ruler (CharacterRules.ChooseSuccessor): the first of the line. Null when nobody is left (the engine makes one up).</summary>
    public Character? Choose(Country c, Character? old)
    {
        if (old == null) return Line(c, null).FirstOrDefault(k => Age(k) >= _game.Data.Defines.AdultAge);
        return Line(c, old).FirstOrDefault();
    }

    /// <summary>The heir of the current ruler under the law (CharacterRules.ChooseHeir).</summary>
    public Character? HeirOf(Country c) => c.Ruler == null ? null : Line(c, c.Ruler).FirstOrDefault();

    // ------------------------------------------------------------------------------------- rulers and regents
    private void OnRulerChanged(Country c, Character? old, Character now)
    {
        if (_dying.Remove(c, out var dead)) old ??= dead;
        if (!Enabled) return;
        _lastSuccession[c] = _game.World.Date.TotalDays;
        Successions++;
        SuccessionsByLaw[LawOf(c).Key] = SuccessionsByLaw.GetValueOrDefault(LawOf(c).Key) + 1;
        c.Regent = null;
        _regencySince.Remove(c);
        if (Age(now) < Rules.RegencyAge(_game)) Appoint(c, now);
        if (old != null && !old.Alive && LawOf(c).Split) Partition(c, old, now);
        c.Heir = HeirOf(c);
    }

    private void Appoint(Country c, Character ruler)
    {
        var adult = _game.Data.Defines.AdultAge;
        var family = new[] { ruler.Mother, ruler.Father }.Concat(ruler.Father?.Children ?? new List<Character>()).Concat(ruler.Mother?.Children ?? new List<Character>())
            .Where(x => x != null && x != ruler).Cast<Character>().Distinct().Where(x => x.Alive && Age(x) >= adult && !Dyn.IsConcubine(x));
        var regent = family.OrderByDescending(Stats).FirstOrDefault()
                     ?? _game.World.CharactersOf(c).Where(x => Eligible(c, x, ruler) && Age(x) >= adult).OrderByDescending(Stats).FirstOrDefault();
        if (regent == null) return;
        regent.Country ??= c;
        c.Regent = regent;
        _regencySince[c] = _game.World.Date.TotalDays;
        Regencies++;
        _game.NotifyPlayer($"{ruler.DisplayName} is a child: {regent.DisplayName} rules as regent of {c.Name}.", NotificationKind.Character, c);
        _game.Events.FireOnAction("on_regency_started", c);
    }

    private double Stats(Character x) => _game.Data.Defines.CharacterStats.Sum(s => x.GetStat(s));

    public bool InRegency(Country c) => c.Regent != null;
    public int RegencyEndsIn(Country c) => c.Ruler == null ? 0 : Math.Max(0, Rules.RegencyAge(_game) - Age(c.Ruler));

    /// <summary>Country modifiers while a regent rules; the modifier system asks for it.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country { Regent: not null } || !key.Equals("stability_target", StringComparison.OrdinalIgnoreCase)) return 0;
        return Rules.RegencyStability(_game);
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            if (c.Ruler is { Alive: true } r)
            {
                if (c.Regent != null && (!c.Regent.Alive || Age(r) >= Rules.RegencyAge(_game)))
                {
                    bool grown = c.Regent.Alive;
                    c.Regent = null;
                    _regencySince.Remove(c);
                    if (grown) _game.NotifyPlayer($"{r.DisplayName} comes of age and takes the rule of {c.Name}.", NotificationKind.Good, c);
                }
                if (c.Regent == null && Age(r) < Rules.RegencyAge(_game)) Appoint(c, r);
            }
            // the heir always follows the law, which can change
            var heir = HeirOf(c);
            if (heir != null && c.Heir != heir) c.Heir = heir;
        }
    }

    // ------------------------------------------------------------------------------------- gavelkind
    /// <summary>Under a split law the sons share the realm: the heir keeps the capital and a share of the land, each other son gets a share and rules it as a subject.</summary>
    private void Partition(Country c, Character old, Character heir)
    {
        var sons = old.Children.Where(k => Eligible(c, k, old) && k != heir && !k.Female && Dyn.Claim(k) >= 1).OrderByDescending(Age).ToList();
        var capital = c.Capital?.State ?? c.OwnedStates.FirstOrDefault();
        if (sons.Count == 0 || capital == null) return;
        sons = sons.Take(Math.Max(0, c.OwnedStates.Count - 1)).ToList();
        if (sons.Count == 0) return;
        // equal parts: every state, richest first, goes to the share that has the least so far (the heir's share starts with the capital)
        var shares = sons.Select(x => (Son: x, States: new List<State>(), Tax: 0.0)).ToList();
        double heirTax = capital.Tax;
        foreach (var st in c.OwnedStates.Where(s => s != capital).OrderByDescending(s => s.Tax).ThenBy(s => s.Key, StringComparer.Ordinal))
        {
            int pick = -1;
            for (int i = 0; i < shares.Count; i++) if (pick < 0 || shares[i].Tax < shares[pick].Tax) pick = i;
            if (shares[pick].Tax <= heirTax)
            {
                shares[pick].States.Add(st);
                shares[pick] = (shares[pick].Son, shares[pick].States, shares[pick].Tax + st.Tax);
            }
            else heirTax += st.Tax;
        }
        var type = _game.Data.SubjectTypes.TryGetValue("vassal", out var v) ? v : _game.Data.SubjectTypes.TryGetValue("client_state", out var cs) ? cs : _game.Data.SubjectTypes.Values.First();
        foreach (var (son, states, _) in shares.Where(x => x.States.Count > 0))
        {
            string tag = FreeTag();
            var best = states.MaxBy(s => s.Tax)!;
            var realm = _game.World.CreateCountry(tag, $"{best.DisplayName} ({son.FirstName})", c.Color.Lerp(Rgb.White, 0.25), c.FlagPath, c.Government);
            realm.Origin = c.Tag;
            realm.Playable = false;
            realm.Names = c.Names;
            foreach (var s in states) { _game.Diplomacy.TransferState(s, realm); s.Cores.Add(realm); }
            realm.Capital = best.Locations.FirstOrDefault(l => l.IsLand && l.IsPassable) ?? best.Locations[0];
            son.Country = realm;
            foreach (var kid in son.Children.Where(k => k.Alive && k.Country == c)) kid.Country = realm;
            if (son.Spouse is { Alive: true } sp && sp.Country == c) sp.Country = realm;
            realm.Ruler = son;
            if (_game.Politics.Enabled) _game.Politics.SetRulingParty(realm, _game.Politics.Of(c).Ruling, "gavelkind");
            _game.Diplomacy.MakeSubject(realm, c, type);
            _game.NotifyPlayer($"{son.DisplayName} inherits {best.DisplayName} and rules it under {heir.DisplayName}.", NotificationKind.Character, c);
        }
        Partitions++;
        _game.Events.FireOnAction("on_realm_partitioned", c);
        _game.RaiseMapChanged();
    }

    private string FreeTag()
    {
        for (char a = 'H'; a >= 'A'; a--)
            for (int i = 0; i < 100; i++)
            {
                string t = $"{a}{i:00}";
                if (_game.World.GetCountry(t) == null) return t;
            }
        for (int i = 0; i < 46656; i++) { string t = $"Z{i % 36:X1}{i / 36 % 36:X1}"; if (_game.World.GetCountry(t) == null) return t; }
        return "ZZZ";
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        _game.Script.RegisterValue("years_since_succession", (ctx, scope) => scope is Country c && Enabled ? YearsSinceSuccession(c) : null);
        _game.Script.RegisterTrigger("in_regency", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { Regent: not null }, (ctx, n) => "Is ruled by a regent");
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        foreach (var kv in _lastSuccession.OrderBy(k => k.Key.Tag, StringComparer.Ordinal))
        {
            var n = root.AddBlock("country");
            n.Add("tag", kv.Key.Tag);
            n.Add("day", kv.Value.ToString(CultureInfo.InvariantCulture));
            if (kv.Key.Regent != null) n.Add("regent", kv.Key.Regent.Key, quoted: true);
        }
        foreach (var c in _game.World.Countries.Where(c => c.Regent != null && !_lastSuccession.ContainsKey(c)))
        {
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            n.Add("regent", c.Regent!.Key, quoted: true);
        }
    }

    private void Load(Node root)
    {
        _lastSuccession.Clear(); _regencySince.Clear();
        foreach (var b in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(b.GetString("tag"));
            if (c == null) continue;
            if (b.Has("day")) _lastSuccession[c] = (int)b.GetDouble("day");
            if (b.GetString("regent") is { } rk && _game.World.GetCharacter(rk) is { Alive: true } reg) c.Regent = reg;
        }
    }

    /// <summary>Hash of regencies and the last successions for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 41;
        foreach (var c in _game.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            if (c.Regent != null) h = h * 31 + c.Regent.Id;
            if (_lastSuccession.TryGetValue(c, out var d)) h = h * 31 + d;
        }
        return h;
    }
}
