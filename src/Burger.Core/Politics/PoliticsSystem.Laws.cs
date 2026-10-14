using Burger.Core.Mechanics;
using Burger.Core.Model;

namespace Burger.Core.Politics;

public sealed partial class PoliticsSystem
{
    // =============================================================================================
    // Political power (the resource laws, ministers and suppressing movements cost)
    // =============================================================================================

    /// <summary>The resource political costs are paid with (define politics_resource, default political_power); null = everything is free.</summary>
    public ResourceDef? PowerResource
    {
        get
        {
            string key = _game.Data.Defines.Str("politics_resource", "political_power");
            return _game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public double PowerOf(Country c) => PowerResource is { } r ? _game.Resources.Get(c, r) : double.PositiveInfinity;
    public string PowerName => PowerResource?.Name ?? "political power";
    public bool CanAfford(Country c, double cost) => cost <= 0 || PowerOf(c) >= cost;

    private void Pay(Country c, double cost)
    {
        if (cost > 0 && PowerResource is { } r) _game.Resources.Set(c, r, _game.Resources.Get(c, r) - cost);
    }

    // =============================================================================================
    // Laws
    // =============================================================================================

    public LawDef? LawOf(Country c, LawGroup g) => Of(c).Laws.TryGetValue(g.Key, out var k) && Laws.TryGetValue(k, out var l) ? l : null;

    public bool HasLaw(Country c, string law) => LawsEnabled && Laws.TryGetValue(law, out var l) && Of(c).Laws.GetValueOrDefault(l.Group.Key) == l.Key;

    public bool IsLawVisible(Country c, LawDef law) => IsGroupVisible(c, law.Group) && (law.Potential == null || _game.Script.EvalTrigger(law.Potential, _game.Context(c)));

    /// <summary>True if the law group applies to this country (its potential, if any, is met).</summary>
    public bool IsGroupVisible(Country c, LawGroup g) => g.Potential == null || _game.Script.EvalTrigger(g.Potential, _game.Context(c));

    /// <summary>Days before a law group can change again (0 = now).</summary>
    public int LawCooldownLeft(Country c, LawGroup g)
    {
        if (!Of(c).LawChangedDay.TryGetValue(g.Key, out int day)) return 0;
        int cd = (int)_game.Data.Defines.Get("law_change_cooldown_days", 180);
        return Math.Max(0, day + cd - _game.World.Date.TotalDays);
    }

    /// <summary>Why the country cannot enact this law now (null = it can).</summary>
    public string? CannotEnact(Country c, LawDef law)
    {
        if (!LawsEnabled) return "Laws are not used in this game";
        if (HasLaw(c, law.Key)) return "Already in force";
        if (!IsLawVisible(c, law)) return "Not available";
        int cd = LawCooldownLeft(c, law.Group);
        if (cd > 0) return $"{law.Group.Name} changed recently ({cd} days left)";
        if (law.Allow != null && !_game.Script.EvalTrigger(law.Allow, _game.Context(c))) return "Requirements not met";
        double cost = EnactCost(c, law);
        if (!CanAfford(c, cost)) return $"Needs {cost:0} {PowerName}" + (cost > law.Cost ? $" (+{cost - law.Cost:0} to defy factions)" : "");
        return null;
    }

    /// <summary>Enacts a law (pays its cost unless <paramref name="free"/>). Returns null on success, else the reason.</summary>
    public string? Enact(Country c, LawDef law, bool free = false)
    {
        if (!free)
        {
            var reason = CannotEnact(c, law);
            if (reason != null) return reason;
            Pay(c, EnactCost(c, law));
        }
        var p = Of(c);
        var bannedBefore = p.PartySeats.Keys.Where(k => IsPartyBanned(c, p, k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!free) ApplyLawReaction(c, law, LawOf(c, law.Group));
        p.Laws[law.Group.Key] = law.Key;
        p.LawChangedDay[law.Group.Key] = _game.World.Date.TotalDays;
        _modsDirty.Add(c);
        LawsChanged++;
        foreach (var k in p.PartySeats.Keys.ToList()) if (!bannedBefore.Contains(k) && IsPartyBanned(c, p, k)) _game.Hub.RaisePartyBanned(c, k);
        if (law.OnEnact != null) _game.Script.Execute(law.OnEnact, _game.Context(c));
        // Bans apply to every party of the affected ideology; single-party laws also exclude rival organizations
        // that share the ruling ideology. Keep zero-seat entries so inspecting the HUD cannot re-apportion the result.
        foreach (var key in p.PartySeats.Keys.ToList())
            if (IsPartyBanned(c, p, key)) p.PartySeats[key] = 0;
        p.Seats = AggregatePartySeats(c, p.PartySeats);
        foreach (var i in Ideologies)
        {
            if (!IsBanned(c, p, i.Key)) continue;
            p.Seats.Remove(i.Key);
            if (p.Government.Contains(i.Key, StringComparer.OrdinalIgnoreCase) && i.Key != p.Ruling)
                SetGovernment(c, p.Government.Where(g => !g.Equals(i.Key, StringComparison.OrdinalIgnoreCase)).ToList(), "");
        }
        var allowedOrganizations = p.GovernmentOrganizations.Where(key => !IsPartyBanned(c, p, key)
            && IdeologyKeyOfParty(key) is { } ideology && p.Government.Contains(ideology, StringComparer.OrdinalIgnoreCase)).ToList();
        if (allowedOrganizations.Count != p.GovernmentOrganizations.Count)
        {
            if (allowedOrganizations.Count == 0)
            {
                string fallback = p.PartySeats.Where(kv => kv.Value > 0 && !IsPartyBanned(c, p, kv.Key)
                        && !PoliticsSystem.IsMinorityParty(kv.Key) && IdeologyKeyOfParty(kv.Key) != null)
                    .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault()
                    ?? (PartyOf(c, p.Ruling) is { } rulingParty && !IsPartyBanned(c, p, rulingParty.Key)
                        ? rulingParty.Key : p.Ruling);
                allowedOrganizations.Add(fallback);
            }
            var allowedIdeologies = allowedOrganizations.Select(IdeologyKeyOfParty).Where(key => key != null).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedIdeologies.Count == 0) allowedIdeologies.Add(p.Ruling);
            SetGovernment(c, allowedIdeologies, "", allowedOrganizations);
        }
        if (HoldsElections(c, p)) p.NextElection ??= _game.World.Date.AddMonths(ElectionMonths());
        else p.NextElection = null;
        // movements that wanted this law are satisfied
        Movements.RemoveAll(m => m.Country == c && m.Demand == MovementDemand.Legalize && !IsBanned(c, p, m.Ideology));
        if (c.IsPlayer) _game.Notify($"New law: {law.Name} ({law.Group.Name}).", NotificationKind.Good);
        _game.Events.FireOnAction("on_law_changed", c);
        return null;
    }

    /// <summary>AI: considers changing one law group (weights from ai_will_do).</summary>
    private void AiLaws(Country c)
    {
        if (!LawsEnabled || LawGroups.Count == 0) return;
        var g = LawGroups[_game.Rng.Next(LawGroups.Count)];
        if (LawCooldownLeft(c, g) > 0) return;
        var current = LawOf(c, g);
        double Weight(LawDef l) => l.AiWillDo == null ? (l == current ? 1 : 0) : _game.Script.EvalWeight(l.AiWillDo, _game.Context(c), 0);
        double currentWeight = current != null && IsLawVisible(c, current) ? Weight(current) : 0;
        LawDef? best = null;
        double bestWeight = currentWeight * 1.25 + 0.5; // only change for a clearly better law
        foreach (var l in g.Laws)
        {
            if (l == current || CannotEnact(c, l) != null) continue;
            double w = Weight(l);
            if (w > bestWeight) { bestWeight = w; best = l; }
        }
        if (best != null) Enact(c, best);
    }

    // =============================================================================================
    // Cabinet
    // =============================================================================================

    /// <summary>Ideology a character belongs to (flag party_IDEOLOGY), or null.</summary>
    public string? MinisterParty(Character ch)
        => OrganizationOf(ch) is { } org && IdeologyKeyOfParty(org) is { } ideology ? ideology : MinisterIdeologyFlag(ch);

    private string? MinisterIdeologyFlag(Character ch)
    {
        foreach (var f in ch.Vars.Flags)
            if (f.StartsWith("party_", StringComparison.OrdinalIgnoreCase) && IdeologyByKey.ContainsKey(f[6..])) return f[6..];
        return null;
    }

    public void SetParty(Character ch, string ideology)
    {
        foreach (var f in ch.Vars.Flags.Where(f => f.StartsWith("party_", StringComparison.OrdinalIgnoreCase)).ToList()) ch.Vars.ClearFlag(f);
        ch.Vars.SetFlag("party_" + ideology);
    }

    public Character? MinisterOf(Country c, CabinetPosition pos)
        => Of(c).Ministers.TryGetValue(pos.Key, out var ch) && ch.Alive && ch.Country == c ? ch : null;

    /// <summary>Candidates for a post: adult characters of the governing parties who hold no other post, best first.
    /// Missing candidates are generated so every governing party offers a few.</summary>
    public List<Character> Candidates(Country c, CabinetPosition pos)
    {
        var p = Of(c);
        var now = _game.World.Date;
        var busy = p.Ministers.Values.ToHashSet();
        bool Usable(Character ch) => ch.Alive && ch.Country == c && !ch.IsRuler && ch.Age(now) >= _game.Data.Defines.AdultAge && !busy.Contains(ch)
                                     && _game.World.Armies.All(a => a.General != ch);
        var list = _game.World.CharactersOf(c).Where(ch => Usable(ch) && PartyInGovernmentOf(c, ch)).ToList();
        // every governing organization offers a few candidates; generated ones are spread over its factions by influence
        var organizations = p.GovernmentOrganizations.Count > 0 ? p.GovernmentOrganizations : p.Government;
        foreach (var org in organizations)
        {
            var scope = _game.World.GetGenericScope(org);
            var factions = scope != null && IsParty(scope) ? FactionsOf(scope).OrderByDescending(FactionInfluence).ToList() : new List<GenericScope>();
            int have = list.Count(ch => (OrganizationOf(ch, c) ?? MinisterParty(ch)) == org);
            for (int i = have; i < 3 + Math.Min(2, factions.Count); i++)
            {
                var ch = _game.Characters.Generate(c, null, 35 + _game.Rng.Next(30));
                SetAffiliation(ch, org, factions.Count > 0 ? factions[i % factions.Count].Key : null);
                list.Add(ch);
            }
        }
        string stat = pos.Stat;
        return list.OrderByDescending(ch => stat.Length > 0 ? ch.GetStat(stat) : 0).ToList();
    }

    public string? CannotAppoint(Country c, CabinetPosition pos, Character ch)
    {
        if (!CabinetEnabled) return "No cabinet in this game";
        if (!ch.Alive || ch.Country != c) return "Not one of our characters";
        if (MinisterOf(c, pos) == ch) return "Already holds this post";
        if (!PartyInGovernmentOf(c, ch)) return "Their party is not in government";
        if (!CanAfford(c, pos.Cost)) return $"Needs {pos.Cost:0} {PowerName}";
        return null;
    }

    public string? Appoint(Country c, CabinetPosition pos, Character ch, bool free = false)
    {
        if (!free)
        {
            var reason = CannotAppoint(c, pos, ch);
            if (reason != null) return reason;
            Pay(c, pos.Cost);
        }
        var p = Of(c);
        foreach (var kv in p.Ministers.Where(kv => kv.Value == ch).ToList()) p.Ministers.Remove(kv.Key); // one post each
        p.Ministers[pos.Key] = ch;
        if (!free) ApplyAppointmentReaction(c, pos, ch);
        _modsDirty.Add(c);
        _game.Hub.RaiseMinisterChanged(c, ch, true);
        return null;
    }

    public void Dismiss(Country c, CabinetPosition pos)
    {
        if (Of(c).Ministers.Remove(pos.Key, out var gone)) { _modsDirty.Add(c); _game.Hub.RaiseMinisterChanged(c, gone, false); }
    }

    private void AiCabinet(Country c)
    {
        if (!CabinetEnabled) return;
        foreach (var pos in Positions)
        {
            if (MinisterOf(c, pos) != null || !CanAfford(c, pos.Cost)) continue;
            // the AI weighs skill against keeping the discontented factions of its party in the cabinet
            var best = Candidates(c, pos).Where(x => CannotAppoint(c, pos, x) == null)
                .OrderByDescending(x => (pos.Stat.Length > 0 ? x.GetStat(pos.Stat) : 0)
                    + (FactionOfCharacter(x) is { } f ? FactionGrievance(f) * 0.3 + (FactionPost(f) == pos.Key ? 4 : 0) : 0)).FirstOrDefault();
            if (best != null) Appoint(c, pos, best);
            break; // one appointment per month
        }
    }

    // set when the AI system calls AiMonthly; a project whose backend AI.cs predates politics never does,
    // and then politics runs its AI part itself
    private bool _aiCalled;

    /// <summary>AI: laws, ministers and movements (monthly, called by the AI system).</summary>
    public void AiMonthly(Country c)
    {
        _aiCalled = true;
        if (!Enabled || c.IsPlayer) return;
        AiLaws(c);
        AiCabinet(c);
        if (CivilWarsEnabled) AiMovements(c);
    }
}
