using System.Collections.Immutable;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Geography;
using Burger.Core.Politics;
using Burger.Core.Population;

namespace Burger.Core.Simulation;

/// <summary>
/// Immutable render-facing state. It contains values only; no domain object or mutable collection is retained.
/// The simulation owns the source Game and publishes one completed snapshot at a tick boundary.
/// </summary>
public sealed record RenderSnapshot(
    long Tick,
    GameDate Date,
    float HourFraction,
    int VisibilityVersion,
    int ExplorationVersion,
    bool ExplorationEnabled,
    ImmutableArray<int> NewlyChartedLocations,
    bool GeoEnabled,
    int GeoVersion,
    int Speed,
    bool Paused,
    double ActualDaysPerSecond,
    string MapContentKey,
    string? PlayerTag,
    RenderPlayer? Player,
    ImmutableArray<RenderCountry> Countries,
    ImmutableArray<RenderCharacter> Characters,
    ImmutableArray<RenderLocation> Locations,
    ImmutableArray<RenderState> States,
    ImmutableArray<RenderDivision> Divisions,
    ImmutableArray<RenderArmySummary> Armies,
    ImmutableArray<RenderSite> Sites,
    ImmutableArray<RenderFleet> Fleets,
    ImmutableArray<RenderAirWing> AirWings,
    ImmutableArray<RenderTradeRoute> BlockedTradeRoutes,
    ImmutableArray<RenderTradeTransaction> RecentTrades,
    ImmutableArray<RenderResource> Resources,
    ImmutableArray<RenderCommodityBalance> CommodityBalances,
    ImmutableArray<RenderDecision> Decisions,
    ImmutableArray<RenderFocus> Focuses,
    string? CurrentFocusKey,
    double CurrentFocusProgress,
    ImmutableArray<RenderTechnology> Technologies,
    ImmutableArray<RenderResearchSlot> ResearchSlots,
    double TechnologyPoints,
    int TechnologySlotCount,
    int KnownTechnologyCount,
    ImmutableArray<RenderProductionLine> ProductionLines,
    ImmutableArray<RenderConstructionItem> ConstructionQueue,
    ImmutableArray<RenderStockpileItem> Stockpile,
    ImmutableArray<RenderArmyOrder> ArmyOrders,
    ImmutableArray<RenderNotification> Notifications,
    ImmutableArray<RenderWar> Wars,
    ImmutableArray<RenderDiplomacyRelation> DiplomacyRelations,
    ImmutableArray<RenderParticleEvent> ParticleEvents,
    ImmutableArray<RenderPoliticsOverview> PoliticsOverviews,
    ImmutableArray<RenderStatecraftBanner> StatecraftBanners)
{
    public static RenderSnapshot Empty { get; } = new(
        0, default, 0, 0, 0, false, ImmutableArray<int>.Empty, false, 0, 0, true, 0, "", null, null,
        ImmutableArray<RenderCountry>.Empty,
        ImmutableArray<RenderCharacter>.Empty,
        ImmutableArray<RenderLocation>.Empty,
        ImmutableArray<RenderState>.Empty,
        ImmutableArray<RenderDivision>.Empty,
        ImmutableArray<RenderArmySummary>.Empty,
        ImmutableArray<RenderSite>.Empty,
        ImmutableArray<RenderFleet>.Empty,
        ImmutableArray<RenderAirWing>.Empty,
        ImmutableArray<RenderTradeRoute>.Empty,
        ImmutableArray<RenderTradeTransaction>.Empty,
        ImmutableArray<RenderResource>.Empty,
        ImmutableArray<RenderCommodityBalance>.Empty,
        ImmutableArray<RenderDecision>.Empty,
        ImmutableArray<RenderFocus>.Empty,
        null,
        0,
        ImmutableArray<RenderTechnology>.Empty,
        ImmutableArray<RenderResearchSlot>.Empty,
        0,
        0,
        0,
        ImmutableArray<RenderProductionLine>.Empty,
        ImmutableArray<RenderConstructionItem>.Empty,
        ImmutableArray<RenderStockpileItem>.Empty,
        ImmutableArray<RenderArmyOrder>.Empty,
        ImmutableArray<RenderNotification>.Empty,
        ImmutableArray<RenderWar>.Empty,
        ImmutableArray<RenderDiplomacyRelation>.Empty,
        ImmutableArray<RenderParticleEvent>.Empty,
        ImmutableArray<RenderPoliticsOverview>.Empty,
        ImmutableArray<RenderStatecraftBanner>.Empty);
}

public readonly record struct RenderModifier(string Name, string Description, string Details, string? Expires);
public readonly record struct RenderPopMood(string Name, double Size, string Reaction, double Opinion, string Objective, double Grievance, double Radicalism, string PreferredParty);
public readonly record struct RenderInstitution(string Key, string Name, double Presence, bool Adopted);
public readonly record struct RenderOpinionModifier(string Name, double Value);
public readonly record struct RenderCountryRelation(int Kind, string Tag, string Name, double LibertyDesire);
public readonly record struct RenderCharacterStat(string Key, double Value);
public readonly record struct RenderCharacterSummary(string Name, int Age, ImmutableArray<RenderCharacterStat> Stats, ImmutableArray<string> Traits);
public readonly record struct RenderCharacter(int Id, string Name, string? CountryTag, int Age, bool Female, bool Alive, double Loyalty, double Health,
    ImmutableArray<RenderCharacterStat> Stats, ImmutableArray<string> Traits);
public readonly record struct RenderCountry(int Index, string Tag, string Name, Rgb Color, bool Exists, bool Playable, bool IsPlayer, int OwnedStateCount, int LocationCount, int DivisionCount, int WarCount, Rgb IdeologyColor, Rgb SphereColor, bool IsSpherePower, ImmutableArray<RenderResource> Resources, int DiplomaticRelationsUsed, int DiplomaticRelationsMax, ImmutableArray<RenderModifier> Modifiers, ImmutableArray<RenderCountryRelation> Relations, RenderCharacterSummary? Ruler, RenderCharacterSummary? Heir, string Government, string? PrimaryContinentName);
public readonly record struct RenderState(string Key, string? OwnerTag, double Tax, double Manpower,
    ImmutableArray<string> CoreTags, ImmutableArray<string> ClaimTags, ImmutableArray<RenderBuilding> Buildings,
    int DevelopmentLevel, int DevelopmentStartLevel, int DevelopmentGained, double Literacy, double Urbanization,
    double PopulationTotal, double PopulationGrowth, double PopulationCapacity, ImmutableArray<RenderModifier> Modifiers,
    ImmutableArray<RenderPopMood> Moods, ImmutableArray<RenderPopulationShare> CultureBreakdown,
    ImmutableArray<RenderPopulationShare> ReligionBreakdown, ImmutableArray<RenderPopulationShare> ClassBreakdown,
    ImmutableArray<RenderInstitution> Institutions);
public readonly record struct RenderBuilding(string Key, int Level, double Damaged);
public readonly record struct RenderRequirement(string Text, bool Ok);
public readonly record struct RenderDecision(string Key, string Name, string Desc, string Category, bool CanTake,
    ImmutableArray<RenderRequirement> Requirements, ImmutableArray<string> Effects);
public readonly record struct RenderFocus(string Key, string Name, string Desc, string TreeKey, int X, int Y, int Days,
    ImmutableArray<string> Prerequisite, ImmutableArray<string> Requires, ImmutableArray<string> MutuallyExclusive,
    bool Done, bool Busy, bool CanStart, string? BlockReason, ImmutableArray<RenderRequirement> Requirements, ImmutableArray<string> Reward);
public readonly record struct RenderTechnology(string Key, string Name, string Desc, string Category, double Year, int Order,
    bool Known, bool Busy, string? BlockReason, ImmutableArray<string> Requires, ImmutableArray<string> Unlocks);
public readonly record struct RenderResearchSlot(string Key, string Name, double Progress, double Cost);
public readonly record struct RenderProductionLine(string EquipmentKey, string EquipmentName, bool UsesDockyards, int Factories,
    double Efficiency, double LastOutput, double Cost, double Stock);
public readonly record struct RenderConstructionItem(string BuildingKey, string BuildingName, string StateKey, string StateName,
    double Progress, double Cost, int Factories);
public readonly record struct RenderStockpileItem(string Key, string Name, double Stock, double ProducedPerDay, double Shortage);
public readonly record struct RenderPopulationShare(string Key, string Name, Rgb Color, double Size, bool Primary, bool Accepted);
public readonly record struct RenderPlayer(string Tag, string Name, string RulerName, Rgb Color, double Gold, double LastIncome, double LastExpenses,
    double Manpower, double LastManpowerGain, int CivilianFactories, int MilitaryFactories, int Dockyards, int Divisions, int Recruitment,
    int Construction, int AirOrders, int NavyOrders, double HomeAirShare, bool HomeAirContested,
    int EnvoysFree, int EnvoysTotal, int EnvoysBusy, int NextEnvoyBackIn,
    double Population, double PopulationGrowth, double HomePopulation, double AcceptedCultureShare, double StateReligionShare,
    int MobilisedDaysLeft, bool HasLevies, ImmutableArray<RenderPopulationShare> CultureBreakdown,
    ImmutableArray<RenderPopulationShare> ReligionBreakdown, ImmutableArray<RenderPopulationShare> ClassBreakdown, int IdleDivisions);
public readonly record struct RenderPoliticsOverview(string CountryTag, bool Enabled, string PowerName, double PoliticalPower,
    double Stability, double GovernmentSupport, string RulingOrganization, int TotalSeats, bool HoldsElections,
    GameDate? NextElection, GameDate? LastElection, ImmutableArray<RenderPoliticsParty> Parties,
    double PopulationOpinion, double PopulationUnrest, double PopulationUnderground, ImmutableArray<RenderPopMood> PopGroups);
public readonly record struct RenderPoliticsParty(string Key, string Name, Rgb Color, string IdeologyName, string? LeaderName,
    double Popularity, int Seats, bool Banned, bool Ruling, bool Governing);
public readonly record struct RenderStatecraftBanner(string Text, string Tip, int Tab, int Tone);

public readonly record struct RenderLocation(
    int Index,
    string Key,
    string? StateKey,
    string? OwnerTag,
    string? ControllerTag,
    int StateIndex,
    string TerrainName,
    bool IsCoastal,
    string? ContinentName,
    bool ContinentIsOcean,
    string? StateName,
    int StateLocationCount,
    string? OwnerName,
    string? ControllerName,
    double Tax,
    double Manpower,
    double SiegeProgress,
    string? SiegerTag,
    Rgb TerrainColor,
    Rgb ContinentColor,
    Rgb CultureColor,
    Rgb ReligionColor,
    double DevelopmentLevel,
    Rgb TitleColor,
    double SupplyValue,
    double AirShare,
    bool AirContested,
    int AirRegionIndex,
    bool HasGeoData,
    Rgb GeoTimeColor,
    Rgb GeoProvinceColor,
    int GeoProvinceIndex,
    bool GeoTimeHatched,
    int RiverBorders,
    int Fort,
    int DivisionCount,
    bool IsWater,
    bool IsLake,
    bool IsOccupied,
    Rgb MapColor,
    bool IsCharted,
    float CenterX,
    float CenterY,
    ImmutableArray<RenderModifier> Modifiers);

public readonly record struct RenderDivision(
    int Id,
    string OwnerTag,
    string LocationKey,
    float CenterX,
    float CenterY,
    float NextCenterX,
    float NextCenterY,
    bool HasNextLocation,
    bool IsWater,
    string ModelKey,
    string Name,
    string TemplateName,
    string LocationName,
    string? ArmyName,
    double StrengthValue,
    double MaxStrength,
    double OrganisationValue,
    double MaxOrganisation,
    double Strength,
    double Organisation,
    double Supply,
    ImmutableArray<RenderPoint> Route,
    bool InBattle,
    bool IsMoving,
    bool Retreating);
public readonly record struct RenderArmySummary(int Id, string Name, string OwnerTag, string? GeneralName, double GeneralStat, int DivisionCount, double StrengthFraction, string Mode, string? FrontEnemyTag, string? FrontEnemyName, string? ObjectiveName, int FallbackCount, string? MoveTargetName, string? LocationName, double Supply);

public readonly record struct RenderSite(string LocationKey, float CenterX, float CenterY, string ModelKey, bool IsVisible);
public readonly record struct RenderPoint(float X, float Y);
public readonly record struct RenderFleet(int Id, string Name, string OwnerTag, string LocationKey, string LocationName, string Composition, float CenterX, float CenterY,
    float NextCenterX, float NextCenterY, bool HasNextLocation, ImmutableArray<RenderPoint> Route, int Ships, double HpFraction, bool Retreating, bool InBattle, bool InPort);
public readonly record struct RenderAirWing(int Id, string Name, string OwnerTag, string BaseKey, string TypeName, float CenterX, float CenterY, double Planes, bool Active, string Mission, string? RegionName);
public readonly record struct RenderTradeRoute(float FromX, float FromY, float ToX, float ToY, string FromName, string ToName, double DistanceKm, double Capacity);
public readonly record struct RenderTradeTransaction(string Key, float FromX, float FromY, float ToX, float ToY, string Commodity, double Quantity, string Seller, string Buyer, string Kind, double DistanceKm, double TransportCost, double Price, double Tariff);
public readonly record struct RenderResource(string Name, double Value, double MonthlyChange, int Decimals, double? Min, double? Max, string Description);
public readonly record struct RenderCommodityBalance(string CountryTag, string CommodityKey, double Output, double Import, double Export,
    double Demand, double PopulationUse, double MilitaryUse, double Stockpile, double Price, double Satisfied);
public readonly record struct RenderFrontPair(float OwnX, float OwnY, float EnemyX, float EnemyY, ImmutableArray<RenderPoint> Border);
public readonly record struct RenderArmyOrder(int Id, string Name, string OwnerTag, string Mode, bool HasEnemy, ImmutableArray<RenderFrontPair> FrontPairs,
    ImmutableArray<RenderPoint> Fallback, ImmutableArray<RenderPoint> FrontLimit, bool HasObjective, float ObjectiveX, float ObjectiveY);
public readonly record struct RenderNotification(GameDate Date, string Text, NotificationKind Kind, string? LocationKey, float LocationX, float LocationY);
public readonly record struct RenderWarCountry(string Tag, string Name, Rgb Color, bool IsLeader, double Exhaustion);
public readonly record struct RenderWarGoal(string HolderTag, string HolderName, string TargetTag, string TargetName, string CasusBelliName, int GoalStateCount, double CostMultiplier, bool Ticking);
public readonly record struct RenderWar(int Id, string Name, string AttackerLeaderTag, string DefenderLeaderTag, string AttackerLeaderName, string DefenderLeaderName,
    ImmutableArray<RenderWarCountry> Attackers, ImmutableArray<RenderWarCountry> Defenders, ImmutableArray<RenderWarGoal> Goals,
    GameDate StartDate, double WarScore, double BattleScore, double TickingScore, int LastChangeDay, double MaxExhaustion, bool IsMine, bool CivilWar);
public readonly record struct RenderDiplomacyRelation(string FromTag, string ToTag, int Kind, double Opinion, double ReverseOpinion,
    bool AtWar, bool Allied, bool FromIsOverlord, bool ToIsOverlord, bool FromHasAccess, bool ToHasAccess,
    bool FromGuarantees, bool ToGuarantees, bool HasTruce, bool HasPact, double LibertyDesire, double WarExhaustion,
    double BaseOpinion, double IdeologyOpinion, double ReligionOpinion, double ColonialOpinion, ImmutableArray<RenderOpinionModifier> OpinionModifiers);
public readonly record struct RenderParticleEvent(string Effect, bool HasPosition, float X, float Y);

/// <summary>Builds a detached snapshot while the simulation thread exclusively owns the mutable Game.</summary>
public static class RenderSnapshotBuilder
{
    /// <summary>Updates the high-frequency map state without rebuilding economy and HUD projections.</summary>
    public static RenderSnapshot BuildFast(Game game, RenderSnapshot previous, long tick, float hourFraction)
    {
        var divisions = game.World.Divisions.Select(d =>
        {
            var next = d.Path.FirstOrDefault();
            bool hasNext = next != null && next.IsLand;
            var route = d.Path.Where(l => l.IsLand).Select(l => new RenderPoint(l.CenterX, l.CenterY)).ToImmutableArray();
            var model = game.Figures.SoldierFor(d);
            return new RenderDivision(d.Id, d.Owner.Tag, d.Location.Key, d.Location.CenterX, d.Location.CenterY,
                hasNext ? next!.CenterX : 0, hasNext ? next!.CenterY : 0, hasNext, d.Location.IsWater, model.Key,
                d.Name, d.Template.Name, d.Location.DisplayName, d.Army?.Name, d.Strength, d.MaxStrength, d.Org, d.MaxOrg,
                d.StrengthFraction, d.OrgFraction, d.Supply, route, d.InBattle, d.IsMoving, d.Retreating);
        }).ToImmutableArray();
        var armies = game.World.Armies.Select(a => new RenderArmySummary(a.Id, a.Name, a.Owner.Tag,
            a.General?.DisplayName, a.General != null ? a.General.GetStat(game.Data.Defines.GeneralStat) : 0,
            a.Divisions.Count, a.Divisions.Count == 0 ? 0 : a.Divisions.Average(d => d.StrengthFraction), a.Mode.ToString(),
            a.FrontEnemy?.Tag, a.FrontEnemy?.Name, a.Objective?.DisplayName, a.Fallback.Count,
            a.MoveTarget?.DisplayName, a.Divisions.FirstOrDefault()?.Location.DisplayName,
            a.Divisions.Count == 0 ? 0 : a.Divisions.Average(d => d.Supply))).ToImmutableArray();
        var constructionQueue = previous.ConstructionQueue;
        if (game.World.Player is { } productionPlayer && game.Features.Has(FeatureSet.Production))
        {
            constructionQueue = productionPlayer.Construction.Select(item => new RenderConstructionItem(
                item.Building.Key, item.Building.Name, item.State.Key, item.State.DisplayName,
                item.Progress, item.Building.Cost, item.Factories)).ToImmutableArray();
        }
        return previous with
        {
            Tick = tick,
            Date = game.World.Date,
            HourFraction = hourFraction,
            Speed = game.Clock.Speed,
            Paused = game.Clock.Paused,
            ActualDaysPerSecond = game.Clock.ActualDaysPerSecond,
            Divisions = divisions,
            Armies = armies,
            ConstructionQueue = constructionQueue
        };
    }

    public static RenderSnapshot Build(Game game, long tick, float hourFraction)
    {
        ArgumentNullException.ThrowIfNull(game);
        var divisionCounts = game.World.Divisions.GroupBy(d => d.Owner.Tag).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var warCounts = game.World.Wars
            .SelectMany(w => w.Attackers.Concat(w.Defenders))
            .GroupBy(c => c.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var countries = game.World.Countries
            .Select(c =>
            {
                RenderCharacterSummary? CharacterSummary(Character? character) => character == null ? null : new RenderCharacterSummary(
                    character.DisplayName, character.Age(game.World.Date),
                    game.Data.Defines.CharacterStats.Select(key => new RenderCharacterStat(key, character.GetStat(key))).ToImmutableArray(),
                    character.Traits.Select(t => Localisation.Get(t.Name)).ToImmutableArray());
                var sphere = game.GreatPowers.Enabled ? game.GreatPowers.IsGreat(c) ? c : game.GreatPowers.SphereOf(c) : null;
                var relationRows = new List<RenderCountryRelation>();
                foreach (var target in game.World.Countries)
                {
                    if (target == c || !target.Exists) continue;
                    if (game.World.AtWar(c, target)) relationRows.Add(new RenderCountryRelation(1, target.Tag, target.Name, 0));
                    if (game.World.Relations.AreAllied(c, target)) relationRows.Add(new RenderCountryRelation(2, target.Tag, target.Name, 0));
                    if (game.World.Relations.OverlordOf(target) == c) relationRows.Add(new RenderCountryRelation(3, target.Tag, target.Name, game.Diplomacy.LibertyDesire(target)));
                    if (game.World.Relations.OverlordOf(c) == target) relationRows.Add(new RenderCountryRelation(4, target.Tag, target.Name, game.Diplomacy.LibertyDesire(c)));
                    if (game.World.Relations.IsGuaranteeing(c, target)) relationRows.Add(new RenderCountryRelation(5, target.Tag, target.Name, 0));
                    if (game.World.Relations.IsGuaranteeing(target, c)) relationRows.Add(new RenderCountryRelation(6, target.Tag, target.Name, 0));
                    if (game.World.Relations.HasAccess(target, c)) relationRows.Add(new RenderCountryRelation(7, target.Tag, target.Name, 0));
                }
                return new RenderCountry(c.Index, c.Tag, c.Name, c.Color, c.Exists, c.Playable, c.IsPlayer, c.OwnedStates.Count,
                    c.OwnedStates.Sum(s => s.Locations.Count), divisionCounts.GetValueOrDefault(c.Tag), warCounts.GetValueOrDefault(c.Tag),
                    game.Politics.Enabled && game.Politics.RulingIdeology(c) is { } ideology ? ideology.Color : Rgb.Black,
                    sphere?.Color ?? new Rgb(96, 98, 104), sphere != null && game.GreatPowers.IsGreat(c),
                    game.Resources.Defs.Where(d => d.Scope == ScopeType.Country)
                        .Select(d => new RenderResource(d.Name, game.Resources.Get(c, d), game.Resources.MonthlyChange(c, d), d.Decimals, d.Min, d.Max, d.Desc))
                        .ToImmutableArray(), game.Diplomacy.RelationsUsed(c), game.Diplomacy.MaxRelations(c),
                    game.Modifiers.Of(c).Where(m => !m.Def.Hidden).Select(m => new RenderModifier(
                    Localisation.Get(m.Def.Name), Localisation.Get(m.Def.Desc), game.Modifiers.Describe(m.Def), m.Expires?.ToDisplay())).ToImmutableArray(), relationRows.ToImmutableArray(),
                    CharacterSummary(c.Ruler), CharacterSummary(c.Heir), c.Government, c.PrimaryContinent?.DisplayName);
            })
            .ToImmutableArray();
        var characters = game.World.Characters
            .Select(ch => new RenderCharacter(ch.Id, ch.DisplayName, ch.Country?.Tag, ch.Age(game.World.Date), ch.Female, ch.Alive, ch.Loyalty, ch.Health,
                game.Data.Defines.CharacterStats.Select(key => new RenderCharacterStat(key, ch.GetStat(key))).ToImmutableArray(),
                ch.Traits.Select(t => Localisation.Get(t.Name)).ToImmutableArray()))
            .ToImmutableArray();
        var politicsOverviews = game.Politics.Enabled
            ? game.World.Countries.Where(c => c.Exists).Select(c =>
            {
                var politics = game.Politics.Of(c);
                var partyKeys = game.Politics.PartyKeysOf(c)
                    .Union(politics.PartyPopularity.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(key => politics.PartyPopularity.GetValueOrDefault(key))
                    .ThenByDescending(key => politics.PartySeats.GetValueOrDefault(key))
                    .ThenBy(key => game.Politics.PartyDisplayName(c, key), StringComparer.OrdinalIgnoreCase);
                var parties = partyKeys.Select(key =>
                {
                    var ideology = game.Politics.Ideology(game.Politics.IdeologyKeyOfParty(key));
                    var leader = game.Politics.LeaderOf(game.World.GetGenericScope(key));
                    return new RenderPoliticsParty(key, game.Politics.PartyDisplayName(c, key), game.Politics.PartyColor(key),
                        ideology?.Name ?? "Unaligned", leader?.DisplayName,
                        politics.PartyPopularity.GetValueOrDefault(key), politics.PartySeats.GetValueOrDefault(key),
                        game.Politics.IsPartyBanned(c, key),
                        string.Equals(politics.RulingOrganization, key, StringComparison.OrdinalIgnoreCase),
                        politics.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase));
                }).ToImmutableArray();
                var governmentSupport = politics.GovernmentOrganizations
                    .Where(key => !PoliticsSystem.IsMinorityParty(key))
                    .Sum(key => politics.PartyPopularity.GetValueOrDefault(key));
                var popSummary = game.Politics.PopsEnabled ? game.Politics.PopSummaryOf(c) : null;
                var popGroups = game.Politics.PopsEnabled
                    ? game.Politics.PopGroupRows(c).Take(7).Select(m => new RenderPopMood(
                        m.Name, m.Size, m.Reaction, m.Opinion, m.Objective, m.Grievance, m.Radicalism, m.PreferredParty)).ToImmutableArray()
                    : ImmutableArray<RenderPopMood>.Empty;
                return new RenderPoliticsOverview(c.Tag, true, game.Politics.PowerName, game.Politics.PowerOf(c),
                    game.Rules.Politics.StabilityOf(game, c), governmentSupport, politics.RulingOrganization,
                    politics.PartySeats.Values.Sum(), game.Politics.HoldsElections(c), politics.NextElection, politics.LastElection, parties,
                    popSummary?.AverageOpinion ?? 0, popSummary?.UnrestShare ?? 0,
                    popSummary?.Underground.Values.Sum() ?? 0, popGroups);
            }).ToImmutableArray()
            : ImmutableArray<RenderPoliticsOverview>.Empty;
        var statecraftBanners = game.World.Player is { } bannerPlayer
            ? BuildStatecraftBanners(game, bannerPlayer)
            : ImmutableArray<RenderStatecraftBanner>.Empty;
        RenderPlayer? playerSnapshot = null;
        if (game.World.Player is { } player)
        {
            int civ = game.Features.Has(FeatureSet.Production) ? game.Production.CivilianFactories(player) : 0;
            int mil = game.Features.Has(FeatureSet.Production) ? game.Production.MilitaryFactories(player) : 0;
            int dock = game.Features.Has(FeatureSet.Production) ? game.Production.Dockyards(player) : 0;
            var envoys = game.Envoys.Enabled ? (game.Envoys.Free(player), game.Envoys.Total(player), game.Envoys.Busy(player), game.Envoys.NextBackIn(player)) : (0, 0, 0, 0);
            int mobilisedDaysLeft = game.Raising.Enabled && game.Raising.IsMobilised(player) ? game.Raising.MobilisedDaysLeft(player) : 0;
            bool hasLevies = game.Raising.Enabled && game.Raising.HasLevies(player);
            double homeAirShare = 0;
            bool homeAirContested = false;
            if (game.Features.Has(FeatureSet.Air) && player.Capital?.State is { } homeState && game.Air.RegionOf(homeState) is { } homeRegion)
                (homeAirShare, homeAirContested) = game.Air.Superiority(homeRegion, player);
            double population = 0, populationGrowth = 0, homePopulation = 0, acceptedCultureShare = 0, stateReligionShare = 0;
            var cultureBreakdown = ImmutableArray<RenderPopulationShare>.Empty;
            var religionBreakdown = ImmutableArray<RenderPopulationShare>.Empty;
            var classBreakdown = ImmutableArray<RenderPopulationShare>.Empty;
            if (game.Population.Enabled)
            {
                population = game.Population.TotalOf(player);
                var populationStates = player.OwnedStates.Where(game.Population.HasPops).ToList();
                populationGrowth = populationStates.Sum(s => game.Population.Of(s).LastGrowth);
                homePopulation = game.Population.HomeStates(player).Sum(game.Population.TotalOf);
                var shares = game.Population.Shares(player);
                acceptedCultureShare = shares.Accepted;
                stateReligionShare = shares.Faithful;
                var homeStates = game.Population.HomeStates(player).ToList();
                var primaryCulture = game.Population.Of(player).Culture;
                var primaryReligion = game.Population.Of(player).Religion;
                cultureBreakdown = game.Population.Breakdown(homeStates, p => p.Culture).Select(x =>
                    new RenderPopulationShare(x.Key, game.Population.Culture(x.Key)?.Name ?? x.Key, game.Population.Culture(x.Key)?.Color ?? Rgb.Gray,
                        x.Size, x.Key.Equals(primaryCulture, StringComparison.OrdinalIgnoreCase), game.Population.IsAccepted(player, x.Key))).ToImmutableArray();
                religionBreakdown = game.Population.ReligionEnabled
                    ? game.Population.Breakdown(homeStates, p => p.Religion).Select(x =>
                        new RenderPopulationShare(x.Key, game.Population.Religion(x.Key)?.Name ?? x.Key, game.Population.Religion(x.Key)?.Color ?? Rgb.Gray,
                            x.Size, x.Key.Equals(primaryReligion, StringComparison.OrdinalIgnoreCase), true)).ToImmutableArray()
                    : ImmutableArray<RenderPopulationShare>.Empty;
                classBreakdown = game.Population.ClassesEnabled
                    ? game.Population.Breakdown(homeStates, p => p.Type).Select(x =>
                        new RenderPopulationShare(x.Key, game.Population.PopTypeByKey.TryGetValue(x.Key, out var type) ? type.Name : x.Key,
                            game.Population.PopTypeByKey.TryGetValue(x.Key, out type) ? type.Color : Rgb.Gray, x.Size, false, true)).ToImmutableArray()
                    : ImmutableArray<RenderPopulationShare>.Empty;
            }
            playerSnapshot = new RenderPlayer(player.Tag, player.Name, player.Ruler?.DisplayName ?? "", player.Color, player.Gold, player.LastIncome,
                player.LastExpenses, player.Manpower, player.LastManpowerGain, civ, mil, dock, game.World.Divisions.Count(d => d.Owner == player),
                player.Recruitment.Count, player.Construction.Count,
                game.Features.Has(Burger.Core.Modding.FeatureSet.Air) ? game.Air.OrdersOf(player).Count : 0,
                game.Features.Has(Burger.Core.Modding.FeatureSet.Navy) ? game.Navy.OrdersOf(player).Count : 0,
                homeAirShare, homeAirContested,
                envoys.Item1, envoys.Item2, envoys.Item3, envoys.Item4,
                population, populationGrowth, homePopulation, acceptedCultureShare, stateReligionShare,
                mobilisedDaysLeft, hasLevies, cultureBreakdown, religionBreakdown, classBreakdown,
                game.World.Divisions.Count(d => d.Owner == player && d.VolunteerFrom == null && !d.InBattle));
        }
        int geoDay = game.World.Date.TotalDays;
        var geoProvinceIndices = game.Geo.Enabled
            ? game.Geo.Snapshot(geoDay).Provinces.Select((p, i) => (p.Id, Index: i + 1)).ToDictionary(x => x.Id, x => x.Index, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        (bool Has, Rgb Time, Rgb Province, int Index, bool Hatched) Geo(Location l)
        {
            if (!game.Geo.Enabled || !l.IsLand) return (false, Rgb.Black, Rgb.Black, 0, false);
            var border = BorderView.At(game.Geo, l.Key, geoDay);
            var owner = border?.Owner is { } ownerTag ? game.World.GetCountry(ownerTag) : null;
            var province = game.Geo.ProvinceOfAtom(l.Key, geoDay);
            int index = province != null && geoProvinceIndices.TryGetValue(province.Id, out var provinceIndex) ? provinceIndex : 0;
            var provinceOwner = province != null && game.Geo.Owner(province.Id, geoDay) is { } provinceOwnerTag ? game.World.GetCountry(provinceOwnerTag) : null;
            Rgb provinceColor = provinceOwner?.Color ?? new Rgb(120, 120, 120);
            if (index > 0)
            {
                double shade = 0.78 + 0.22 * ((index * 0.6180339) % 1);
                provinceColor = new Rgb((int)(provinceColor.R * shade), (int)(provinceColor.G * shade), (int)(provinceColor.B * shade));
            }
            return (true, owner?.Color ?? new Rgb(120, 120, 120), provinceColor, index, border != null && BorderControl.Hatched(border.Control));
        }
        var locationDivisionCounts = game.World.Divisions.GroupBy(d => d.Location.Index).ToDictionary(g => g.Key, g => g.Count());
        var locations = game.World.Locations
            .Select(l =>
            {
                var geo = Geo(l);
                return new RenderLocation(l.Index, l.Key, l.State?.Key, l.Owner?.Tag, l.Controller?.Tag, l.State?.Index ?? -1,
                l.Terrain.Name, l.IsCoastal, l.Continent?.DisplayName, l.Continent?.IsOcean == true, l.State?.DisplayName, l.State?.Locations.Count ?? 0,
                l.Owner?.Name, l.Controller?.Name, l.State?.Tax ?? 0, l.State?.Manpower ?? 0, l.SiegeProgress, l.Sieger?.Tag,
                l.Terrain.DisplayColor, l.Continent?.Color ?? Rgb.Black,
                game.Population.Enabled && l.State != null && game.Population.Culture(game.Population.MajorityCulture(l.State)) is { } culture ? culture.Color : Rgb.Black,
                game.Population.Enabled && l.State != null && game.Population.Religion(game.Population.MajorityReligion(l.State)) is { } religion ? religion.Color : Rgb.Black,
                game.Development.Enabled && l.State != null ? game.Development.Level(l.State) : 0,
                game.Titles.Enabled && l.State != null && game.Titles.VassalOf(l.State) is { } vassal ? vassal.Country?.Color ?? Rgb.Black : l.Owner?.Color ?? Rgb.Black,
                game.World.Player is { } mapPlayer && game.Features.HasSupply && l.IsLand ? game.Supply.SupplyAt(mapPlayer, l) : 0,
                game.World.Player is { } airPlayer && game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && l.IsLand && game.Air.RegionOf(l) is { } airRegion
                    ? game.Air.Superiority(airRegion, airPlayer).Share : 0,
                game.World.Player is { } airPlayer2 && game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && l.IsLand && game.Air.RegionOf(l) is { } contestedRegion
                    ? game.Air.Superiority(contestedRegion, airPlayer2).Contested : false,
                game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && l.IsLand && game.Air.RegionOf(l) is { } indexedRegion ? indexedRegion.Index + 1 : 0,
                geo.Has, geo.Time, geo.Province, geo.Index, geo.Hatched,
                l.Neighbors.Count(a => a.River), l.Fort, locationDivisionCounts.GetValueOrDefault(l.Index),
                l.IsWater, l.IsLake, l.IsOccupied, l.MapColor, game.IsVisible(l), l.CenterX, l.CenterY,
                game.Modifiers.Of(l).Where(m => !m.Def.Hidden).Select(m => new RenderModifier(
                    Localisation.Get(m.Def.Name), Localisation.Get(m.Def.Desc), game.Modifiers.Describe(m.Def), m.Expires?.ToDisplay())).ToImmutableArray());
            })
            .ToImmutableArray();
        ImmutableArray<RenderPopulationShare> StatePopulationBreakdown(State state, Func<Pop, string?> selector, Func<string, (string Name, Rgb Color)> info, string? primary)
            => game.Population.Breakdown(new[] { state }, selector).Select(x =>
                new RenderPopulationShare(x.Key, info(x.Key).Name, info(x.Key).Color, x.Size,
                    x.Key.Equals(primary, StringComparison.OrdinalIgnoreCase), true)).ToImmutableArray();
        var states = game.World.States
            .Select(s => new RenderState(s.Key, s.Owner?.Tag, s.Tax, s.Manpower,
                s.Cores.Select(c => c.Tag).ToImmutableArray(), s.Claims.Select(c => c.Tag).ToImmutableArray(),
                s.Buildings.OrderBy(b => b.Key).Select(b => new RenderBuilding(b.Key, b.Value, s.Damaged.GetValueOrDefault(b.Key))).ToImmutableArray(),
                game.Development.Enabled ? game.Development.Level(s) : 0,
                game.Development.Enabled ? game.Development.StartLevel(s) : 0,
                game.Development.Enabled ? game.Development.Gained(s) : 0,
                game.Development.Enabled ? game.Development.Literacy(s) : 0,
                game.Development.Enabled ? game.Development.Urbanization(s) : 0,
                game.Population.Enabled ? game.Population.TotalOf(s) : 0,
                game.Population.Enabled ? game.Population.Of(s).LastGrowth : 0,
                game.Population.Enabled ? game.Population.Capacity(s) : 0,
                game.Modifiers.Of(s).Where(m => !m.Def.Hidden).Select(m => new RenderModifier(
                    Localisation.Get(m.Def.Name), Localisation.Get(m.Def.Desc), game.Modifiers.Describe(m.Def), m.Expires?.ToDisplay())).ToImmutableArray(),
                game.Politics.PopsEnabled && s.Owner != null
                    ? game.Politics.PopGroupRows(s.Owner, new[] { s }).Take(3).Select(m => new RenderPopMood(
                        m.Name, m.Size, m.Reaction, m.Opinion, m.Objective, m.Grievance, m.Radicalism, m.PreferredParty)).ToImmutableArray()
                    : ImmutableArray<RenderPopMood>.Empty,
                game.Population.Enabled ? StatePopulationBreakdown(s, p => p.Culture,
                    key => (game.Population.Culture(key)?.Name ?? key, game.Population.Culture(key)?.Color ?? Rgb.Gray),
                    game.Population.MajorityCulture(s)) : ImmutableArray<RenderPopulationShare>.Empty,
                game.Population.Enabled && game.Population.ReligionEnabled ? StatePopulationBreakdown(s, p => p.Religion,
                    key => (game.Population.Religion(key)?.Name ?? key, game.Population.Religion(key)?.Color ?? Rgb.Gray),
                    game.Population.MajorityReligion(s)) : ImmutableArray<RenderPopulationShare>.Empty,
                game.Population.Enabled && game.Population.ClassesEnabled ? StatePopulationBreakdown(s, p => p.Type,
                    key => (game.Population.PopTypeByKey.TryGetValue(key, out var type) ? type.Name : key,
                        game.Population.PopTypeByKey.TryGetValue(key, out type) ? type.Color : Rgb.Gray), null) : ImmutableArray<RenderPopulationShare>.Empty,
                game.Development.Enabled ? game.Development.Institutions.Where(i => game.Development.HasAppeared(i)).Select(i =>
                    new RenderInstitution(i.Key, i.Name, game.Development.Presence(i, s), game.Development.Adopted(i, s))).ToImmutableArray()
                    : ImmutableArray<RenderInstitution>.Empty))
            .ToImmutableArray();
        var divisions = game.World.Divisions
            .Select(d =>
            {
                var next = d.Path.FirstOrDefault();
                bool hasNext = next != null && next.IsLand;
                var route = d.Path.Where(l => l.IsLand).Select(l => new RenderPoint(l.CenterX, l.CenterY)).ToImmutableArray();
                var model = game.Figures.SoldierFor(d);
                return new RenderDivision(d.Id, d.Owner.Tag, d.Location.Key, d.Location.CenterX, d.Location.CenterY,
                    hasNext ? next!.CenterX : 0, hasNext ? next!.CenterY : 0, hasNext, d.Location.IsWater, model.Key,
                    d.Name, d.Template.Name, d.Location.DisplayName, d.Army?.Name, d.Strength, d.MaxStrength, d.Org, d.MaxOrg,
                    d.StrengthFraction, d.OrgFraction, d.Supply, route, d.InBattle, d.IsMoving, d.Retreating);
            })
            .ToImmutableArray();
        var armySummaries = game.World.Armies.Select(a => new RenderArmySummary(a.Id, a.Name, a.Owner.Tag,
            a.General?.DisplayName, a.General != null ? a.General.GetStat(game.Data.Defines.GeneralStat) : 0,
            a.Divisions.Count, a.Divisions.Count == 0 ? 0 : a.Divisions.Average(d => d.StrengthFraction), a.Mode.ToString(),
            a.FrontEnemy?.Tag, a.FrontEnemy?.Name, a.Objective?.DisplayName, a.Fallback.Count,
            a.MoveTarget?.DisplayName, a.Divisions.FirstOrDefault()?.Location.DisplayName,
            a.Divisions.Count == 0 ? 0 : a.Divisions.Average(d => d.Supply))).ToImmutableArray();
        var sites = game.Sites.Sites
            .Where(s => s.Model != null)
            .Select(s => new RenderSite(s.Location.Key, s.Location.CenterX, s.Location.CenterY, s.Model!.Key, game.IsVisible(s.Location)))
            .ToImmutableArray();
        var fleets = game.Navy.Fleets
            .Where(f => f.Ships.Count > 0)
            .Select(f =>
            {
                var next = f.Path.FirstOrDefault();
                bool hasNext = next != null;
                var route = f.Path.Select(l => new RenderPoint(l.CenterX, l.CenterY)).ToImmutableArray();
                string composition = string.Join(", ", f.Ships.GroupBy(s => s.Type).Select(g => $"{g.Count()} {g.Key.Name}"));
                return new RenderFleet(f.Id, f.Name, f.Owner.Tag, f.Location.Key, f.Location.DisplayName, composition, f.Location.CenterX, f.Location.CenterY,
                    hasNext ? next!.CenterX : 0, hasNext ? next!.CenterY : 0, hasNext, route, f.Ships.Count, f.HpFraction,
                    f.Retreating, f.Battle != null, f.InPort);
            })
            .ToImmutableArray();
        var airWings = game.Air.Wings
            .Select(w => new RenderAirWing(w.Id, w.Name, w.Owner.Tag, w.Base.Key, w.Type.Name, w.Base.Locations.Count == 0 ? 0 : w.Base.Locations.Average(l => l.CenterX),
                w.Base.Locations.Count == 0 ? 0 : w.Base.Locations.Average(l => l.CenterY), w.Planes, w.Active,
                w.Mission.ToString(), w.Region?.DisplayName))
            .ToImmutableArray();
        var blockedTradeRoutes = game.TradeNetwork.BlockadedRoutes
            .Where(r => r.From.Locations.Count > 0 && r.To.Locations.Count > 0)
            .Select(r => (From: r.From.Locations.FirstOrDefault(l => l.IsLand) ?? r.From.Locations[0], To: r.To.Locations.FirstOrDefault(l => l.IsLand) ?? r.To.Locations[0], Route: r))
            .Select(x => new RenderTradeRoute(x.From.CenterX, x.From.CenterY, x.To.CenterX, x.To.CenterY, x.Route.From.DisplayName, x.Route.To.DisplayName, x.Route.DistanceKm, x.Route.Capacity))
            .ToImmutableArray();
        var recentTrades = game.TradeNetwork.RecentHistory(24)
            .Where(t => t.From.Locations.Count > 0 && t.To.Locations.Count > 0)
            .Select((t, i) =>
            {
                var from = t.From.Locations.FirstOrDefault(l => l.IsLand) ?? t.From.Locations[0];
                var to = t.To.Locations.FirstOrDefault(l => l.IsLand) ?? t.To.Locations[0];
                return new RenderTradeTransaction($"{t.Date.TotalDays}:{i}:{t.Commodity}:{t.From.Index}:{t.To.Index}", from.CenterX, from.CenterY, to.CenterX, to.CenterY,
                    t.Commodity, t.Quantity, t.Seller.Name, t.Buyer.Name, t.RouteKind.ToString(), t.DistanceKm, t.TransportCost, t.Price, t.Tariff);
            })
            .ToImmutableArray();
        var resources = game.World.Player is { } resourcePlayer
            ? game.Resources.Defs.Where(d => d.TopBar && d.Scope == ScopeType.Country)
                .Select(d => new RenderResource(d.Name, game.Resources.Get(resourcePlayer, d), game.Resources.MonthlyChange(resourcePlayer, d), d.Decimals, d.Min, d.Max, d.Desc))
                .ToImmutableArray()
            : ImmutableArray<RenderResource>.Empty;
        var commodityBalances = game.Commodities.Defs.Values
            .Where(def => def.IsActive(game.World.Date))
            .SelectMany(def => game.World.Countries.Where(c => c.Exists).Select(country =>
            {
                var balance = game.Commodities.BalanceOf(country, def.Key);
                return new RenderCommodityBalance(country.Tag, def.Key, balance.Output, balance.Import, balance.Export,
                    balance.Demand, balance.PopulationUse, balance.MilitaryUse, balance.Stockpile, balance.Price, balance.Satisfied);
            })).ToImmutableArray();
        var decisions = game.World.Player is { } decisionPlayer
            ? game.Decisions.Defs.Values.Where(d => game.Decisions.IsVisible(d, decisionPlayer)).Select(d =>
                new RenderDecision(d.Key, d.Name, d.Desc, d.Category, game.Decisions.CanTake(d, decisionPlayer),
                    game.Decisions.Requirements(d, decisionPlayer).Select(r => new RenderRequirement(r.Text, r.Ok)).ToImmutableArray(),
                    game.Decisions.DescribeEffect(d, decisionPlayer).ToImmutableArray())).ToImmutableArray()
            : ImmutableArray<RenderDecision>.Empty;
        string? currentFocusKey = null;
        double currentFocusProgress = 0;
        var focuses = game.World.Player is { } focusPlayer
            ? game.Focus.FocusesOf(focusPlayer).Select(f =>
            {
                var current = game.Focus.Current(focusPlayer);
                bool done = game.Focus.IsDone(focusPlayer, f.Key);
                double days = game.Rules.Focus.Days(game, focusPlayer, f);
                if (current == f) { currentFocusKey = f.Key; currentFocusProgress = game.Focus.Progress(focusPlayer); }
                string? blockReason = !done && current != f ? game.Focus.CannotStart(focusPlayer, f) : null;
                return new RenderFocus(f.Key, f.Name, f.Desc, f.Tree.Key, f.X, f.Y, (int)Math.Round(days),
                    f.Prerequisite.ToImmutableArray(), f.Requires.ToImmutableArray(), f.MutuallyExclusive.ToImmutableArray(), done,
                    current == f, !done && blockReason == null, blockReason,
                    game.Focus.Requirements(focusPlayer, f).Select(r => new RenderRequirement(r.Text, r.Ok)).ToImmutableArray(),
                    game.Focus.DescribeReward(focusPlayer, f).ToImmutableArray());
            }).ToImmutableArray()
            : ImmutableArray<RenderFocus>.Empty;
        double technologyPoints = 0;
        int technologySlotCount = 0, knownTechnologyCount = 0;
        var researchSlots = ImmutableArray<RenderResearchSlot>.Empty;
        var technologies = ImmutableArray<RenderTechnology>.Empty;
        if (game.World.Player is { } technologyPlayer && game.Tech.Enabled)
        {
            technologyPoints = game.Rules.Tech.MonthlyPoints(game, technologyPlayer);
            technologySlotCount = game.Rules.Tech.Slots(game, technologyPlayer);
            knownTechnologyCount = game.Tech.Count(technologyPlayer);
            var liveSlots = game.Tech.SlotsOf(technologyPlayer).ToList();
            var busyTechnologies = liveSlots.Select(s => s.Tech.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            researchSlots = liveSlots.Select(s => new RenderResearchSlot(s.Tech.Key, s.Tech.Name, s.Progress, game.Rules.Tech.Cost(game, technologyPlayer, s.Tech))).ToImmutableArray();
            technologies = game.Tech.Defs.Values.Select(t => new RenderTechnology(t.Key, t.Name, t.Desc, t.Category, t.Year, t.Order,
                game.Tech.Has(technologyPlayer, t.Key), busyTechnologies.Contains(t.Key), game.Tech.CannotResearch(technologyPlayer, t),
                t.Requires.ToImmutableArray(), t.Unlocks.ToImmutableArray())).ToImmutableArray();
        }
        var productionLines = ImmutableArray<RenderProductionLine>.Empty;
        var constructionQueue = ImmutableArray<RenderConstructionItem>.Empty;
        var stockpile = ImmutableArray<RenderStockpileItem>.Empty;
        if (game.World.Player is { } productionPlayer && game.Features.Has(FeatureSet.Production))
        {
            productionLines = productionPlayer.ProductionLines.Select(line => new RenderProductionLine(line.Equipment.Key, line.Equipment.Name,
                game.Production.UsesDockyards(line.Equipment), line.Factories, line.Efficiency, line.LastOutput, line.Equipment.Cost,
                game.Production.Stock(productionPlayer, line.Equipment.Key))).ToImmutableArray();
            constructionQueue = productionPlayer.Construction.Select(item => new RenderConstructionItem(item.Building.Key, item.Building.Name,
                item.State.Key, item.State.DisplayName, item.Progress, item.Building.Cost, item.Factories)).ToImmutableArray();
            var shortage = game.Production.Shortage(productionPlayer);
            var produced = productionPlayer.ProductionLines.GroupBy(line => line.Equipment.Key)
                .ToDictionary(g => g.Key, g => g.Sum(line => line.LastOutput), StringComparer.OrdinalIgnoreCase);
            stockpile = game.Data.Equipment.Values.Select(e => new RenderStockpileItem(e.Key, e.Name, game.Production.Stock(productionPlayer, e.Key),
                produced.GetValueOrDefault(e.Key), shortage.GetValueOrDefault(e.Key))).ToImmutableArray();
        }
        var armyOrders = game.World.Armies.Select(army => new RenderArmyOrder(
            army.Id, army.Name, army.Owner.Tag, army.Mode.ToString(), army.FrontEnemy != null,
            army.FrontPairs.Select(pair =>
            {
                var border = pair.Own.AdjacencyTo(pair.Enemy)?.BorderPoints.Select(p => new RenderPoint(p.X, p.Y)).ToImmutableArray() ?? ImmutableArray<RenderPoint>.Empty;
                return new RenderFrontPair(pair.Own.CenterX, pair.Own.CenterY, pair.Enemy.CenterX, pair.Enemy.CenterY, border);
            }).ToImmutableArray(),
            army.Fallback.Select(l => new RenderPoint(l.CenterX, l.CenterY)).ToImmutableArray(),
            army.FrontLimit.Select(l => new RenderPoint(l.CenterX, l.CenterY)).ToImmutableArray(),
            army.Objective is { } objective, army.Objective?.CenterX ?? 0, army.Objective?.CenterY ?? 0)).ToImmutableArray();
        var notifications = game.Notifications.TakeLast(12).Select(note => new RenderNotification(
            note.Date, note.Text, note.Kind, note.Location?.Key, note.Location?.CenterX ?? 0, note.Location?.CenterY ?? 0)).ToImmutableArray();
        var playerForWars = game.World.Player;
        var wars = game.World.Wars.Select(war =>
        {
            RenderWarCountry Country(Country c, bool leader) => new(c.Tag, c.Name, c.Color, leader, game.Diplomacy.WarExhaustion(c));
            var attackers = war.Attackers.Select(c => Country(c, c == war.AttackerLeader)).ToImmutableArray();
            var defenders = war.Defenders.Select(c => Country(c, c == war.DefenderLeader)).ToImmutableArray();
            var goals = war.Goals.Select(g => new RenderWarGoal(g.Holder.Tag, g.Holder.Name, g.Target.Tag, g.Target.Name,
                g.CasusBelli?.Name ?? "", game.Diplomacy.GoalStates(g).Count, g.CasusBelli?.GoalCostMultiplier ?? 1, g.CasusBelli?.TickingWarScore ?? false)).ToImmutableArray();
            return new RenderWar(war.Id, war.Name, war.AttackerLeader.Tag, war.DefenderLeader.Tag, war.AttackerLeader.Name, war.DefenderLeader.Name,
                attackers, defenders, goals, war.StartDate, war.WarScore, war.BattleScore, war.TickingScore, war.LastChangeDay,
                game.Diplomacy.MaxWarExhaustion, playerForWars != null && war.Involves(playerForWars), war.Vars.HasFlag("civil_war"));
        }).ToImmutableArray();
        var diplomacyRelations = game.World.Player is { } diplomacyFocus
            ? countries.Select(to =>
            {
                var toCountry = game.World.GetCountry(to.Tag)!;
                int kind = diplomacyFocus.Tag == to.Tag ? 0 : game.World.AtWar(diplomacyFocus, toCountry) ? 1
                    : game.World.Relations.AreAllied(diplomacyFocus, toCountry) ? 2
                    : game.World.Relations.AreOverlordAndSubject(toCountry, diplomacyFocus) ? 3
                    : game.World.Relations.HasTruce(diplomacyFocus, toCountry, game.World.Date) ? 4 : 5;
                var opinionModifiers = game.World.Relations.Opinions.TryGetValue((toCountry, diplomacyFocus), out var modifiers)
                    ? modifiers.Select(m => new RenderOpinionModifier(m.Name, m.Value)).ToImmutableArray()
                    : ImmutableArray<RenderOpinionModifier>.Empty;
                return new RenderDiplomacyRelation(diplomacyFocus.Tag, to.Tag, kind,
                    game.Diplomacy.Opinion(toCountry, diplomacyFocus), game.Diplomacy.Opinion(diplomacyFocus, toCountry),
                    game.World.AtWar(diplomacyFocus, toCountry), game.World.Relations.AreAllied(diplomacyFocus, toCountry),
                    game.World.Relations.OverlordOf(toCountry) == diplomacyFocus, game.World.Relations.OverlordOf(diplomacyFocus) == toCountry,
                    game.World.Relations.HasAccess(diplomacyFocus, toCountry), game.World.Relations.HasAccess(toCountry, diplomacyFocus),
                    game.World.Relations.IsGuaranteeing(diplomacyFocus, toCountry), game.World.Relations.IsGuaranteeing(toCountry, diplomacyFocus),
                    game.World.Relations.HasTruce(diplomacyFocus, toCountry, game.World.Date), game.World.Relations.HasPact(diplomacyFocus, toCountry, game.World.Date),
                    game.Diplomacy.LibertyDesire(toCountry), game.Diplomacy.WarExhaustion(toCountry),
                    game.Rules.Diplomacy.BaseOpinion(game, toCountry, diplomacyFocus),
                    game.Politics.IdeologyOpinion(toCountry, diplomacyFocus),
                    game.Population.ReligionOpinion(toCountry, diplomacyFocus),
                    game.Population.ColonialOpinion(toCountry, diplomacyFocus), opinionModifiers);
            }).ToImmutableArray()
            : ImmutableArray<RenderDiplomacyRelation>.Empty;
        var particleEvents = new List<RenderParticleEvent>();
        while (game.Particles.TryTake(out var request))
        {
            switch (request.At)
            {
                case Location location:
                    particleEvents.Add(new RenderParticleEvent(request.Effect, true, location.CenterX, location.CenterY));
                    break;
                case Country { Capital: { } capital }:
                    particleEvents.Add(new RenderParticleEvent(request.Effect, true, capital.CenterX, capital.CenterY));
                    break;
                default:
                    particleEvents.Add(new RenderParticleEvent(request.Effect, false, 0, 0));
                    break;
            }
        }
        var newlyChartedLocations = game.Exploration.DrainRevealed().ToImmutableArray();
        return new RenderSnapshot(tick, game.World.Date, hourFraction, game.VisibilityVersion, game.Exploration.Version, game.Exploration.Enabled, newlyChartedLocations,
            game.Geo.Enabled, game.Geo.Version,
            game.Clock.Speed, game.Clock.Paused, game.Clock.ActualDaysPerSecond, game.World.Map.ContentKey,
            game.World.Player?.Tag, playerSnapshot, countries, characters, locations, states, divisions, armySummaries, sites, fleets, airWings, blockedTradeRoutes, recentTrades, resources, commodityBalances, decisions, focuses, currentFocusKey, currentFocusProgress, technologies, researchSlots, technologyPoints, technologySlotCount, knownTechnologyCount, productionLines, constructionQueue, stockpile, armyOrders, notifications, wars, diplomacyRelations, particleEvents.ToImmutableArray(), politicsOverviews, statecraftBanners);
    }

    private static ImmutableArray<RenderStatecraftBanner> BuildStatecraftBanners(Game game, Country player)
    {
        var lines = new List<RenderStatecraftBanner>();
        if (game.Raising.Enabled && game.Raising.IsMobilised(player))
            lines.Add(new RenderStatecraftBanner($"Mobilised: {game.Raising.MobilisedDaysLeft(player)} days left",
                "The realm is under arms. -5 stability was paid; afterwards the people rest 90 days.", 3, 1));
        if (game.Raising.Enabled && game.Raising.HasLevies(player))
            lines.Add(new RenderStatecraftBanner("Levies stand: tax -10%", "Disbanded 30 days after the peace.", 3, 1));
        if (game.Reputation.Enabled && game.Reputation.CoalitionAgainst(player) is { } coalition)
            lines.Add(new RenderStatecraftBanner($"Coalition against you: {coalition.Members.Count} countries",
                string.Join(", ", coalition.Members.Select(c => c.Name)) + "\nIt dissolves when your aggressive expansion falls below 10.", 1, 2));
        if (game.Factions.Enabled && game.Factions.FactionOf(player).Count > 1
            && game.Factions.Goals.TryGetValue(game.Factions.LeaderOf(player), out var goal)
            && game.Factions.DaysLeft(goal) > 0)
            lines.Add(new RenderStatecraftBanner($"Faction goal: war on {goal.Target.Name} ({game.Factions.DaysLeft(goal)} days)",
                "Members that do not join lose 25 opinion with the leader.", 1,
                game.Factions.Supports(player, goal) ? 0 : 1));
        if (game.GreatPowers.Enabled)
            foreach (var crisis in game.GreatPowers.Crises.Where(c => c.Powers.Contains(player) || c.Target == player))
                lines.Add(new RenderStatecraftBanner($"Crisis over {crisis.Target.Name}", "Two great powers have more than 50 influence there.", 0, 2));
        if (game.Conferences.Enabled)
            foreach (var conference in game.Conferences.Active.Where(c => c.Leader == player || c.Loser == player || c.Turns.Any(t => t.Winner == player)))
                lines.Add(new RenderStatecraftBanner($"Peace conference: {game.Conferences.DaysToNext(conference)} days to the next turn", "Click to see the conference.", -1, 1));
        return lines.ToImmutableArray();
    }
}
