using Burger.Core.Model;

namespace Burger.Core.Politics;

/// <summary>
/// What a government can do about the factions inside its own party (and what the AI does), beyond paying to defy them:
/// favour one, sideline one, bring a rival party into the coalition, or lose a faction that has given up on the party.
/// Every action moves persisted state only (faction weight, grievance, loyalty, government membership, or the party tree),
/// so influence, cohesion, law resistance and stability pick the result up through the same formulas as everything else.
/// </summary>
public sealed partial class PoliticsSystem
{
    private const string FavouredVar = "favoured_day", RestlessVar = "restless_months";
    private const double MinWeight = 0.25, MaxWeight = 8;

    private void SetWeight(GenericScope f, double w)
        => f.Attributes["weight"] = Math.Clamp(w, MinWeight, MaxWeight).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private string? CannotReshape(Country c, GenericScope faction, string what)
    {
        if (!IsPartyFaction(faction) || PartyCountry(faction) != c) return "Not a faction of this country";
        if (!Of(c).GovernmentOrganizations.Contains(PartyKeyOf(faction), StringComparer.OrdinalIgnoreCase)) return "Only factions of a governing party can be managed";
        int cd = (int)Def("faction_reshape_cooldown_days", 180);
        if (faction.Vars.Has(FavouredVar) && faction.Vars.Get(FavouredVar) + cd > _game.World.Date.TotalDays)
            return $"Recently {what} ({(int)(faction.Vars.Get(FavouredVar) + cd - _game.World.Date.TotalDays)} days left)";
        return null;
    }

    public double FavourCost => Def("faction_favour_cost", 60);
    public double SidelineCost => Def("faction_sideline_cost", 30);

    public string? CannotFavour(Country c, GenericScope faction)
    {
        var r = CannotReshape(c, faction, "favoured");
        if (r != null) return r;
        if (FactionsOf((GenericScope)_game.World.ScopeHierarchy.ParentOf(faction)!).Count() < 2) return "The party has no other faction";
        if (FactionWeight(faction) >= MaxWeight) return "Already dominant";
        return CanAfford(c, FavourCost) ? null : $"Needs {FavourCost:0} {PowerName}";
    }

    /// <summary>Put the party's weight behind one faction: it gains influence and goodwill, its rivals inside the party resent it.</summary>
    public string? Favour(Country c, GenericScope faction)
    {
        var r = CannotFavour(c, faction);
        if (r != null) return r;
        Pay(c, FavourCost);
        SetWeight(faction, FactionWeight(faction) * Def("faction_favour_factor", 1.5));
        AddGrievance(faction, -Def("faction_favour_relief", 15));
        foreach (var other in FactionsOf((GenericScope)_game.World.ScopeHierarchy.ParentOf(faction)!).Where(o => o != faction))
            AddGrievance(other, Def("faction_favour_rivals", 12));
        faction.Vars.Set(FavouredVar, _game.World.Date.TotalDays);
        _modsDirty.Add(c);
        return null;
    }

    public string? CannotSideline(Country c, GenericScope faction)
    {
        var r = CannotReshape(c, faction, "sidelined");
        if (r != null) return r;
        if (FactionsOf((GenericScope)_game.World.ScopeHierarchy.ParentOf(faction)!).Count() < 2) return "The party has no other faction";
        if (FactionWeight(faction) <= MinWeight) return "Already marginal";
        return CanAfford(c, SidelineCost) ? null : $"Needs {SidelineCost:0} {PowerName}";
    }

    /// <summary>Cut a faction down to size: it counts for less in the party (and in law resistance), but it will not forget.</summary>
    public string? Sideline(Country c, GenericScope faction)
    {
        var r = CannotSideline(c, faction);
        if (r != null) return r;
        Pay(c, SidelineCost);
        SetWeight(faction, FactionWeight(faction) * Def("faction_sideline_factor", 0.6));
        AddGrievance(faction, Def("faction_sideline_grievance", 25));
        faction.Vars.Set(FavouredVar, _game.World.Date.TotalDays);
        _modsDirty.Add(c);
        return null;
    }

    // ------------------------------------------------------------------------------------- coalition

    public string? CannotInvite(Country c, GenericScope party)
    {
        var p = Of(c);
        if (!IsParty(party) || PartyCountry(party) != c) return "Not a party of this country";
        if (p.GovernmentOrganizations.Contains(party.Key, StringComparer.OrdinalIgnoreCase)) return "Already in government";
        if (IsPartyBanned(c, party.Key)) return "Banned";
        if (p.PartySeats.GetValueOrDefault(party.Key) <= 0) return "Holds no seats";
        double cost = Def("coalition_invite_cost", 50);
        return CanAfford(c, cost) ? null : $"Needs {cost:0} {PowerName}";
    }

    /// <summary>Bring a party into the governing coalition: its seats count for the majority, and its factions now weigh on laws, cabinet and cohesion.</summary>
    public string? Invite(Country c, GenericScope party)
    {
        var r = CannotInvite(c, party);
        if (r != null) return r;
        var p = Of(c);
        Pay(c, Def("coalition_invite_cost", 50));
        p.GovernmentOrganizations.Add(party.Key);
        var ideology = IdeologyOfParty(party);
        if (ideology != null && !p.Government.Contains(ideology, StringComparer.OrdinalIgnoreCase)) p.Government.Add(ideology);
        _modsDirty.Add(c);
        return null;
    }

    public string? CannotDismiss(Country c, GenericScope party)
    {
        var p = Of(c);
        if (!IsParty(party) || PartyCountry(party) != c) return "Not a party of this country";
        if (string.Equals(p.RulingOrganization, party.Key, StringComparison.OrdinalIgnoreCase)) return "Leads the government";
        return p.GovernmentOrganizations.Contains(party.Key, StringComparer.OrdinalIgnoreCase) ? null : "Not in government";
    }

    /// <summary>Drop a coalition partner: its ministers stand down, and its supporters (and its factions) are left in opposition.</summary>
    public string? Dismiss(Country c, GenericScope party)
    {
        var r = CannotDismiss(c, party);
        if (r != null) return r;
        var p = Of(c);
        p.GovernmentOrganizations.RemoveAll(k => k.Equals(party.Key, StringComparison.OrdinalIgnoreCase));
        var ideology = IdeologyOfParty(party);
        if (ideology != null && !p.GovernmentOrganizations.Any(k => IdeologyOfParty(_game.World.GetGenericScope(k)!) == ideology))
            p.Government.RemoveAll(i => i.Equals(ideology, StringComparison.OrdinalIgnoreCase));
        foreach (var f in FactionsOf(party)) AddGrievance(f, Def("coalition_dismiss_grievance", 20));
        foreach (var kv in p.Ministers.ToList())
            if (OrganizationOf(kv.Value, c) is { } org && org.Equals(party.Key, StringComparison.OrdinalIgnoreCase))
                p.Ministers.Remove(kv.Key);
        _modsDirty.Add(c);
        return null;
    }

    // ------------------------------------------------------------------------------------- splintering

    public int RestlessMonths(GenericScope faction) => (int)faction.Vars.Get(RestlessVar);
    public int SplinterMonths => (int)Def("faction_splinter_months", 6);
    public double SplinterLoyalty => Def("faction_splinter_loyalty", 25);

    /// <summary>A faction that is disaffected, strong enough to matter and has other factions to leave behind is a splinter risk.</summary>
    public bool CanSplinter(GenericScope f)
        => IsPartyFaction(f) && ParentParty(f) is { } party && FactionsOf(party).Count() >= 2
           && FactionInfluence(f) >= Def("faction_splinter_influence", 0.2);

    public IEnumerable<GenericScope> GoverningFactions(Country c) => GoverningParties(c).SelectMany(x => FactionsOf(x.Party));

    private GenericScope? ParentParty(GenericScope f) => _game.World.ScopeHierarchy.ParentOf(f) as GenericScope;

    /// <summary>
    /// Monthly chance a restless faction (loyalty under faction_splinter_loyalty for at least a month) breaks away. It rises with how long and how deep the
    /// disaffection runs and with the faction's strength; a patient faction holds out longer, one with a cabinet post to lose rarely leaves, and one
    /// shut out of a governing party's cabinet goes sooner. Nothing happens in the first half of faction_splinter_months (6); after that it is a chance, not a timer.
    /// </summary>
    public double SplinterChance(Country c, GenericScope f)
    {
        int months = RestlessMonths(f);
        if (months < Math.Max(2, SplinterMonths / 2)) return 0; // (a passing sulk is not a split: it takes a few months of disaffection)
        double depth = Math.Clamp((SplinterLoyalty - FactionLoyalty(f)) / Math.Max(1, SplinterLoyalty), 0, 1);
        double chance = Def("faction_splinter_chance", 0.12) * (0.4 + months / (double)Math.Max(1, SplinterMonths)) * (0.5 + depth) * (0.6 + FactionInfluence(f)) / FactionTolerance(f);
        var p = Of(c);
        if (p.GovernmentOrganizations.Contains(PartyKeyOf(f), StringComparer.OrdinalIgnoreCase) && CabinetEnabled)
            chance *= p.Ministers.Values.Any(m => ReferenceEquals(FactionOfCharacter(m), f)) ? 0.4 : FactionPost(f) != null ? 1.4 : 1;
        return Math.Clamp(chance, 0, 0.8);
    }

    private void TrackRestlessness(GenericScope f)
        => f.Vars.Set(RestlessVar, FactionLoyalty(f) < SplinterLoyalty ? f.Vars.Get(RestlessVar) + 1 : 0);

    /// <summary>
    /// The faction leaves and founds its own party of the same ideology, taking its share of the party's support and seats.
    /// A governing party that splinters loses those seats from the government benches; the new party starts in opposition.
    /// Ideology totals are unchanged (the new party's share is subtracted from the old), so only competition changes.
    /// </summary>
    public GenericScope? Splinter(Country c, GenericScope faction)
    {
        if (ParentParty(faction) is not { } old || !CanSplinter(faction)) return null;
        var p = Of(c);
        var ideology = IdeologyOfParty(old);
        if (ideology == null) return null;
        string key = $"{old.Key}_{faction.Key}";
        if (_game.World.GetGenericScope(key) != null) key += $"_{_game.World.Date.TotalDays}";
        if (!_game.World.TryCreateGenericScope(key, PartyKind, faction.Name, true, out var scope, out _) || scope == null) return null;
        scope.Attributes[PartyIdeologyAttribute] = ideology;
        // its own colour, so the generated flag (seeded by the party key) is not the old party's colour twice
        var parentColour = PartyColor(old.Key);
        uint h = 2166136261; foreach (char ch in key) h = (h ^ ch) * 16777619;
        scope.Attributes["color"] = $"{(parentColour.R * 6 + (int)(h & 255) * 4) / 10} {(parentColour.G * 6 + (int)((h >> 8) & 255) * 4) / 10} {(parentColour.B * 6 + (int)((h >> 16) & 255) * 4) / 10}";
        if (!_game.World.ScopeHierarchy.TrySetParent(scope, c, out _) || !_game.World.ScopeHierarchy.TryReparent(faction, scope, out _)) return null;

        double share = FactionInfluence(faction); // measured before the move changed anything
        // the old party's remaining factions are no longer in a party that has lost its wing
        double pop = p.PartyPopularity.GetValueOrDefault(old.Key), take = pop * share;
        p.PartyPopularity[old.Key] = pop - take;
        p.PartyPopularity[key] = take;
        int seats = p.PartySeats.GetValueOrDefault(old.Key), taken = (int)Math.Round(seats * share);
        p.PartySeats[old.Key] = seats - taken;
        p.PartySeats[key] = taken;
        faction.Vars.Set(LoyaltyVar, StartLoyalty);
        faction.Vars.Set(GrievanceVar, 0);
        faction.Vars.Set(RestlessVar, 0);
        SetWeight(faction, 1);
        foreach (var rest in FactionsOf(old)) AddGrievance(rest, Def("faction_splinter_shock", 10));
        foreach (var kv in p.Ministers.ToList())
            if (ReferenceEquals(FactionOfCharacter(kv.Value), faction)) p.Ministers.Remove(kv.Key);
        _modsDirty.Add(c);
        if (c.IsPlayer)
            _game.Notify($"{faction.DisplayName} has broken away from {old.DisplayName} and founded its own party.", NotificationKind.Bad);
        return scope;
    }

    // ------------------------------------------------------------------------------------- AI

    /// <summary>
    /// What a country run by the AI does about its own factions each month: negotiate with the most dangerous one it can afford,
    /// favour the faction it relies on when none is angry, and cut a hopeless minority loose rather than keep paying it off.
    /// It keeps a reserve so it never spends the power it needs for laws and the cabinet.
    /// </summary>
    private void AiFactions(Country c)
    {
        var p = Of(c);
        double reserve = Def("faction_ai_reserve", 60);
        var governing = GoverningParties(c).SelectMany(x => FactionsOf(x.Party)).ToList();
        if (governing.Count == 0) return;
        double Have() => PowerOf(c);

        var worst = governing.OrderByDescending(f => FactionGrievance(f) * FactionInfluence(f)).First();
        if (FactionGrievance(worst) >= 30 && CannotPlacate(c, worst) == null && Have() >= Def("faction_placate_cost", 40) + reserve)
        {
            Placate(c, worst);
            return;
        }
        // a faction nobody can satisfy and that is too weak to matter: sideline it instead of letting it drag cohesion down
        var hopeless = governing.Where(f => FactionGrievance(f) >= 60 && FactionInfluence(f) < 0.2 && CannotSideline(c, f) == null)
            .OrderByDescending(FactionGrievance).FirstOrDefault();
        if (hopeless != null && Have() >= SidelineCost + reserve) { Sideline(c, hopeless); return; }
        // otherwise back the most loyal faction when the party is already restless
        if (GovernmentCohesion(c) < Def("party_infighting_threshold", 55) + 10)
        {
            var anchor = governing.Where(f => FactionLoyalty(f) >= 55).OrderByDescending(f => FactionLoyalty(f)).FirstOrDefault();
            if (anchor != null && CannotFavour(c, anchor) == null && Have() >= FavourCost + reserve) Favour(c, anchor);
        }
    }
}
