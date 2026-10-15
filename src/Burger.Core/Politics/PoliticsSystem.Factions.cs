using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>
/// Internal party factions. A faction is the existing <c>party_faction</c> generic scope under a party; nothing new is
/// created for it. Ownership of each value:
/// <list type="bullet">
/// <item>Static preferences (scenario data, <see cref="GenericScope.Attributes"/>): <c>weight</c> (relative size, default 1),
/// <c>likes</c> / <c>dislikes</c> (law keys, space separated) and <c>post</c> (cabinet post it wants).</item>
/// <item>Dynamic state (<see cref="GenericScope.Vars"/>, saved with the generic scopes): <c>loyalty</c> (0-100, default
/// <see cref="StartLoyalty"/>) and <c>grievance</c> (0-100, default 0). Nothing is copied into <see cref="CountryPolitics"/>.</item>
/// <item>Derived, never stored: influence (weight x loyalty, as a share of the party), party cohesion (influence-weighted
/// loyalty), law resistance, standing grievance.</item>
/// <item>A character's membership: flags <c>org_&lt;party key&gt;</c> and <c>faction_&lt;faction key&gt;</c> (see <see cref="SetAffiliation"/>);
/// the legacy <c>party_&lt;ideology&gt;</c> flag is rewritten from it.</item>
/// </list>
/// Grievance only ever moves towards a bounded "standing" value (laws in force a faction dislikes, cabinet posts it wants)
/// plus one-off shocks that decay, and loyalty follows grievance, so the loop converges instead of running away.
/// </summary>
public sealed partial class PoliticsSystem
{
    public const double StartLoyalty = 60;
    private const string LoyaltyVar = "loyalty", GrievanceVar = "grievance", PlacatedVar = "placated_day";
    private const string OrgFlag = "org_", FactionFlag = "faction_";

    private double Def(string key, double fallback) => _game.Data.Defines.Get(key, fallback);

    public IEnumerable<GenericScope> FactionsOf(GenericScope party) => PartyUnitsOf(party, PartyFactionKind);

    public double FactionLoyalty(GenericScope f) => Math.Clamp(f.Vars.Get(LoyaltyVar, StartLoyalty), 0, 100);
    public double FactionGrievance(GenericScope f) => Math.Clamp(f.Vars.Get(GrievanceVar, 0), 0, 100);

    private void AddGrievance(GenericScope f, double amount)
        => f.Vars.Set(GrievanceVar, Math.Clamp(FactionGrievance(f) + amount, 0, 100));

    private double FactionWeight(GenericScope f)
        => f.Attributes.TryGetValue("weight", out var raw)
           && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
           && double.IsFinite(w) && w > 0 ? w : 1;

    private static IEnumerable<string> Keys(GenericScope f, string attribute)
        => f.Attributes.TryGetValue(attribute, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries) : Enumerable.Empty<string>();

    private double AttributeNumber(GenericScope f, string attribute, double fallback)
        => f.Attributes.TryGetValue(attribute, out var raw)
           && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
           && double.IsFinite(v) ? v : fallback;

    /// <summary>Attribute <c>hardline</c> (0-1, default 0.5): how it feels about repression. Above 0.5 it applauds crackdowns and resents concessions; below, the reverse.</summary>
    public double FactionHardline(GenericScope f) => Math.Clamp(AttributeNumber(f, "hardline", 0.5), 0, 1);

    /// <summary>Attribute <c>tolerance</c> (default 1, 0.3-2): how much it puts up with. Low tolerance turns the same grievance into lost loyalty faster.</summary>
    public double FactionTolerance(GenericScope f) => Math.Clamp(AttributeNumber(f, "tolerance", 1), 0.3, 2);

    public IReadOnlyList<string> FactionLikes(GenericScope f) => Keys(f, "likes").ToList();
    public IReadOnlyList<string> FactionDislikes(GenericScope f) => Keys(f, "dislikes").ToList();
    public string? FactionPost(GenericScope f) => f.Attributes.TryGetValue("post", out var p) && !string.IsNullOrWhiteSpace(p) ? p.Trim() : null;

    /// <summary>Share of its party a faction commands (0-1): weight scaled by loyalty, so a disaffected faction counts for less.</summary>
    public double FactionInfluence(GenericScope f)
    {
        if (_game.World.ScopeHierarchy.ParentOf(f) is not GenericScope party || !IsParty(party)) return 0;
        double Power(GenericScope x) => FactionWeight(x) * (0.5 + FactionLoyalty(x) / 100);
        double total = FactionsOf(party).Sum(Power);
        return total > 1e-9 ? Power(f) / total : 0;
    }

    /// <summary>How united a party is (0-100): influence-weighted loyalty of its factions. A party without factions is united.</summary>
    public double PartyCohesion(GenericScope party)
    {
        var factions = FactionsOf(party).ToList();
        return factions.Count == 0 ? 100 : factions.Sum(f => FactionInfluence(f) * FactionLoyalty(f));
    }

    /// <summary>Seat-weighted cohesion of the organizations in government (100 when none has factions).</summary>
    public double GovernmentCohesion(Country c)
    {
        var p = Of(c);
        double seats = 0, sum = 0;
        foreach (var key in p.GovernmentOrganizations)
        {
            if (_game.World.GetGenericScope(key) is not { } party || !IsParty(party) || FactionsOf(party).Any() == false) continue;
            double s = Math.Max(1, p.PartySeats.GetValueOrDefault(key));
            seats += s;
            sum += s * PartyCohesion(party);
        }
        return seats > 0 ? sum / seats : 100;
    }

    /// <summary>Parties in government that have factions, with each party's weight in the government (its seat share).</summary>
    private IEnumerable<(GenericScope Party, double Weight)> GoverningParties(Country c)
    {
        var p = Of(c);
        var parties = p.GovernmentOrganizations.Select(k => _game.World.GetGenericScope(k)).Where(IsParty).Cast<GenericScope>()
            .Where(party => FactionsOf(party).Any()).ToList();
        double total = parties.Sum(x => Math.Max(1, p.PartySeats.GetValueOrDefault(x.Key)));
        foreach (var party in parties) yield return (party, Math.Max(1, p.PartySeats.GetValueOrDefault(party.Key)) / Math.Max(1, total));
    }

    // ------------------------------------------------------------------------------------- laws

    /// <summary>Share (0-1) of the governing factional weight that dislikes / likes a law, and the factions responsible.</summary>
    public (double Opposition, double Support, List<(GenericScope Faction, double Weight, bool Dislikes)> Factions) LawStance(Country c, LawDef law)
    {
        double opposition = 0, support = 0;
        var who = new List<(GenericScope, double, bool)>();
        foreach (var (party, partyWeight) in GoverningParties(c))
            foreach (var f in FactionsOf(party))
            {
                double w = partyWeight * FactionInfluence(f);
                if (FactionDislikes(f).Contains(law.Key, StringComparer.OrdinalIgnoreCase)) { opposition += w; who.Add((f, w, true)); }
                else if (FactionLikes(f).Contains(law.Key, StringComparer.OrdinalIgnoreCase)) { support += w; who.Add((f, w, false)); }
            }
        return (opposition, support, who.OrderByDescending(x => x.Item2).ToList());
    }

    /// <summary>Extra political power it costs to push a law past the governing factions (0 when they do not object).
    /// Supporters offset half of the opposition, so a law the whole party wants is never surcharged.</summary>
    public double FactionSurcharge(Country c, LawDef law)
    {
        if (!LawsEnabled || law.Cost <= 0) return 0;
        var (opposition, support, _) = LawStance(c, law);
        return law.Cost * Math.Max(0, opposition - 0.5 * support) * Def("faction_resistance_cost", 1.0);
    }

    public double EnactCost(Country c, LawDef law) => law.Cost + FactionSurcharge(c, law);

    /// <summary>Why a law meets (or would meet) resistance and what it does to the governing factions, for tooltips.</summary>
    public List<string> ExplainLaw(Country c, LawDef law)
    {
        var lines = new List<string>();
        var (opposition, support, who) = LawStance(c, law);
        foreach (var (f, w, dislikes) in who.Take(4))
            lines.Add($"{(dislikes ? "Opposed" : "Backed")} by {f.DisplayName} ({w:P0} of the government)");
        double surcharge = FactionSurcharge(c, law);
        if (surcharge >= 0.5) lines.Add($"Defying them costs +{surcharge:0} {PowerName} and angers them");
        else if (opposition > 0) lines.Add("Their objection is outweighed by supporters");
        if (LawOf(c, law.Group) is { } current && current != law)
            foreach (var (party, _) in GoverningParties(c))
                foreach (var f in FactionsOf(party).Where(f => FactionLikes(f).Contains(current.Key, StringComparer.OrdinalIgnoreCase)))
                    lines.Add($"Repealing {current.Name} upsets {f.DisplayName}");
        return lines;
    }

    private void ApplyLawReaction(Country c, LawDef law, LawDef? replaced)
    {
        double shock = Def("faction_law_grievance", 15);
        foreach (var (party, _) in GoverningParties(c))
            foreach (var f in FactionsOf(party))
            {
                double delta = 0;
                if (FactionDislikes(f).Contains(law.Key, StringComparer.OrdinalIgnoreCase)) delta += shock;
                if (FactionLikes(f).Contains(law.Key, StringComparer.OrdinalIgnoreCase)) delta -= shock / 2;
                if (replaced != null && FactionLikes(f).Contains(replaced.Key, StringComparer.OrdinalIgnoreCase)) delta += shock / 2;
                if (delta != 0) AddGrievance(f, delta);
            }
    }

    // ------------------------------------------------------------------------------------- characters and cabinet

    /// <summary>The party organization a character belongs to: its <c>org_</c> flag, else the representative party of the legacy ideology flag.</summary>
    public string? OrganizationOf(Character ch, Country? country = null)
    {
        foreach (var flag in ch.Vars.Flags)
            if (flag.StartsWith(OrgFlag, StringComparison.OrdinalIgnoreCase) && _game.World.GetGenericScope(flag[OrgFlag.Length..]) is { } p && IsParty(p))
                return p.Key;
        country ??= ch.Country;
        return MinisterIdeologyFlag(ch) is { } ideology && country != null ? PartyOf(country, ideology)?.Key : null;
    }

    public GenericScope? FactionOfCharacter(Character ch)
    {
        foreach (var flag in ch.Vars.Flags)
            if (flag.StartsWith(FactionFlag, StringComparison.OrdinalIgnoreCase) && _game.World.GetGenericScope(flag[FactionFlag.Length..]) is { } f && IsPartyFaction(f))
                return f;
        return null;
    }

    /// <summary>Makes a character a member of a party (and optionally one of its factions). The only writer of the affiliation flags.</summary>
    public void SetAffiliation(Character ch, string partyKey, string? factionKey = null)
    {
        if (_game.World.GetGenericScope(partyKey) is not { } party || !IsParty(party) || IdeologyOfParty(party) is not { } ideology)
        {
            SetParty(ch, partyKey); // an ideology key: legacy affiliation
            return;
        }
        foreach (var f in ch.Vars.Flags.Where(f => f.StartsWith(OrgFlag, StringComparison.OrdinalIgnoreCase)
                     || f.StartsWith(FactionFlag, StringComparison.OrdinalIgnoreCase)).ToList()) ch.Vars.ClearFlag(f);
        SetParty(ch, ideology);
        ch.Vars.SetFlag(OrgFlag + party.Key);
        if (factionKey != null && _game.World.GetGenericScope(factionKey) is { } faction && IsPartyFaction(faction)
            && ReferenceEquals(_game.World.ScopeHierarchy.ParentOf(faction), party))
            ch.Vars.SetFlag(FactionFlag + faction.Key);
    }

    /// <summary>Whether a character's party is in the government (by organization when known, else by ideology).</summary>
    private bool PartyInGovernmentOf(Country c, Character ch)
    {
        var p = Of(c);
        if (OrganizationOf(ch, c) is { } org) return p.GovernmentOrganizations.Contains(org, StringComparer.OrdinalIgnoreCase);
        return MinisterIdeologyFlag(ch) is { } ideology && InGovernment(c, ideology);
    }

    /// <summary>Explains how appointing a character moves the factions (null when nothing changes).</summary>
    public string? ExplainAppointment(Country c, CabinetPosition pos, Character ch)
    {
        if (FactionOfCharacter(ch) is not { } faction) return null;
        var parts = new List<string> { $"Member of {faction.DisplayName}: it gains a voice in cabinet" };
        if (FactionPost(faction) == pos.Key) parts.Add("it wanted this post");
        foreach (var other in FactionsOf((GenericScope)_game.World.ScopeHierarchy.ParentOf(faction)!)
                     .Where(o => o != faction && FactionPost(o) == pos.Key && !Holds(c, pos, o)))
            parts.Add($"{other.DisplayName} also wanted it");
        return string.Join("; ", parts);
    }

    private bool Holds(Country c, CabinetPosition pos, GenericScope faction)
        => MinisterOf(c, pos) is { } m && ReferenceEquals(FactionOfCharacter(m), faction);

    private void ApplyAppointmentReaction(Country c, CabinetPosition pos, Character ch)
    {
        if (FactionOfCharacter(ch) is not { } faction) return;
        double shock = Def("faction_appoint_grievance", 8);
        AddGrievance(faction, -shock);
        foreach (var other in FactionsOf((GenericScope)_game.World.ScopeHierarchy.ParentOf(faction)!).Where(o => o != faction && FactionPost(o) == pos.Key))
            AddGrievance(other, shock / 2);
    }

    // ------------------------------------------------------------------------------------- compromise

    public string? CannotPlacate(Country c, GenericScope faction)
    {
        if (!IsPartyFaction(faction) || PartyCountry(faction) != c) return "Not a faction of this country";
        if (FactionGrievance(faction) < 5) return "No grievance to settle";
        int cd = (int)Def("faction_placate_cooldown_days", 90);
        if (faction.Vars.Has(PlacatedVar) && faction.Vars.Get(PlacatedVar) + cd > _game.World.Date.TotalDays)
            return $"Settled recently ({(int)(faction.Vars.Get(PlacatedVar) + cd - _game.World.Date.TotalDays)} days left)";
        double cost = Def("faction_placate_cost", 40);
        return CanAfford(c, cost) ? null : $"Needs {cost:0} {PowerName}";
    }

    /// <summary>A negotiated compromise: spend political power to ease one faction's grievance.</summary>
    public string? Placate(Country c, GenericScope faction)
    {
        var reason = CannotPlacate(c, faction);
        if (reason != null) return reason;
        Pay(c, Def("faction_placate_cost", 40));
        AddGrievance(faction, -Def("faction_placate_relief", 25));
        faction.Vars.Set(PlacatedVar, _game.World.Date.TotalDays);
        return null;
    }

    // ------------------------------------------------------------------------------------- monthly dynamics

    /// <summary>Grievance a faction settles at while its party governs: laws in force it dislikes (and likes), cabinet posts it wants.</summary>
    public double StandingGrievance(Country c, GenericScope faction)
    {
        var p = Of(c);
        double standing = 0, perLaw = Def("faction_standing_per_law", 25);
        foreach (var kv in p.Laws)
        {
            if (FactionDislikes(faction).Contains(kv.Value, StringComparer.OrdinalIgnoreCase)) standing += perLaw;
            if (FactionLikes(faction).Contains(kv.Value, StringComparer.OrdinalIgnoreCase)) standing -= perLaw / 2;
        }
        if (FactionPost(faction) is { } postKey && Positions.FirstOrDefault(x => x.Key == postKey) is { } pos && CabinetEnabled)
        {
            var holder = MinisterOf(c, pos);
            if (holder != null && ReferenceEquals(FactionOfCharacter(holder), faction)) standing -= 10;
            else if (holder != null && FactionOfCharacter(holder) != null) standing += 10;
        }
        return Math.Clamp(standing, 0, Def("faction_standing_cap", 80));
    }

    /// <summary>Where a governing faction's loyalty is heading, with the reasons (for the faction tooltip).</summary>
    public (double Target, List<string> Reasons) LoyaltyOutlook(Country c, GenericScope faction)
    {
        var reasons = new List<string>();
        var p = Of(c);
        bool governing = p.GovernmentOrganizations.Contains(PartyKeyOf(faction), StringComparer.OrdinalIgnoreCase);
        double target = StartLoyalty;
        double grievance = FactionGrievance(faction);
        if (grievance > 1) { target -= grievance * 0.5 / FactionTolerance(faction); reasons.Add($"Grievance {grievance:0}" + (FactionTolerance(faction) < 0.8 ? " (short temper)" : FactionTolerance(faction) > 1.25 ? " (patient)" : "")); }
        // a faction shut out of a governing party's cabinet loses faith faster than one with a voice
        if (governing && CabinetEnabled && FactionPost(faction) != null && !p.Ministers.Values.Any(m => ReferenceEquals(FactionOfCharacter(m), faction)))
        { target -= 5; reasons.Add("No voice in cabinet"); }
        if (governing && CabinetEnabled)
            foreach (var m in p.Ministers.Values)
                if (ReferenceEquals(FactionOfCharacter(m), faction)) { target += 10; reasons.Add("Holds a cabinet post"); break; }
        return (Math.Clamp(target, 0, 100), reasons);
    }

    private string PartyKeyOf(GenericScope faction) => _game.World.ScopeHierarchy.ParentOf(faction)?.ScopeKey ?? "";

    public void FactionMonthly(Country c)
    {
        var p = Of(c);
        double ease = Def("faction_ease", 0.12);
        bool changed = false;
        foreach (var party in PartiesOf(c))
        {
            bool governing = p.GovernmentOrganizations.Contains(party.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var f in FactionsOf(party))
            {
                double standing = governing ? StandingGrievance(c, f) : 0;
                double g = FactionGrievance(f);
                double newG = g + (standing - g) * ease;
                f.Vars.Set(GrievanceVar, Math.Clamp(newG, 0, 100));
                var (target, _) = LoyaltyOutlook(c, f);
                double l = FactionLoyalty(f);
                double newL = Math.Clamp(l + (target - l) * 0.15, 0, 100);
                if (Math.Abs(newL - l) > 0.01 || Math.Abs(newG - g) > 0.01) changed = true;
                f.Vars.Set(LoyaltyVar, newL);
                TrackRestlessness(f);
            }
        }
        var leaving = PartiesOf(c).SelectMany(FactionsOf).Where(f => RestlessMonths(f) > 0 && CanSplinter(f))
            .OrderByDescending(FactionInfluence).FirstOrDefault(f => _game.Rngs.Politics.NextDouble() < SplinterChance(c, f));
        if (leaving != null && Splinter(c, leaving) != null) changed = true;
        if (!c.IsPlayer) AiFactions(c);
        if (changed) _modsDirty.Add(c);
    }

    /// <summary>Stability a divided government loses: nothing while its cohesion is above the threshold, then linear and capped.</summary>
    public double InfightingStability(Country c)
    {
        double threshold = Def("party_infighting_threshold", 55);
        double cohesion = GovernmentCohesion(c);
        return cohesion >= threshold ? 0 : -Math.Min(Def("party_infighting_max", 6), (threshold - cohesion) * Def("party_infighting_stability", 0.12));
    }

    /// <summary>
    /// Share of a party's support it keeps at an election (0-1): nothing is lost while its factions are loyal. Only factions whose loyalty has fallen
    /// below party_division_loyalty (50) take their followers with them, in proportion to their influence and how far they have fallen (squared,
    /// so a mild grumble costs next to nothing), up to party_division_votes (35%) when every faction has turned.
    /// </summary>
    public double PartyVoteRetention(GenericScope party)
    {
        double floor = Def("party_division_loyalty", 50), loss = 0;
        foreach (var f in FactionsOf(party))
        {
            double fall = Math.Clamp((floor - FactionLoyalty(f)) / floor, 0, 1);
            loss += FactionInfluence(f) * fall * fall;
        }
        return 1 - Math.Min(1, loss) * Def("party_division_votes", 0.35);
    }

    private double CohesionVoteFactor(string partyKey)
        => _game.World.GetGenericScope(partyKey) is { } party && IsParty(party) && FactionsOf(party).Any() ? PartyVoteRetention(party) : 1;

    public double CollapseCohesion => Def("party_collapse_cohesion", 40);

    /// <summary>
    /// Monthly chance a government falls apart over its factions. Zero while cohesion holds above party_collapse_cohesion (40); below it, the chance
    /// grows with the shortfall, with how narrow the majority is (a thin coalition cannot afford a revolt) and with an embittered faction that has no
    /// voice in cabinet. It is a chance, not a timer: a divided party can limp on, and a strong one can go suddenly.
    /// </summary>
    public double CollapseChance(Country c)
    {
        if (!GoverningParties(c).Any()) return 0;
        double threshold = CollapseCohesion, cohesion = GovernmentCohesion(c);
        if (cohesion >= threshold) return 0;
        double shortfall = (threshold - cohesion) / threshold;
        var p = Of(c);
        double majority = p.PartySeats.Where(kv => p.GovernmentOrganizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).Sum(kv => kv.Value) / (double)Math.Max(1, p.TotalSeats);
        double thin = Math.Clamp(1.4 - majority, 0.6, 1.4);
        return Math.Clamp(Def("party_collapse_chance", 0.5) * Math.Sqrt(shortfall) * thin, 0, 0.9);
    }

    private bool GovernmentCollapsed(Country c)
    {
        double chance = CollapseChance(c);
        return chance > 0 && _game.Rngs.Politics.NextDouble() < chance;
    }

    private IEnumerable<(string Source, string Key, double Value)> FactionModifierSources(Country c)
    {
        double infighting = InfightingStability(c);
        if (Math.Abs(infighting) > 0.01) yield return ("Party infighting", "stability_target", infighting);
    }

    // ------------------------------------------------------------------------------------- validation and script

    private void ValidateFactions(GenericScope scope, string? where, List<(string, string?)> problems)
    {
        if (!IsPartyFaction(scope)) return;
        if (scope.Attributes.TryGetValue("weight", out var raw)
            && (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) || !double.IsFinite(w) || w <= 0))
            problems.Add(($"Faction '{scope.Key}' has invalid weight '{raw}' (expected a positive number)", where));
        foreach (var attribute in new[] { "likes", "dislikes" })
            foreach (var law in Keys(scope, attribute))
                if (LawsEnabled && !Laws.ContainsKey(law))
                    problems.Add(($"Faction '{scope.Key}' {attribute} unknown law '{law}'", where));
        if (FactionPost(scope) is { } post && CabinetEnabled && Positions.All(x => x.Key != post))
            problems.Add(($"Faction '{scope.Key}' wants unknown cabinet post '{post}'", where));
        var likes = FactionLikes(scope);
        foreach (var law in FactionDislikes(scope).Where(l => likes.Contains(l, StringComparer.OrdinalIgnoreCase)))
            problems.Add(($"Faction '{scope.Key}' both likes and dislikes '{law}'", where));
    }

    private void RegisterFactionScript()
    {
        var s = _game.Script;
        s.RegisterValue("faction_loyalty", (ctx, x) => x is GenericScope f && IsPartyFaction(f) ? FactionLoyalty(f) : null);
        s.RegisterValue("faction_grievance", (ctx, x) => x is GenericScope f && IsPartyFaction(f) ? FactionGrievance(f) : null);
        s.RegisterValue("faction_influence", (ctx, x) => x is GenericScope f && IsPartyFaction(f) ? FactionInfluence(f) : null);
        s.RegisterValue("party_cohesion", (ctx, x) => x is GenericScope p && IsParty(p) ? PartyCohesion(p) : null);
        s.RegisterEffect("add_faction_grievance", (ctx, n) =>
        {
            GenericScope? f = n.GetString("faction") is { } key ? _game.World.GetGenericScope(key) : ctx.This as GenericScope;
            if (f == null || !IsPartyFaction(f)) return;
            AddGrievance(f, s.ResolveNumber(n.GetString("value"), ctx));
            if (PartyCountry(f) is { } owner) _modsDirty.Add(owner);
        }, (ctx, n) => $"Change faction grievance by {n.GetString("value")}");
    }
}
