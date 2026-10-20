using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Production;

/// <summary>A strategic commodity from <c>common/commodities.txt</c> (steel, oil, rubber, aluminium, chromium, tungsten ...).</summary>
public sealed class CommodityDef
{
    public string Key = "";
    public string Name = "";
    public Rgb Color = new(160, 160, 160);
    /// <summary>Gold one unit costs when it is traded (commodity_trade_price define scales it).</summary>
    public double Price = 1;
    /// <summary>Monthly use of each civilian factory (industry and construction need it too).</summary>
    public double CivilianUse;
    /// <summary>Monthly use per 1,000 people. This is the first population-needs layer.</summary>
    public double PopulationUse;
    /// <summary>Storage class used by the market when it rolls surplus into next month's stockpile.</summary>
    public string Category = "industrial";
    /// <summary>Maximum number of months of demand that can be stored. Zero means no storage.</summary>
    public double StorageMonths;
    /// <summary>First astronomical year in which the commodity is available.</summary>
    public int StartYear = int.MinValue;
    /// <summary>Last astronomical year in which the commodity is available.</summary>
    public int EndYear = int.MaxValue;
    /// <summary>Optional monthly use per 1,000 people of a social class (farmers, nobles, clergy...).</summary>
    public Dictionary<string, double> ClassPopulationUse = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");

    public bool IsActive(GameDate date) => date.Year >= StartYear && date.Year <= EndYear;
}

/// <summary>
/// Overridable commodity rules (commodities feature, backend/Commodities.cs): how much a country digs, what it needs and who sells to whom.
/// </summary>
public class CommodityRules
{
    /// <summary>Monthly output of a state: its deposits (units a year / 12) scaled by what the owner controls and by the modifier extraction_factor.</summary>
    public virtual double Output(Game game, Country c, State s, CommodityDef k, double deposit)
        => deposit / 12.0 * s.ControlledShare(c) * OutputFactor(game, c, k);

    /// <summary>Country-wide extraction multiplier of a commodity (the same for all its states).</summary>
    public double OutputFactor(Game game, Country c, CommodityDef k)
    {
        if (CacheOpen && _factorCountry == c && _factorCommodity == k) return _factor;
        double f = Math.Max(0.1, 1 + game.Modifiers.Sum(c, "extraction") + game.Modifiers.Sum(c, k.Key + "_output"));
        if (CacheOpen) { _factorCountry = c; _factorCommodity = k; _factor = f; }
        return f;
    }

    /// <summary>True while one commodity is settled: modifiers cannot change in between, so the last factor may be reused.</summary>
    internal bool CacheOpen;
    private Country? _factorCountry;
    private CommodityDef? _factorCommodity;
    private double _factor;

    /// <summary>Monthly use of a country besides its military lines: its civilian factories (industry, consumer goods, construction).</summary>
    public virtual double BaseDemand(Game game, Country c, CommodityDef k)
        => game.Production.Enabled ? game.Production.CivilianFactories(c) * k.CivilianUse : 0;

    /// <summary>Monthly population demand for a commodity, in abstract market units.</summary>
    public virtual double PopulationDemand(Game game, Country c, CommodityDef k)
        => game.Population.Enabled ? game.Population.TotalOf(c) / 1000.0 * k.PopulationUse : 0;

    /// <summary>Maximum number of months of demand held after local consumption.</summary>
    public virtual double StorageMonths(Game game, CommodityDef k)
        => k.StorageMonths > 0 ? k.StorageMonths : k.Category.Equals("food", StringComparison.OrdinalIgnoreCase) ? 6 : 12;

    /// <summary>May the seller sell to the buyer? Not at war, not hated, and not a country that has put an embargo on it (flag embargo_TAG).</summary>
    public virtual bool WillTrade(Game game, Country seller, Country buyer)
    {
        if (game.World.AtWar(seller, buyer)) return false;
        if (seller.Vars.HasFlag("embargo_" + buyer.Tag)) return false;
        // allies of the seller's enemies do not get its goods
        foreach (var war in game.World.WarsOf(seller)) if (war.Involves(buyer) && war.EnemiesOf(seller).Contains(buyer)) return false;
        return game.Diplomacy.Opinion(seller, buyer) > game.Data.Defines.Get("trade_min_opinion", -60);
    }

    /// <summary>Who the seller prefers to sell to (higher first): allies and subjects, then friends.</summary>
    public virtual double TradePriority(Game game, Country seller, Country buyer)
    {
        var r = game.World.Relations;
        double p = game.Diplomacy.Opinion(seller, buyer);
        if (r.AreAllied(seller, buyer)) p += 100;
        if (r.AreOverlordAndSubject(buyer, seller)) p += 150;
        return p;
    }

    /// <summary>Share (0-1) of its demand a country gets, as a speed for what it makes with it: a shortage hurts, but never stops everything (commodity_floor, 0.15).</summary>
    public virtual double Speed(Game game, double satisfied) => Math.Clamp(Math.Max(game.Data.Defines.Get("commodity_floor", 0.15), satisfied), 0, 1);
}

/// <summary>Deposits, trade and shortages of strategic commodities (commodities feature).</summary>
public sealed class CommoditySystem
{
    private readonly Game _game;
    public readonly Dictionary<string, CommodityDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>State key -> commodity -> yearly output at full extraction (history/commodities.txt).</summary>
    public readonly Dictionary<State, Dictionary<string, double>> Deposits = new();
    private readonly Dictionary<string, Dictionary<string, double>> _equipUse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per country and commodity: what it digs, imports, needs, and the share of its need it got last month.</summary>
    public sealed class Balance
    {
        public double Output, Import, Export, Demand, PopulationUse, MilitaryUse;
        public double Stockpile, Price = 1, Satisfied = 1;
    }
    public readonly Dictionary<Country, Dictionary<string, Balance>> Books = new();
    private readonly Dictionary<Country, Dictionary<string, double>> _usedThisMonth = new();
    private const int SettlementBatchSize = 1;
    private readonly List<string> _settlementCommodities = new();
    private readonly List<Country> _settlementCountries = new();
    private int _settlementIndex;
    private bool _settlementStarted;
    private bool _settlementComplete;

    public CommoditySystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection
        {
            Name = "commodities",
            Write = Save,
            Read = Load,
            Reset = () =>
            {
                Books.Clear();
                _usedThisMonth.Clear();
                _settlementCommodities.Clear();
                _settlementCountries.Clear();
                _settlementIndex = 0;
                _settlementStarted = false;
                _settlementComplete = false;
            }
        });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Commodities) && Defs.Count > 0;
    private CommodityRules Rules => _game.Rules.Commodities;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Defs.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/commodities"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var k = new CommodityDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Price = n.GetDouble("price", 1),
                    CivilianUse = n.GetDouble("civilian_use"),
                    PopulationUse = n.GetDouble("population_use"),
                    Category = n.GetString("category") ?? "industrial",
                    StorageMonths = n.GetDouble("storage_months"),
                    StartYear = n.GetInt("start_year", int.MinValue),
                    EndYear = n.GetInt("end_year", int.MaxValue),
                    Definition = n,
                };
                if (Rgb.TryParse(n.Get("color"), out var col)) k.Color = col;
                if (n.Get("class_use") is { Children: not null } classUse)
                    foreach (var classEntry in classUse.All) k.ClassPopulationUse[classEntry.Key] = classEntry.AsDouble();
                Defs[k.Key] = k;
            }
    }

    /// <summary>Reads deposits (history/commodities) and what equipment uses; called when the world is loaded.</summary>
    public void LoadWorld()
    {
        Deposits.Clear();
        _equipUse.Clear();
        if (Defs.Count == 0) return;
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/commodities"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var s = _game.World.GetState(n.Key);
                if (s == null) { Log.ErrorOnce("deposit:" + n.Key, $"history/commodities: unknown state '{n.Key}'", n.Location); continue; }
                var d = Deposits.TryGetValue(s, out var existing) ? existing : Deposits[s] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in n.All)
                {
                    if (!Defs.ContainsKey(kv.Key)) { Log.ErrorOnce("deposit-k:" + kv.Key, $"history/commodities: unknown commodity '{kv.Key}'", kv.Location); continue; }
                    d[kv.Key] = d.GetValueOrDefault(kv.Key) + kv.AsDouble(0);
                }
            }
        foreach (var e in _game.Data.Equipment.Values)
            if (e.Definition.Get("commodities") is { Children: not null } cn)
            {
                var use = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cn.All)
                {
                    if (!Defs.ContainsKey(kv.Key)) { Log.ErrorOnce("equip-k:" + kv.Key, $"Equipment '{e.Key}' uses unknown commodity '{kv.Key}'", kv.Location); continue; }
                    use[kv.Key] = kv.AsDouble(0);
                }
                _equipUse[e.Key] = use;
            }
    }

    // ------------------------------------------------------------------------------------- questions
    public Balance BalanceOf(Country c, string commodity)
    {
        if (!Books.TryGetValue(c, out var b)) Books[c] = b = new Dictionary<string, Balance>(StringComparer.OrdinalIgnoreCase);
        if (!b.TryGetValue(commodity, out var x)) b[commodity] = x = new Balance();
        return x;
    }

    public IReadOnlyDictionary<string, double> UseOf(EquipmentType e) => _equipUse.TryGetValue(e.Key, out var u) ? u : EmptyUse;
    private static readonly Dictionary<string, double> EmptyUse = new();

    /// <summary>Speed (0-1) at which this country can make an equipment: its worst shortage among the commodities it needs.</summary>
    public double Factor(Country c, EquipmentType e)
    {
        if (!Enabled || !_equipUse.TryGetValue(e.Key, out var use)) return 1;
        double f = 1;
        foreach (var k in use.Keys)
            if (Defs.TryGetValue(k, out var def) && def.IsActive(_game.World.Date))
                f = Math.Min(f, Math.Min(BalanceOf(c, k).Satisfied, _game.TradeNetwork.CountrySatisfaction(c, k)));
        return Rules.Speed(_game, f);
    }

    /// <summary>Records what a production line used (units made x the equipment's use).</summary>
    public void Use(Country c, EquipmentType e, double units)
    {
        if (!Enabled || units <= 0 || !_equipUse.TryGetValue(e.Key, out var use)) return;
        if (!_usedThisMonth.TryGetValue(c, out var d)) _usedThisMonth[c] = d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, per) in use) d[k] = d.GetValueOrDefault(k) + per * units;
    }

    /// <summary>The total yearly deposits of a commodity in the states of a country (for the AI and the interface).</summary>
    public double DepositsOf(Country c, string commodity)
    {
        double v = 0;
        foreach (var s in c.OwnedStates) if (Deposits.TryGetValue(s, out var d) && d.TryGetValue(commodity, out var x)) v += x;
        return v;
    }

    /// <summary>Commodities the country is short of (satisfied under 85%), worst first.</summary>
    public List<(string Commodity, double Satisfied)> Shortages(Country c)
        => Defs.Values.Where(k => k.IsActive(_game.World.Date)).Select(k => (k.Key, Balance: BalanceOf(c, k.Key)))
                     .Where(x => x.Balance.Demand > 0.0001 && x.Balance.Satisfied < 0.85)
                     .Select(x => (x.Key, x.Balance.Satisfied)).OrderBy(x => x.Satisfied).ToList();

    /// <summary>Food shortage factor used by population growth in the following month.</summary>
    public double PopulationGrowthFactor(Country c)
    {
        if (!Enabled || !Defs.TryGetValue("grain", out var grain) || !grain.IsActive(_game.World.Date)) return 1;
        double satisfied = BalanceOf(c, grain.Key).Satisfied;
        return satisfied >= 0.75 ? 1 : Math.Clamp(0.25 + satisfied, 0.25, 1);
    }

    /// <summary>
    /// How much the state's deposits would help a country that is short of commodities (0 when it is not): the summed share of its
    /// shortages the deposits would cover. The AI adds this to its wish for land and for good relations with its owner.
    /// </summary>
    public double NeedScore(Country c, State s)
    {
        if (!Enabled || !Deposits.TryGetValue(s, out var d)) return 0;
        double score = 0;
        foreach (var (k, shortfall) in Shortages(c))
            if (d.TryGetValue(k, out var yearly) && yearly > 0)
            {
                var b = BalanceOf(c, k);
                double missing = Math.Max(0.1, b.Demand - b.Output - b.Import);
                score += Math.Min(2, yearly / 12.0 / missing) * (1 - shortfall);
            }
        return score;
    }

    /// <summary>Need score of everything a country owns (the AI's wish to take, or make friends with, its owner).</summary>
    public double NeedScore(Country c, Country owner) => owner.OwnedStates.Sum(s => NeedScore(c, s));

    /// <summary>AI investment hook for pre-production economies: queue a farm, workshop, market or road project when the market says it is needed.</summary>
    public void AiMonthly(Country c)
    {
        if (!Enabled || c.IsPlayer || !c.Exists || !_game.Features.Has(FeatureSet.Economy) || c.OwnedStates.Count == 0) return;
        _game.TradeNetwork.AiMonthly(c);
    }

    // ------------------------------------------------------------------------------------- monthly market
    public void MonthlyTick() => MonthlyTick(streamEconomicWork: false);

    /// <summary>
    /// Starts this month's settlement. Headless and direct callers drain it to completion; the live clock
    /// initializes the work and lets subsequent daily ticks process the bounded commodity batches.
    /// </summary>
    public void MonthlyTick(bool streamEconomicWork)
    {
        if (!Enabled) return;
        // A bounded settlement may still be in flight when the calendar reaches the next month.
        // Finish that snapshot before replacing its commodity and country lists, otherwise pending
        // commodities would be discarded and the new month would mix economic periods.
        if (_settlementStarted && !_settlementComplete)
            ProcessSettlementToCompletion();
        BeginSettlement();
        if (!streamEconomicWork)
            ProcessSettlementToCompletion();
    }

    /// <summary>Processes the next deterministic commodity batch after the month has started.</summary>
    public void DailyTick()
    {
        if (!Enabled || !_settlementStarted || _settlementComplete) return;
        ProcessSettlementBatch();
    }

    private void BeginSettlement()
    {
        if (_settlementStarted && !_settlementComplete)
            throw new InvalidOperationException("Cannot begin a new commodity settlement while the previous settlement is incomplete.");
        _game.TradeNetwork.BeginMonth();
        _settlementCommodities.Clear();
        _settlementCommodities.AddRange(Defs.Values
            .Where(k => k.IsActive(_game.World.Date))
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .Select(k => k.Key));
        _settlementCountries.Clear();
        _settlementCountries.AddRange(_game.World.Countries.Where(c => c.Exists));
        _settlementIndex = 0;
        _settlementStarted = true;
        _settlementComplete = false;
    }

    private void ProcessSettlementBatch()
    {
        int processed = 0;
        while (processed < SettlementBatchSize && _settlementIndex < _settlementCommodities.Count)
        {
            string key = _settlementCommodities[_settlementIndex++];
            if (Defs.TryGetValue(key, out var commodity))
                ProcessCommodity(commodity);
            processed++;
        }

        if (_settlementIndex >= _settlementCommodities.Count)
        {
            _settlementIndex = _settlementCommodities.Count;
            _settlementComplete = true;
            _usedThisMonth.Clear();
        }
    }

    private void ProcessSettlementToCompletion()
    {
        while (_settlementIndex < _settlementCommodities.Count)
            ProcessSettlementBatch();
    }

    private readonly List<(State State, double Output)> _stateOutputs = new();
    private readonly List<double> _statePops = new();

    private void ProcessCommodity(CommodityDef k)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _game.TradeNetwork.BeginCommodity(k.Key);
        Rules.CacheOpen = true;
        _game.TradeNetwork.MethodMemo = new();
        Burger.Core.Mechanics.ModifierSystem.BeginMemo();
        foreach (var c in _settlementCountries)
        {
            var b = BalanceOf(c, k.Key);
            double output = 0;
            var stateOutputs = _stateOutputs; stateOutputs.Clear();
            foreach (var s in c.OwnedStates)
            {
                double deposit = Deposits.TryGetValue(s, out var d) ? d.GetValueOrDefault(k.Key) : 0;
                double stateOutput = _game.TradeNetwork.ProductionOutput(c, s, k, Rules.Output(_game, c, s, k, deposit));
                stateOutputs.Add((s, stateOutput));
                output += stateOutput;
            }
            double military = _usedThisMonth.TryGetValue(c, out var u) ? u.GetValueOrDefault(k.Key) : 0;
            b.Output = output;
            b.MilitaryUse = military;
            b.PopulationUse = PopulationDemand(c, k);
            b.Demand = Rules.BaseDemand(_game, c, k) + b.PopulationUse + military;
            b.Import = 0;
            b.Export = 0;
            double opening = Math.Max(0, b.Stockpile);
            double population = 0;
            _statePops.Clear();
            foreach (var s in c.OwnedStates)
            {
                double sp = _game.Population.Enabled && _game.Population.HasPops(s) ? _game.Population.Of(s).Total : Math.Max(1, s.Manpower * 1000);
                _statePops.Add(sp);
                population += sp;
            }
            double demandWithoutMilitary = Math.Max(0, b.Demand);
            double outputWeight = Math.Max(0.0001, output);
            for (int si = 0; si < stateOutputs.Count; si++)
            {
                var (state, stateOutput) = stateOutputs[si];
                double statePopulation = _statePops[si];
                double demandShare = population <= 0 ? 1.0 / Math.Max(1, stateOutputs.Count) : statePopulation / population;
                double outputShare = output <= 0 ? demandShare : stateOutput / outputWeight;
                _game.TradeNetwork.AddMarket(state, k.Key, stateOutput, demandWithoutMilitary * demandShare, opening * demandShare, Rules.StorageMonths(_game, k));
            }
        }
        Rules.CacheOpen = false;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _game.TradeNetwork.MethodMemo = null;
        try { _game.TradeNetwork.MatchCommodity(k, (seller, buyer) => Rules.WillTrade(_game, seller, buyer), (seller, buyer) => Rules.TradePriority(_game, seller, buyer)); }
        finally { Burger.Core.Mechanics.ModifierSystem.EndMemo(); }
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var c in _settlementCountries)
        {
            var b = BalanceOf(c, k.Key);
            var totals = _game.TradeNetwork.Totals(c, k.Key);
            b.Output = totals.Output;
            b.Demand = totals.Demand;
            b.Import = totals.Import;
            b.Export = totals.Export;
            b.Stockpile = totals.Stockpile;
            b.Satisfied = totals.Satisfied;
            b.Price = totals.Price;
        }
        if (_game.Profiling)
        {
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            _game.ProfileElapsed("Settle.Markets", t1 - t0);
            _game.ProfileElapsed("Settle.Match", t2 - t1);
            _game.ProfileElapsed("Settle.Totals", t3 - t2);
            _game.ProfileElapsed("Settle[" + k.Key + "]", t3 - t0); _game.ProfileElapsed("SettleM[" + k.Key + "]", t1 - t0); _game.ProfileElapsed("SettleX[" + k.Key + "]", t2 - t1);
        }
    }

    private double PopulationDemand(Country country, CommodityDef commodity)
    {
        if (!_game.Population.Enabled || commodity.ClassPopulationUse.Count == 0)
            return Rules.PopulationDemand(_game, country, commodity);
        double demand = 0;
        foreach (var state in country.OwnedStates)
            if (_game.Population.HasPops(state))
                foreach (var pop in _game.Population.Of(state).Pops)
                    demand += pop.Size / 1000.0 * commodity.ClassPopulationUse.GetValueOrDefault(pop.Type ?? "", commodity.PopulationUse);
        return demand;
    }

    // ------------------------------------------------------------------------------------- state splits
    /// <summary>Deposits are extensive: the child gets its share of each, the parent keeps the rest.</summary>
    public void OnStateSplit(State parent, State child, double share)
    {
        if (!Deposits.TryGetValue(parent, out var d)) return;
        var c = Deposits[child] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in d.ToList()) { c[k] = v * share; d[k] = v - c[k]; }
    }

    public void OnStateUnsplit(State parent, State child)
    {
        if (Deposits.Remove(child, out var c) && Deposits.TryGetValue(parent, out var d))
            foreach (var (k, v) in c) d[k] = d.GetValueOrDefault(k) + v;
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        root.Add("settlement_started", _settlementStarted ? "1" : "0");
        root.Add("settlement_complete", _settlementComplete ? "1" : "0");
        root.Add("settlement_index", _settlementIndex.ToString(CultureInfo.InvariantCulture));
        foreach (var (c, used) in _usedThisMonth.OrderBy(x => x.Key.Tag, StringComparer.Ordinal))
        {
            var n = root.AddBlock("used");
            n.Add("tag", c.Tag);
            foreach (var (k, v) in used.OrderBy(x => x.Key, StringComparer.Ordinal)) n.Add("use_" + k, F(v));
        }
        foreach (var (c, books) in Books)
        {
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            foreach (var (k, b) in books)
            {
                n.Add("output_" + k, F(b.Output));
                n.Add("import_" + k, F(b.Import));
                n.Add("export_" + k, F(b.Export));
                n.Add("demand_" + k, F(b.Demand));
                n.Add("population_use_" + k, F(b.PopulationUse));
                n.Add("military_use_" + k, F(b.MilitaryUse));
                n.Add("stockpile_" + k, F(b.Stockpile));
                n.Add("price_" + k, F(b.Price));
                n.Add("satisfied_" + k, F(b.Satisfied));
            }
        }
    }

    private void Load(Node root)
    {
        Books.Clear();
        _usedThisMonth.Clear();
        _settlementCommodities.Clear();
        _settlementCountries.Clear();
        _settlementStarted = root.GetInt("settlement_started") != 0;
        _settlementComplete = root.GetInt("settlement_complete") != 0;
        _settlementIndex = Math.Max(0, root.GetInt("settlement_index"));
        if (_settlementStarted || _settlementComplete)
        {
            _settlementCommodities.AddRange(Defs.Values
                .Where(k => k.IsActive(_game.World.Date))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => k.Key));
            _settlementCountries.AddRange(_game.World.Countries.Where(c => c.Exists));
            _settlementIndex = Math.Min(_settlementIndex, _settlementCommodities.Count);
            // Older saves had no explicit completion flag. Their terminal index is equivalent.
            if (_settlementIndex >= _settlementCommodities.Count)
                _settlementComplete = true;
        }
        foreach (var n in root.GetAll("used"))
            if (_game.World.GetCountry(n.GetString("tag")) is { } uc)
            {
                var d = _usedThisMonth[uc] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in n.All)
                    if (kv.Key.StartsWith("use_", StringComparison.OrdinalIgnoreCase)) d[kv.Key[4..]] = kv.AsDouble();
            }
        foreach (var n in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            foreach (var kv in n.All)
            {
                string? key = null;
                Action<Balance, double>? set = null;
                if (kv.Key.StartsWith("output_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[7..]; set = (b, v) => b.Output = v; }
                else if (kv.Key.StartsWith("import_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[7..]; set = (b, v) => b.Import = v; }
                else if (kv.Key.StartsWith("export_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[7..]; set = (b, v) => b.Export = v; }
                else if (kv.Key.StartsWith("demand_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[7..]; set = (b, v) => b.Demand = v; }
                else if (kv.Key.StartsWith("population_use_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[15..]; set = (b, v) => b.PopulationUse = v; }
                else if (kv.Key.StartsWith("military_use_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[13..]; set = (b, v) => b.MilitaryUse = v; }
                else if (kv.Key.StartsWith("stockpile_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[10..]; set = (b, v) => b.Stockpile = v; }
                else if (kv.Key.StartsWith("price_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[6..]; set = (b, v) => b.Price = v; }
                else if (kv.Key.StartsWith("satisfied_", StringComparison.OrdinalIgnoreCase)) { key = kv.Key[10..]; set = (b, v) => b.Satisfied = v; }
                else if (Defs.ContainsKey(kv.Key)) { key = kv.Key; set = (b, v) => b.Satisfied = v; }
                if (set != null && key != null && Defs.ContainsKey(key))
                {
                    set(BalanceOf(c, key), kv.AsDouble());
                }
            }
        }
    }
}
