using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Politics;

/// <summary>
/// Party organizations as generic scopes. A party is a <see cref="GenericScope"/> of kind <c>party</c> whose hierarchy
/// parent is its country and whose <c>ideology</c> attribute names the ideology bucket it represents. Factions
/// (<c>party_faction</c>) and regional branches (<c>party_branch</c>) are generic scopes parented to a party.
/// Optional <c>leader</c> attributes on any party structure reference characters; optional <c>state</c> attributes
/// on regional branches reference states. These are links to existing entities, not copies of their data.
/// An optional <c>flag</c> attribute on a party names an image relative to the game folder (data = { flag = "gfx/flags/parties/spd.png" });
/// parties without one get a flag generated from their colour.
///
/// Ownership of each value (nothing here keeps a second copy):
/// <list type="bullet">
/// <item>Country of a party: the hierarchy parent link (there is no country attribute).</item>
/// <item>Party support and seats: <see cref="CountryPolitics"/>, keyed by unique party scope. Ideology popularity and seats remain
/// aggregated for country-wide drift, laws, ideology opinion, unrest, and other legacy mechanics.</item>
/// <item>Name: <see cref="CountryPolitics.PartyNames"/> (history or a scripted rename made before the party existed)
/// first, then the party scope's name, then the ideology default. <c>set_party_name</c> writes the party scope when one
/// exists, so a scope-backed party has a single place its name lives.</item>
/// <item>Coalitions and government formation stay in <see cref="CountryPolitics.Government"/>; they are not hierarchy links.</item>
/// </list>
/// Saves need no politics changes: scopes, their attributes and their hierarchy links are already persisted by the
/// generic scope sections of the save file.
/// </summary>
public sealed partial class PoliticsSystem
{
    public const string PartyKind = "party";
    public const string PartyFactionKind = "party_faction";
    public const string PartyBranchKind = "party_branch";
    public const string PartyIdeologyAttribute = "ideology";
    public const string LeaderAttribute = "leader";
    public const string FlagAttribute = "flag";
    public const string BranchStateAttribute = "state";

    private static bool HasKind(IScope? scope, string kind)
        => scope is GenericScope generic && string.Equals(generic.Kind, kind, StringComparison.OrdinalIgnoreCase);

    public static bool IsParty(IScope? scope) => HasKind(scope, PartyKind);
    public static bool IsPartyFaction(IScope? scope) => HasKind(scope, PartyFactionKind);
    public static bool IsPartyBranch(IScope? scope) => HasKind(scope, PartyBranchKind);
    private static bool IsPartyStructure(IScope? scope) => IsParty(scope) || IsPartyFaction(scope) || IsPartyBranch(scope);

    /// <summary>The ideology key a party scope declares, or null when it declares none.</summary>
    public static string? IdeologyOfParty(GenericScope party)
        => party.Attributes.TryGetValue(PartyIdeologyAttribute, out var key) ? key : null;

    /// <summary>Party scopes directly under a country, in definition order.</summary>
    public IEnumerable<GenericScope> PartiesOf(Country country)
        => _game.World.ScopeHierarchy.ChildrenOf(country).OfType<GenericScope>().Where(IsParty);

    /// <summary>All named organizations grouped under an ideology. Multiple entries are distinct competing parties.</summary>
    public IEnumerable<GenericScope> PartiesOfIdeology(Country country, string ideology)
        => PartiesOf(country).Where(p => string.Equals(IdeologyOfParty(p), ideology, StringComparison.OrdinalIgnoreCase));

    /// <summary>One representative party for legacy ideology-only APIs. For a contested ideology, the ruling party or
    /// the party with the largest support is preferred; party APIs and elections use the unique scope key.</summary>
    public GenericScope? PartyOf(Country country, string ideology)
    {
        var candidates = PartiesOfIdeology(country, ideology).ToList();
        if (candidates.Count == 0) return null;
        var politics = _state.TryGetValue(country, out var state) ? state : null;
        if (politics?.RulingOrganization is { Length: > 0 } ruling && candidates.FirstOrDefault(p => p.Key.Equals(ruling, StringComparison.OrdinalIgnoreCase)) is { } selected)
            return selected;
        return candidates.OrderByDescending(p => politics?.PartyPopularity.GetValueOrDefault(p.Key) ?? 0).First();
    }

    /// <summary>Stable keys for all competing parties and ideology fallbacks, ensuring old scenarios still run unchanged.</summary>
    public IEnumerable<string> PartyKeysOf(Country country)
    {
        var parties = PartiesOf(country).Where(p => Ideology(IdeologyOfParty(p)) != null).ToList();
        foreach (var party in parties) yield return party.Key;
        foreach (var ideology in Ideologies)
            if (!parties.Any(p => string.Equals(IdeologyOfParty(p), ideology.Key, StringComparison.OrdinalIgnoreCase)))
                yield return ideology.Key;
    }

    public string? IdeologyKeyOfParty(string? key)
    {
        if (key == null) return null;
        if (_game.World.GetGenericScope(key) is { } scope && IsParty(scope)) return IdeologyOfParty(scope);
        return IdeologyByKey.ContainsKey(key) ? key : null;
    }

    public string PartyDisplayName(Country country, string key)
        => _game.World.GetGenericScope(key) is { } scope && IsParty(scope)
            && PartyCountry(scope) == country ? scope.DisplayName : PartyName(country, key);

    private double PartySupportWeight(string key)
    {
        if (_game.World.GetGenericScope(key) is not { } scope || !IsParty(scope)) return 1;
        return scope.Attributes.TryGetValue("support_weight", out var raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight)
            && double.IsFinite(weight) && weight > 0 ? weight : 1;
    }

    /// <summary>Initialize independent support by splitting each ideology's support across its named parties.</summary>
    public void InitializePartyPopularity(Country country, CountryPolitics state)
    {
        var keys = PartyKeysOf(country).ToList();
        state.PartyPopularity.Clear();
        foreach (var group in keys.GroupBy(k => IdeologyKeyOfParty(k), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key == null) continue;
            double ideologySupport = state.Popularity.GetValueOrDefault(group.Key);
            double totalWeight = group.Sum(PartySupportWeight);
            foreach (var key in group)
                state.PartyPopularity[key] = ideologySupport * PartySupportWeight(key) / Math.Max(1e-9, totalWeight);
        }
        NormalizePartySupport(state);
    }

    /// <summary>Keep ideology drift compatible by scaling its member parties proportionally after aggregate support changes.</summary>
    public void SyncPartyPopularityToIdeologies(Country country, CountryPolitics state)
    {
        var keys = PartyKeysOf(country).ToList();
        var valid = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var old in state.PartyPopularity.Keys.Where(k => !valid.Contains(k)).ToList()) state.PartyPopularity.Remove(old);
        foreach (var group in keys.GroupBy(k => IdeologyKeyOfParty(k), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key == null) continue;
            var members = group.ToList();
            double target = state.Popularity.GetValueOrDefault(group.Key);
            double current = members.Sum(k => state.PartyPopularity.GetValueOrDefault(k));
            foreach (var key in members)
            {
                double ratio = current > 1e-12
                    ? state.PartyPopularity.GetValueOrDefault(key) / current
                    : PartySupportWeight(key) / Math.Max(1e-9, members.Sum(PartySupportWeight));
                state.PartyPopularity[key] = target * ratio;
            }
        }
        NormalizePartySupport(state);
    }

    private static void NormalizePartySupport(CountryPolitics state)
    {
        foreach (var key in state.PartyPopularity.Keys.ToList())
            if (!double.IsFinite(state.PartyPopularity[key]) || state.PartyPopularity[key] < 0) state.PartyPopularity[key] = 0;
        double sum = state.PartyPopularity.Values.Sum();
        if (sum <= 1e-12) return;
        foreach (var key in state.PartyPopularity.Keys.ToList()) state.PartyPopularity[key] /= sum;
    }

    private void RebuildIdeologyPopularityFromParties(Country country, CountryPolitics state)
    {
        foreach (var ideology in Ideologies) state.Popularity[ideology.Key] = 0;
        foreach (var kv in state.PartyPopularity)
            if (IdeologyKeyOfParty(kv.Key) is { } ideology && state.Popularity.ContainsKey(ideology))
                state.Popularity[ideology] += kv.Value;
        Normalize(state);
        SyncPartyPopularityToIdeologies(country, state);
    }

    /// <summary>Move support directly to/from one party. The other parties change proportionally; the ideology totals are rebuilt.</summary>
    public bool AddPartySupport(IScope? scope, double amount)
    {
        if (scope is not GenericScope party || !IsParty(party) || !TryResolveParty(party, out var country, out _)
            || !double.IsFinite(amount)) return false;
        var state = Of(country);
        if (state.PartyPopularity.Count == 0) InitializePartyPopularity(country, state);
        if (!state.PartyPopularity.ContainsKey(party.Key)) return false;
        double target = Math.Clamp(state.PartyPopularity[party.Key] + amount, 0, 1);
        double rest = state.PartyPopularity.Where(kv => !kv.Key.Equals(party.Key, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        foreach (var key in state.PartyPopularity.Keys.ToList())
        {
            if (key.Equals(party.Key, StringComparison.OrdinalIgnoreCase)) state.PartyPopularity[key] = target;
            else state.PartyPopularity[key] = rest > 1e-12 ? state.PartyPopularity[key] * (1 - target) / rest : 0;
        }
        if (rest <= 1e-12 && state.PartyPopularity.Count > 1 && target < 1)
        {
            double weight = state.PartyPopularity.Keys.Where(k => !k.Equals(party.Key, StringComparison.OrdinalIgnoreCase)).Sum(PartySupportWeight);
            foreach (var key in state.PartyPopularity.Keys.Where(k => !k.Equals(party.Key, StringComparison.OrdinalIgnoreCase)).ToList())
                state.PartyPopularity[key] = (1 - target) * PartySupportWeight(key) / Math.Max(1e-9, weight);
        }
        NormalizePartySupport(state);
        RebuildIdeologyPopularityFromParties(country, state);
        return true;
    }

    /// <summary>Party votes with ordinary election noise, retaining a separate total for each organization.</summary>
    public Dictionary<string, double> PartyVotes(Country country)
    {
        var state = Of(country);
        if (state.PartyPopularity.Count == 0) InitializePartyPopularity(country, state);
        double noise = _game.Data.Defines.Get("election_noise", 0.03);
        var votes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in PartyKeysOf(country))
            votes[key] = Math.Max(0, state.PartyPopularity.GetValueOrDefault(key) * CohesionVoteFactor(key) + (_game.Rngs.Politics.NextDouble() * 2 - 1) * noise);
        double sum = votes.Values.Sum();
        if (sum > 0) foreach (var key in votes.Keys.ToList()) votes[key] /= sum;
        return votes;
    }

    /// <summary>Whether this exact organization may contest seats under the country's current laws.</summary>
    public bool IsPartyBanned(Country country, string partyKey)
        => IsPartyBanned(country, Of(country), partyKey);

    private bool IsPartyBanned(Country country, CountryPolitics state, string partyKey)
    {
        if (!LawsEnabled) return false;
        if (PoliticsSystem.IsMinorityParty(partyKey))
            return state.Laws.Any(kv => Laws.TryGetValue(kv.Value, out var law) && law.SingleParty);
        string? ideology = IdeologyKeyOfParty(partyKey);
        if (ideology == null) return true;
        foreach (var kv in state.Laws)
        {
            if (!Laws.TryGetValue(kv.Value, out var law)) continue;
            if (law.Bans.Contains(ideology)) return true;
            if (law.SingleParty && !partyKey.Equals(state.RulingOrganization, StringComparison.OrdinalIgnoreCase))
            {
                // Legacy saves can still identify the ruling party by ideology if no named organization was persisted.
                if (!(state.RulingOrganization.Length == 0 && ideology.Equals(state.Ruling, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        return false;
    }

    /// <summary>Allocate seats to unique party keys using D'Hondt, applying the existing ideology bans to each party's ideology.</summary>
    public Dictionary<string, int> PartySeatsFrom(Country country, CountryPolitics state, Dictionary<string, double> votes)
    {
        double threshold = _game.Data.Defines.Get("election_threshold", 0.05);
        var eligible = votes.Where(kv => kv.Value >= threshold && !IsPartyBanned(country, state, kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        // Keep explicit zero-seat entries. An absent key must mean a newly introduced party or a legacy save,
        // not an organization that legitimately failed the threshold; otherwise a UI query could re-apportion seats.
        var seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in votes.Keys) seats[key] = 0;
        if (eligible.Count == 0)
        {
            string fallback = state.RulingOrganization.Length > 0 ? state.RulingOrganization : PartyOf(country, state.Ruling)?.Key ?? state.Ruling;
            if (!seats.ContainsKey(fallback)) seats[fallback] = 0;
            seats[fallback] = state.TotalSeats;
            return seats;
        }
        for (int i = 0; i < state.TotalSeats; i++)
        {
            string best = eligible.MaxBy(kv => kv.Value / (seats[kv.Key] + 1)).Key;
            seats[best]++;
        }
        return seats;
    }

    /// <summary>Find a parliamentary coalition among exact party organizations, not ideology buckets.</summary>
    public List<string>? FormPartyCoalition(Country country, CountryPolitics state, string leaderParty)
    {
        string? leaderIdeology = IdeologyKeyOfParty(leaderParty);
        if (leaderIdeology == null || !state.PartySeats.TryGetValue(leaderParty, out int leaderSeats) || leaderSeats <= 0) return null;
        int total = Math.Max(1, state.PartySeats.Values.Sum());
        var coalition = new List<string> { leaderParty };
        int seats = leaderSeats;
        foreach (var kv in state.PartySeats.Where(kv => kv.Value > 0 && !kv.Key.Equals(leaderParty, StringComparison.OrdinalIgnoreCase)
                         && !PoliticsSystem.IsMinorityParty(kv.Key)).OrderByDescending(kv => kv.Value))
        {
            if (seats * 2 > total) break;
            string? otherIdeology = IdeologyKeyOfParty(kv.Key);
            if (otherIdeology == null) continue;
            var leaderDef = Ideology(leaderIdeology);
            var otherDef = Ideology(otherIdeology);
            if (leaderDef == null || otherDef == null) continue;
            bool compatible = coalition.All(member => IdeologyKeyOfParty(member) is { } memberKey
                && Ideology(memberKey) is { } memberDef && Rules.WillGovernWith(_game, country, memberDef, otherDef));
            if (!compatible) continue;
            coalition.Add(kv.Key);
            seats += kv.Value;
        }
        return seats * 2 > total ? coalition : null;
    }

    /// <summary>Convert independent party seats into the ideology totals existing laws/government rules consume.</summary>
    public Dictionary<string, int> AggregatePartySeats(Country country, Dictionary<string, int> partySeats)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in partySeats)
        {
            string key = PoliticsSystem.IsMinorityParty(kv.Key) ? kv.Key : IdeologyKeyOfParty(kv.Key) ?? kv.Key;
            result[key] = result.GetValueOrDefault(key) + kv.Value;
        }
        return result;
    }

    /// <summary>Migrate old saves by splitting each ideology's saved seat total among its named organizations.</summary>
    public Dictionary<string, int> DistributeLegacySeats(Country country, CountryPolitics state, Dictionary<string, int> legacySeats)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in PartyKeysOf(country)) result[key] = 0;
        foreach (var kv in legacySeats.Where(kv => PoliticsSystem.IsMinorityParty(kv.Key))) result[kv.Key] = kv.Value;
        foreach (var ideologySeats in legacySeats.Where(kv => !PoliticsSystem.IsMinorityParty(kv.Key)))
        {
            var keys = PartyKeysOf(country).Where(key => string.Equals(IdeologyKeyOfParty(key), ideologySeats.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (keys.Count == 0) { result[ideologySeats.Key] = ideologySeats.Value; continue; }
            double[] weights = keys.Select(key => Math.Max(0, state.PartyPopularity.GetValueOrDefault(key))).ToArray();
            if (weights.Sum() <= 1e-12) weights = keys.Select(PartySupportWeight).ToArray();
            double total = Math.Max(1e-12, weights.Sum());
            var assigned = new int[keys.Count];
            var remainders = new List<(int Index, double Fraction)>();
            int used = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                double exact = ideologySeats.Value * weights[i] / total;
                assigned[i] = (int)Math.Floor(exact);
                used += assigned[i];
                remainders.Add((i, exact - assigned[i]));
            }
            foreach (var item in remainders.OrderByDescending(x => x.Fraction).Take(Math.Max(0, ideologySeats.Value - used))) assigned[item.Index]++;
            for (int i = 0; i < keys.Count; i++) result[keys[i]] = assigned[i];
        }
        return result;
    }

    public void AssignGovernmentOrganizations(Country country, CountryPolitics state)
    {
        var organizations = new List<string>();
        foreach (var ideology in state.Government.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var best = state.PartySeats.Where(kv => !PoliticsSystem.IsMinorityParty(kv.Key)
                    && !IsPartyBanned(country, state, kv.Key)
                    && string.Equals(IdeologyKeyOfParty(kv.Key), ideology, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Value)
                .ThenByDescending(kv => state.PartyPopularity.GetValueOrDefault(kv.Key))
                .Select(kv => kv.Key).FirstOrDefault();
            best ??= PartiesOfIdeology(country, ideology).Where(party => !IsPartyBanned(country, state, party.Key))
                .OrderByDescending(party => state.PartyPopularity.GetValueOrDefault(party.Key)).Select(party => party.Key).FirstOrDefault();
            best ??= ideology;
            organizations.Add(best);
        }
        state.GovernmentOrganizations = organizations;
        state.RulingOrganization = organizations.FirstOrDefault() ?? PartyOf(country, state.Ruling)?.Key ?? state.Ruling;
    }

    /// <summary>Factions or regional branches directly under a party.</summary>
    public IEnumerable<GenericScope> PartyUnitsOf(GenericScope party, string kind)
        => _game.World.ScopeHierarchy.ChildrenOf(party).OfType<GenericScope>().Where(s => HasKind(s, kind));

    /// <summary>The country a party, faction or branch belongs to, found by walking up through party structures only.</summary>
    public Country? PartyCountry(IScope? scope)
    {
        if (!IsPartyStructure(scope)) return null;
        for (var cursor = _game.World.ScopeHierarchy.ParentOf(scope); cursor != null; cursor = _game.World.ScopeHierarchy.ParentOf(cursor))
        {
            if (cursor is Country country) return country;
            if (!IsPartyStructure(cursor)) return null;
        }
        return null;
    }

    /// <summary>Game-folder path of a party's flag image (data = { flag = ... }), or null when it has none.</summary>
    public string? PartyFlagPath(string key)
        => _game.World.GetGenericScope(key) is { } scope && IsParty(scope)
            && scope.Attributes.TryGetValue(FlagAttribute, out var path) && !string.IsNullOrWhiteSpace(path) ? path : null;

    /// <summary>True when the character leads a party of their country (political leaders do not command armies).</summary>
    public bool IsPartyLeader(Character ch)
        => Enabled && ch.Country != null && PartiesOf(ch.Country).Any(party => LeaderOf(party) == ch);

    /// <summary>The character named as leader by a party, faction or branch, or null when absent/unknown.</summary>
    public Character? LeaderOf(IScope? scope)
        => scope is GenericScope structure && IsPartyStructure(structure)
            && structure.Attributes.TryGetValue(LeaderAttribute, out var key)
            ? _game.World.GetCharacter(key) : null;

    /// <summary>The state named by a regional branch, or null when absent/unknown.</summary>
    public State? StateOfBranch(IScope? scope)
        => scope is GenericScope branch && IsPartyBranch(branch)
            && branch.Attributes.TryGetValue(BranchStateAttribute, out var key)
            ? _game.World.GetState(key) : null;

    private bool TryResolveParty(IScope? scope, out Country country, out string ideology)
    {
        country = null!;
        ideology = "";
        if (!Enabled || scope is not GenericScope party || !IsParty(party)) return false;
        if (_game.World.ScopeHierarchy.ParentOf(party) is not Country owner) return false;
        if (Ideology(IdeologyOfParty(party)) is not { } def) return false;
        country = owner;
        ideology = def.Key;
        return true;
    }

    /// <summary>Independent country-wide support of a named party (0-1), or null for an invalid party.</summary>
    public double? PartyPopularity(IScope? scope)
    {
        if (!TryResolveParty(scope, out var country, out _)) return null;
        var state = Of(country);
        var key = ((GenericScope)scope!).Key;
        if (!state.PartyPopularity.ContainsKey(key)) SyncPartyPopularityToIdeologies(country, state);
        return state.PartyPopularity.GetValueOrDefault(key);
    }

    /// <summary>Seats won by this exact organization, not the aggregate seats of its ideology.</summary>
    public int? PartySeats(IScope? scope)
    {
        if (!TryResolveParty(scope, out var country, out _)) return null;
        var state = Of(country);
        var key = ((GenericScope)scope!).Key;
        if (!state.PartySeats.ContainsKey(key)) state.PartySeats = DistributeLegacySeats(country, state, state.Seats);
        return state.PartySeats.GetValueOrDefault(key);
    }

    public bool PartyInGovernment(IScope? scope)
    {
        if (!TryResolveParty(scope, out var country, out var ideology)) return false;
        var state = Of(country);
        string key = ((GenericScope)scope!).Key;
        if (state.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase)) return true;
        return InGovernment(country, ideology) && PartiesOfIdeology(country, ideology).Count() == 1;
    }

    /// <summary>
    /// Finds party structures that break the model: invalid party parents or ideologies, invalid support weights,
    /// factions/branches outside a party, and unresolved leader or regional-state references. Parties may share an ideology.
    /// </summary>
    public List<(string Message, string? Where)> FindPartyProblems()
    {
        var problems = new List<(string, string?)>();
        if (!Enabled) return problems;
        var hierarchy = _game.World.ScopeHierarchy;
        foreach (var scope in _game.World.GenericScopes)
        {
            string? where = scope.Definition?.Location;
            if (IsParty(scope))
            {
                if (hierarchy.ParentOf(scope) is not Country country)
                {
                    problems.Add(($"Party '{scope.Key}' must have a country as its parent (parent = country:TAG)", where));
                    continue;
                }
                string? declared = IdeologyOfParty(scope);
                if (Ideology(declared) is not { } def)
                {
                    problems.Add(($"Party '{scope.Key}' needs data = {{ ideology = ... }} naming a known ideology (found '{declared}')", where));
                    continue;
                }
                if (scope.Attributes.TryGetValue("support_weight", out var rawWeight)
                    && (!double.TryParse(rawWeight, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight)
                        || !double.IsFinite(weight) || weight <= 0))
                    problems.Add(($"Party '{scope.Key}' has invalid support_weight '{rawWeight}' (expected a positive number)", where));
            }
            else if ((IsPartyFaction(scope) || IsPartyBranch(scope)) && !IsParty(hierarchy.ParentOf(scope)))
            {
                problems.Add(($"Scope '{scope.Key}' of kind '{scope.Kind}' must have a party as its parent", where));
            }

            ValidateFactions(scope, where, problems);
            if (IsPartyStructure(scope) && scope.Attributes.TryGetValue(LeaderAttribute, out var leaderKey)
                && !string.IsNullOrWhiteSpace(leaderKey) && _game.World.GetCharacter(leaderKey) == null)
                problems.Add(($"Political structure '{scope.Key}' references unknown character '{leaderKey}' in data = {{ leader = ... }}", where));

            if (scope.Attributes.TryGetValue(BranchStateAttribute, out var stateKey) && !string.IsNullOrWhiteSpace(stateKey))
            {
                if (!IsPartyBranch(scope))
                    problems.Add(($"Only a party_branch may declare data = {{ state = ... }} (found on '{scope.Key}')", where));
                else if (_game.World.GetState(stateKey) == null)
                    problems.Add(($"Political branch '{scope.Key}' references unknown state '{stateKey}' in data = {{ state = ... }}", where));
            }
        }
        return problems;
    }

    /// <summary>Logs every problem from <see cref="FindPartyProblems"/>; called with the other script checks.</summary>
    public void ValidateParties()
    {
        foreach (var (message, where) in FindPartyProblems()) Log.Error(message, where);
    }

    private void RegisterPartyScript()
    {
        var s = _game.Script;
        string IdeologyName(string? key) => Ideology(key)?.Name ?? key ?? "?";

        RegisterFactionScript();
        s.RegisterValue("party_popularity", (ctx, x) => PartyPopularity(x));
        s.RegisterValue("party_seats", (ctx, x) => PartySeats(x));
        s.RegisterEffect("add_party_support", (ctx, n) =>
        {
            GenericScope? party = n.GetString("party") is { } key
                ? _game.World.GetGenericScope(key)
                : ctx.This as GenericScope;
            if (party == null || !IsParty(party)) return;
            AddPartySupport(party, s.ResolveNumber(n.GetString("value") ?? n.GetString("support"), ctx));
        }, (ctx, n) => $"Change party support by {ScriptEngine.FormatSigned(100 * s.ResolveNumber(n.GetString("value") ?? n.GetString("support"), ctx))}%");

        s.RegisterTrigger("party_ideology",
            (ctx, n) => ctx.This is GenericScope party && IsParty(party)
                && string.Equals(IdeologyOfParty(party), n.Value, StringComparison.OrdinalIgnoreCase),
            (ctx, n) => $"The party stands for {IdeologyName(n.Value)}");
        s.RegisterTrigger("party_in_government",
            (ctx, n) => IsParty(ctx.This) && PartyInGovernment(ctx.This) == n.AsBool(true),
            (ctx, n) => n.AsBool(true) ? "The party is in government" : "The party is not in government");
        s.RegisterTrigger("has_party",
            (ctx, n) => ctx.This is Country country && (PartyOf(country, n.Value ?? "") != null
                || PartiesOf(country).Any(party => party.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase))),
            (ctx, n) => $"Has an organized {IdeologyName(IdeologyKeyOfParty(n.Value) ?? n.Value)} party");

        s.RegisterLink("party_country", (ctx, x) => PartyCountry(x));
        s.RegisterValue("party_support_weight", (ctx, x) => x is GenericScope p && IsParty(p) ? PartySupportWeight(p.Key) : null);
        s.RegisterLink("party_leader", (ctx, x) => LeaderOf(x));
        s.RegisterLink("party_branch_state", (ctx, x) => StateOfBranch(x));
        s.RegisterTrigger("party_has_leader", (ctx, n) => IsPartyStructure(ctx.This) && (LeaderOf(ctx.This) != null) == n.AsBool(true),
            (ctx, n) => n.AsBool(true) ? "Has a named political leader" : "Has no named political leader");
        s.RegisterTrigger("party_led_by", (ctx, n) => LeaderOf(ctx.This) is { } leader && string.Equals(leader.Key, n.Value, StringComparison.OrdinalIgnoreCase),
            (ctx, n) => $"Led by {(_game.World.GetCharacter(n.Value)?.DisplayName ?? n.Value)}");
        s.RegisterIterator("party", (ctx, x) => x is Country country ? PartiesOf(country).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("party_faction", (ctx, x) => x is GenericScope party && IsParty(party) ? PartyUnitsOf(party, PartyFactionKind).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("party_branch", (ctx, x) => x is GenericScope party && IsParty(party) ? PartyUnitsOf(party, PartyBranchKind).ToList() : Enumerable.Empty<IScope>());
    }
}
