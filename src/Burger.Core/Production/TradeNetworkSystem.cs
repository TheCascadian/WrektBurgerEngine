using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Production;

public enum TradeRouteKind { Land, Sea, Caravan }

public sealed class TradePolicy
{
    public double TariffRate = 0.05;
    public double ImportPriority = 1;
    public double Protectionism;
    public double CustomsRevenue;
    public int AgreementsSigned;
}

public sealed class TradeAgreement
{
    public Country From = null!;
    public Country To = null!;
    public double TariffDiscount = 0.5;
    public double TransportDiscount = 0.1;
    /// <summary>Empty means a general agreement; otherwise this is the guaranteed commodity lane.</summary>
    public string Commodity = "";
    public double MonthlyQuantity;
    public int ExpiresDay;

    public bool Active(GameDate date) => date.TotalDays <= ExpiresDay;
}

public sealed class TradeRoute
{
    public State From = null!;
    public State To = null!;
    public TradeRouteKind Kind;
    public double DistanceKm;
    public double Capacity;
    public List<State> Path = new();
    private string? _key;
    public string Key => _key ??= $"{From.Index}>{To.Index}:{Kind}";
}

public sealed class TradeTransaction
{
    public GameDate Date;
    public string Commodity = "";
    public Country Seller = null!;
    public Country Buyer = null!;
    public State From = null!;
    public State To = null!;
    public TradeRouteKind RouteKind;
    public double Quantity;
    public double DistanceKm;
    public double Price;
    public double TransportCost;
    public double Tariff;
    public bool Blockaded;
}

public sealed class IndustryRecipe
{
    public string Key = "";
    public string Output = "";
    public string Building = "";
    public string Method = "";
    public int StartYear = int.MinValue;
    public int EndYear = int.MaxValue;
    public string RequiresTech = "";
    public double Amount = 1;
    public double WorkforcePerThousand = 0.25;
    public double CapacityPerBuilding = 12;
    public double OutputPerWorkforce = 0.1;
    public Dictionary<string, double> Inputs = new(StringComparer.OrdinalIgnoreCase);

    public bool Active(GameDate date) => date.Year >= StartYear && date.Year <= EndYear;
}

public sealed class WorkforceAllocation
{
    public State State = null!;
    public string Recipe = "";
    public double Workers;
    public double Capacity;
    public double Efficiency;
}

public sealed class AiEconomicPlan
{
    public Country Country = null!;
    public double Budget;
    public string LastDecision = "hold";
    public string Priority = "balanced";
    public int ImportsOrdered;
    public int BuildsOrdered;
    public string TargetCommodity = "";
    public double ImportScore;
    public double BuildScore;
    public int LastReviewDay;
}

/// <summary>
/// State markets and the route layer beneath CommoditySystem. It owns policy, agreements, transaction history,
/// recipe/workforce data and deterministic AI import-versus-build decisions.
/// </summary>
public sealed class TradeNetworkSystem
{
    private readonly Game _game;
    private readonly Dictionary<long, TradeRoute> _routes = new();
    private int _routesMapVersion = -1;
    private bool _routeMissed;
    private long _landTopology;
    private readonly List<Country> _pathOwners = new();
    // Agreement, priority and trade permission of a seller/buyer country pair, in flat arrays indexed buyer * count + seller and valid when the stamp is the current one.
    private TradeAgreement?[] _pairAgreement = Array.Empty<TradeAgreement?>();
    private double[] _pairPriority = Array.Empty<double>();
    private sbyte[] _pairCanTrade = Array.Empty<sbyte>();
    private int[] _pairStamp = Array.Empty<int>();
    private int _pairStampNow;
    private int[] _pairNumber = Array.Empty<int>();
    private int _pairNumbered, _pairSide;
    // scratch of the matching, by country number
    private int[] _scrCountryStart = Array.Empty<int>(), _scrFill = Array.Empty<int>(), _scrHead = Array.Empty<int>(), _scrAlive = Array.Empty<int>(), _scrOrderStart = Array.Empty<int>();
    private readonly List<int> _scrSelling = new();
    private OrderRow[] _scrOrder = Array.Empty<OrderRow>();
    private readonly record struct OrderRow(int Seller, int Agreed, double Priority);
    private readonly struct OrderRowComparer : IComparer<OrderRow>
    {
        public int Compare(OrderRow x, OrderRow y)
        {
            int cmp = y.Agreed.CompareTo(x.Agreed);
            if (cmp == 0) cmp = y.Priority.CompareTo(x.Priority);
            return cmp != 0 ? cmp : x.Seller.CompareTo(y.Seller);
        }
    }
    private static int[] RentZero(ref int[] array, int size)
    {
        if (array.Length < size) array = new int[size + size / 4];
        else Array.Clear(array, 0, size);
        return array;
    }
    private readonly List<(int Seller, int Agreed, double Priority)> _top = new();
    // Recipes by output and by input, built on first use (the recipes only change when the data is loaded).
    private Dictionary<string, List<IndustryRecipe>>? _recipesByOutput, _recipesByInput;
    private List<string>? _recipeOutputs;
    // What a country holds of a commodity, added up (see Totals): the markets of the commodity in the order of Markets, kept while no market is added or
    // dropped (_marketsShape), and the sums of every owner, kept until the markets may have changed (_totalsEpoch).
    private sealed class CommodityTotals
    {
        public long Shape = -1;
        public State[] States = Array.Empty<State>();
        public StateMarket[] Rows = Array.Empty<StateMarket>();
        public int Count;
        public long Epoch = -1;
        public int Stamp;
        public int[] OwnerStamp = Array.Empty<int>();
        public (double Output, double Demand, double Import, double Export, double Stockpile, double Price, double Satisfied)[] OwnerTotals = Array.Empty<(double, double, double, double, double, double, double)>();
    }
    private readonly Dictionary<string, CommodityTotals> _totalsByCommodity = new(StringComparer.OrdinalIgnoreCase);
    private int _totalsStamp;
    private long _marketsShape;
    private long _totalsEpoch;
    private double[] _sumOutput = Array.Empty<double>(), _sumDemand = Array.Empty<double>(), _sumImport = Array.Empty<double>(), _sumExport = Array.Empty<double>(),
        _sumStockpile = Array.Empty<double>(), _sumPrice = Array.Empty<double>(), _sumSatisfied = Array.Empty<double>();
    private int[] _sumRows = Array.Empty<int>();
    private readonly List<Country> _sumOwners = new();
    private readonly Dictionary<State, (float X, float Y)> _landCentres = new();
    // the land graph between states, by state number: neighbours in the order of first contact, and the length of each edge
    private int[][] _landEdgeTo = Array.Empty<int[]>();
    private double[][] _landEdgeKm = Array.Empty<double[]>();
    private int[] _landQueue = Array.Empty<int>();
    private readonly Dictionary<State, ushort[]> _landPaths = new();
    /// <summary>The arrays of dropped land trees, filled again by the next month's origins instead of new ones (a tree is a whole world wide).</summary>
    private readonly Stack<ushort[]> _landTreePool = new();

    private void DropLandTrees()
    {
        if (_landPaths.Count == 0) return;
        int n = _landEdgeTo.Length;
        if (_landTreePool.Count > 0 && _landTreePool.Peek().Length != n) _landTreePool.Clear();
        foreach (var tree in _landPaths.Values)
            if (tree.Length == n) _landTreePool.Push(tree);
        _landPaths.Clear();
    }
    private readonly Dictionary<long, (TradeRouteKind Kind, List<State> Path, int[] Stops, double Distance)> _routeWays = new();
    private int _landEdgesVersion = -1, _landTopologyDay = int.MinValue;
    private readonly Dictionary<string, double> _routeUse = new(StringComparer.Ordinal);
    private readonly Dictionary<TradeAgreement, double> _agreementUse = new();
    private readonly HashSet<string> _blockades = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StateMarket> _current = new();
    private readonly Dictionary<State, int> _currentSlot = new();
    // MatchCommodity is deliberately serial because its commit phase mutates shared world state.
    // Reuse stable per-call storage so bounded commodity settlement does not create a second
    // allocation-heavy pause while preserving the existing candidate and commit order.
    private readonly List<StateMarket> _sellerMarketsScratch = new();
    private readonly List<double> _sellerLeftScratch = new();
    private readonly List<(StateMarket Market, double Need)> _buyersScratch = new();
    private readonly List<int> _tieCursorScratch = new();

    public readonly Dictionary<State, Dictionary<string, StateMarket>> Markets = new();
    public readonly Dictionary<Country, TradePolicy> Policies = new();
    public readonly List<TradeAgreement> Agreements = new();
    public readonly List<TradeTransaction> History = new();
    public readonly Dictionary<string, IndustryRecipe> Recipes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<(State State, string Recipe), WorkforceAllocation> Workforce = new();
    /// <summary>Explicit production-method choices. Missing entries use the newest available method automatically.</summary>
    public readonly Dictionary<(State State, string Output), string> SelectedMethods = new();
    public readonly Dictionary<Country, AiEconomicPlan> Plans = new();

    public TradeNetworkSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "trade_network", Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Commodities) && _game.Commodities.Defs.Count > 0;
    public IReadOnlyList<TradeRoute> ActiveRoutes => _routes.Values.ToList();
    public IReadOnlyList<TradeRoute> BlockadedRoutes => _routes.Values
        .Where(r => r.Kind == TradeRouteKind.Sea && IsRouteBlocked(r)).ToList();
    public IReadOnlyCollection<string> Blockades => _blockades.ToList();
    public int LastMonthTransactions => History.Count(t => t.Date.Year == _game.World.Date.Year && t.Date.Month == _game.World.Date.Month);
    public double LastMonthVolume => History.Where(t => t.Date.Year == _game.World.Date.Year && t.Date.Month == _game.World.Date.Month).Sum(t => t.Quantity);

    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void InvalidateTotalsCache() => _totalsEpoch++;
    /// <summary>
    /// The markets of one commodity have changed. A single player game drops only its totals (and all of them when land changes hands): while a settlement is under way
    /// some commodity changes every hour, and the construction of every country added up all the markets of its inputs again each time. Multiplayer and the tools drop
    /// all totals as before, so a game loaded from a save and the game it was saved from agree.
    /// </summary>
    private void InvalidateTotalsCache(string commodity)
    {
        if (!_game.RelaxedTiming) { _totalsEpoch++; return; }
        if (_totalsByCommodity.TryGetValue(commodity, out var t)) t.Epoch = -1;
    }
    private int _totalsOwnership = -1;

    public TradePolicy PolicyOf(Country country)
        => Policies.TryGetValue(country, out var p) ? p : Policies[country] = new TradePolicy();

    public void LoadDefinitions(GameData data)
    {
        Recipes.Clear();
        _recipesByOutput = _recipesByInput = null; _recipeOutputs = null;
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/industry"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var recipe = new IndustryRecipe
                {
                    Key = n.Key,
                    Output = n.GetString("output") ?? "",
                    Building = n.GetString("building") ?? "",
                    Method = n.GetString("method") ?? "",
                    RequiresTech = n.GetString("requires_tech") ?? "",
                    StartYear = n.GetInt("start_year", int.MinValue),
                    EndYear = n.GetInt("end_year", int.MaxValue),
                    Amount = n.GetDouble("amount", 1),
                    WorkforcePerThousand = n.GetDouble("workforce_per_thousand", 0.25),
                    CapacityPerBuilding = n.GetDouble("capacity_per_building", 12),
                    OutputPerWorkforce = n.GetDouble("output_per_workforce", 0.1),
                };
                if (n.Get("inputs") is { Children: not null } inputs)
                    foreach (var input in inputs.All) recipe.Inputs[input.Key] = input.AsDouble();
                if (recipe.Output.Length > 0) Recipes[recipe.Key] = recipe;
            }
    }

    public void LoadWorld()
    {
        InvalidateTotalsCache();
        _landEdgesVersion = -1;
        _landEdgeTo = Array.Empty<int[]>();
        DropLandTrees();
        _routeWays.Clear();
        _routes.Clear();
        _routeUse.Clear();
        _agreementUse.Clear();
        _current.Clear();
        foreach (var c in _game.World.Countries) PolicyOf(c);
    }

    private void Reset()
    {
        InvalidateTotalsCache();
        _landEdgesVersion = -1;
        _landEdgeTo = Array.Empty<int[]>();
        DropLandTrees();
        _routeWays.Clear();
        Markets.Clear();
        _marketsShape++;
        Policies.Clear();
        Agreements.Clear();
        History.Clear();
        Workforce.Clear();
        Plans.Clear();
        SelectedMethods.Clear();
        _routes.Clear();
        _routeUse.Clear();
        _current.Clear();
        _agreementUse.Clear();
        _blockades.Clear();
    }

    public StateMarket MarketOf(State state, string commodity)
    {
        if (!Markets.TryGetValue(state, out var byCommodity))
        {
            InvalidateTotalsCache();
            _marketsShape++;
            Markets[state] = byCommodity = _freeMarketMaps.Count > 0 ? _freeMarketMaps.Pop() : new(StringComparer.OrdinalIgnoreCase);
        }
        if (!byCommodity.TryGetValue(commodity, out var market))
        {
            InvalidateTotalsCache(commodity);
            _marketsShape++;
            byCommodity[commodity] = market = NewMarket(state, commodity);
        }
        return market;
    }

    // A settlement touches every state for every commodity: hundreds of thousands of markets a month. They are reused, not left to the garbage collector.
    private readonly Stack<StateMarket> _freeMarkets = new();
    private readonly Stack<Dictionary<string, StateMarket>> _freeMarketMaps = new();

    private StateMarket NewMarket(State state, string commodity)
    {
        if (_freeMarkets.Count == 0) return new StateMarket { State = state, Commodity = commodity };
        var m = _freeMarkets.Pop();
        m.State = state; m.Commodity = commodity;
        m.Output = m.Demand = m.OpeningStockpile = m.Stockpile = m.Import = m.Export = m.StorageMonths = 0;
        m.Price = 1; m.Satisfied = 1; m.SellerSlot = -1;
        return m;
    }

    private void RecycleMarkets()
    {
        foreach (var map in Markets.Values)
        {
            foreach (var m in map.Values) _freeMarkets.Push(m);
            map.Clear();
            _freeMarketMaps.Push(map);
        }
        Markets.Clear();
        _marketsShape++;
    }

    public void BeginMonth()
    {
        InvalidateTotalsCache();
        _monthStampNow++;
        // Capacity depends on buildings and service satisfaction. Rebuild economic
        // routes each month so a warm cache and a freshly loaded game agree.
        // The topology/path caches remain reusable until the map changes.
        _routes.Clear();
        DropLandTrees();
        RecycleMarkets();
        Workforce.Clear();
        _routeUse.Clear();
        _agreementUse.Clear();
        _current.Clear();
        Agreements.RemoveAll(a => !a.Active(_game.World.Date));
    }

    public void BeginCommodity(string commodity)
    {
        _current.Clear();
        _currentSlot.Clear();
        // (the markets stand in the order of the states: a state finds its place by its number, and the table of places is only filled if one ever does not)
        foreach (var state in _game.World.States) _current.Add(MarketOf(state, commodity));
        _currentSlotsOpen = true;
    }

    private bool _currentSlotsOpen;   // _currentSlot has not been filled from _current yet

    /// <summary>The place of the state's market in <see cref="_current"/>, or -1.</summary>
    private int CurrentSlot(State state)
    {
        int i = state.Index;
        if ((uint)i < (uint)_current.Count && ReferenceEquals(_current[i].State, state)) return i;
        if (_currentSlotsOpen)
        {
            _currentSlotsOpen = false;
            for (int k = 0; k < _current.Count; k++) _currentSlot[_current[k].State] = k;
        }
        return _currentSlot.TryGetValue(state, out int slot) ? slot : -1;
    }

    private void SetCurrent(State state, StateMarket market)
    {
        int slot = CurrentSlot(state);
        if (slot >= 0) { _current[slot] = market; return; }
        _currentSlot[state] = _current.Count;
        _current.Add(market);
    }

    public void AddMarket(State state, string commodity, double output, double demand, double openingStockpile, double storageMonths)
    {
        var market = MarketOf(state, commodity);
        market.Output = Math.Max(0, output);
        market.Demand = Math.Max(0, demand + (state.Owner == null ? 0 : IndustryInputDemand(state.Owner, state, commodity)));
        market.OpeningStockpile = Math.Max(0, openingStockpile);
        market.StorageMonths = Math.Max(0, storageMonths);
        market.Import = market.Export = 0;
        SetCurrent(state, market);
        InvalidateTotalsCache();
    }

    // the tables hold a row and a column for each country that owns land now (the data knows ten times as many countries, most of them not on the map): numbers handed out as they appear
    private int PairNumber(Country c)
    {
        int countryCount = _game.World.Countries.Count;
        if (_pairNumber.Length < countryCount)
        {
            var grown = new int[countryCount];
            Array.Fill(grown, -1);
            Array.Copy(_pairNumber, grown, _pairNumber.Length);
            _pairNumber = grown;
        }
        int n = _pairNumber[c.Index];
        if (n < 0) { n = _pairNumber[c.Index] = _pairNumbered; _pairNumbered++; }
        return n;
    }

    private void EnsurePairTables()
    {
        if (_pairSide >= _pairNumbered) return;
        int side = Math.Max(_pairNumbered + 64, _pairSide * 3 / 2), size = side * side;
        _pairAgreement = new TradeAgreement?[size]; _pairPriority = new double[size]; _pairCanTrade = new sbyte[size]; _pairStamp = new int[size];
        _pairStampNow = 0; _pairSide = side;
        _preStamp = Array.Empty<int>();
        _prePriority = Array.Empty<double>();
        _monthStamp = Array.Empty<int>();
        _monthPriority = Array.Empty<double>();
    }

    // How much a buyer wants each seller, worked out over the hours before the settlement (see CommoditySystem.SpreadWork): the dearest part of a settlement, and it only reads the game.
    private int[] _preStamp = Array.Empty<int>();
    private double[] _prePriority = Array.Empty<double>();
    private int _preStampNow = 1;
    // ... and kept for the whole settlement: whom a seller prefers is the same question for each of the forty commodities of a month, and asking it again for each
    // was still a twentieth of all simulation time. (The spread settlement only; a war that starts in between still stops the trade, that is asked when the goods are matched.)
    private int[] _monthStamp = Array.Empty<int>();
    private double[] _monthPriority = Array.Empty<double>();
    private int _monthStampNow = 1;

    /// <summary>True if the buyer's priority for every one of the sellers is known from an earlier commodity of this settlement: they are taken, and nothing has to be worked out.</summary>
    internal bool PreparePrioritiesFromMonth(Country buyer, List<Country> sellers)
    {
        int row = _pairNumber[buyer.Index] * _pairSide;
        for (int i = 0; i < sellers.Count; i++)
            if (_monthStamp[row + _pairNumber[sellers[i].Index]] != _monthStampNow) return false;
        for (int i = 0; i < sellers.Count; i++)
        {
            int at = row + _pairNumber[sellers[i].Index];
            _prePriority[at] = _monthPriority[at];
            _preStamp[at] = _preStampNow;
        }
        return true;
    }

    internal void BeginPreparedPriorities(List<Country> buyers, List<Country> sellers)
    {
        _preStampNow++;
        foreach (var c in sellers) PairNumber(c);
        foreach (var c in buyers) PairNumber(c);
        EnsurePairTables();
        int size = _pairSide * _pairSide;
        if (_preStamp.Length < size) { _preStamp = new int[size]; _prePriority = new double[size]; }
        if (_monthStamp.Length < size) { _monthStamp = new int[size]; _monthPriority = new double[size]; }
    }

    internal void PreparePriorities(Country buyer, List<Country> sellers, Func<Country, Country, double> priority)
    {
        int row = _pairNumber[buyer.Index] * _pairSide;
        foreach (var seller in sellers)
        {
            int at = row + _pairNumber[seller.Index];
            _prePriority[at] = priority(seller, buyer);
            _preStamp[at] = _preStampNow;
            _monthPriority[at] = _prePriority[at];
            _monthStamp[at] = _monthStampNow;
        }
    }

    /// <summary>As above, with the buyer's priority for each seller already worked out (in the sellers' order).</summary>
    internal void PreparePriorities(Country buyer, List<Country> sellers, double[] priorities)
    {
        int row = _pairNumber[buyer.Index] * _pairSide;
        for (int i = 0; i < sellers.Count; i++)
        {
            int at = row + _pairNumber[sellers[i].Index];
            _prePriority[at] = priorities[i];
            _preStamp[at] = _preStampNow;
            _monthPriority[at] = priorities[i];
            _monthStamp[at] = _monthStampNow;
        }
    }

    /// <summary>A market whose demand was worked out in advance (see CommoditySystem.SpreadWork).</summary>
    internal void AddPlannedMarket(State state, string commodity, double output, double demand, double openingStockpile, double storageMonths)
    {
        // (the commodity's market of the state is usually at the state's place already: no need to look it up again)
        int at = state.Index;
        var market = (uint)at < (uint)_current.Count && ReferenceEquals(_current[at].State, state) && string.Equals(_current[at].Commodity, commodity, StringComparison.OrdinalIgnoreCase)
            ? _current[at] : MarketOf(state, commodity);
        market.Output = Math.Max(0, output);
        market.Demand = demand;
        market.OpeningStockpile = Math.Max(0, openingStockpile);
        market.StorageMonths = Math.Max(0, storageMonths);
        market.Import = market.Export = 0;
        SetCurrent(state, market);
    }

    public IReadOnlyList<TradeMatch> MatchCommodity(CommodityDef definition, Func<Country, Country, bool> canTrade, Func<Country, Country, double> priority)
    {
        _capStamp++;
        _capHold = true;
        try
        {
            var matches = new List<TradeMatch>();
            foreach (int _ in MatchSteps(definition, canTrade, priority, matches)) { }
            return matches;
        }
        finally { _capHold = false; }
    }

    /// <summary>The matching of a commodity as steps that can be spread over several ticks (see CommoditySystem.SpreadWork).</summary>
    internal IEnumerator<int> BeginMatch(CommodityDef definition, Func<Country, Country, bool> canTrade, Func<Country, Country, double> priority)
        => MatchSteps(definition, canTrade, priority, new List<TradeMatch>()).GetEnumerator();

    /// <summary>Takes steps of a matching until the time is up (a stopwatch timestamp). True when it is done.</summary>
    internal bool AdvanceMatch(IEnumerator<int> steps, long until)
    {
        _capStamp++;
        _capHold = true;
        try
        {
            while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
                if (!steps.MoveNext()) return true;
            return false;
        }
        finally { _capHold = false; }
    }

    /// <summary>As above, by a count of steps instead of the clock (see <see cref="CommoditySystem.SpreadFixedSteps"/>).</summary>
    internal bool AdvanceMatch(IEnumerator<int> steps, ref int stepsLeft)
    {
        _capStamp++;
        _capHold = true;
        try
        {
            while (stepsLeft-- > 0)
                if (!steps.MoveNext()) return true;
            return false;
        }
        finally { _capHold = false; }
    }

    /// <summary>The markets were filled without <see cref="AddMarket"/>: what was added up from them is out of date.</summary>
    internal void MarketsFilled(string commodity) => InvalidateTotalsCache(commodity);

    private bool PairNumbered(Country c) => c.Index < _pairNumber.Length && _pairNumber[c.Index] >= 0;

    private IEnumerable<int> MatchSteps(CommodityDef definition, Func<Country, Country, bool> canTrade, Func<Country, Country, double> priority, List<TradeMatch> matches)
    {
        // Matching mutates stockpiles, imports, exports, prices and satisfaction.
        // Clear before mutation so an exception cannot leave a stale aggregate usable.
        InvalidateTotalsCache(definition.Key);
        var sellerMarkets = _sellerMarketsScratch;
        var sellerLeft = _sellerLeftScratch;
        var buyers = _buyersScratch;
        sellerMarkets.Clear();
        sellerLeft.Clear();
        buyers.Clear();
        foreach (var market in _current)
        {
            market.SellerSlot = -1;
            if (market.State.Owner?.Exists != true) continue;
            double total = market.Output + market.OpeningStockpile;
            double reserve = Math.Min(Math.Max(0, total - market.Demand), market.Demand * market.StorageMonths);
            if (total > market.Demand) { market.SellerSlot = sellerMarkets.Count; sellerMarkets.Add(market); sellerLeft.Add(total - market.Demand - reserve); }
            else if (market.Demand > total) buyers.Add((market, market.Demand - total));
            market.Stockpile = reserve;
        }

        long m0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var left = sellerLeft.ToArray();
        var sellerOwner = new int[sellerMarkets.Count];
        for (int i = 0; i < sellerOwner.Length; i++) sellerOwner[i] = sellerMarkets[i].State.Owner!.Index;
        // Agreement, priority and trade permission depend only on the country pair; many states share a pair.
        int countryCount = _game.World.Countries.Count;
        foreach (var market in sellerMarkets) PairNumber(market.State.Owner!);
        foreach (var (market, _) in buyers) PairNumber(market.State.Owner!);
        EnsurePairTables();
        int pairSide = _pairSide;
        int stamp = ++_pairStampNow;
        // The agreements of each seller-to-buyer pair, so a pair does not scan all of them.
        var agreementsOf = new Dictionary<int, List<TradeAgreement>>();
        var matchDate = _game.World.Date;
        foreach (var a in Agreements)
        {
            if (!a.Active(matchDate)) continue;
            int pairKey = a.From.Index * countryCount + a.To.Index;
            if (!agreementsOf.TryGetValue(pairKey, out var pairList)) agreementsOf[pairKey] = pairList = new();
            pairList.Add(a);
        }
        TradeAgreement? AgreementOf(int pairKey, string commodity)
        {
            if (!agreementsOf.TryGetValue(pairKey, out var list)) return null;
            TradeAgreement? generic = null;
            foreach (var a in list)
            {
                if (a.Commodity.Length == 0) { generic ??= a; continue; }
                if (a.Commodity.Equals(commodity, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return generic;
        }
        int candidateLimit = Math.Max(8, (int)_game.Data.Defines.Get("trade_seller_candidates", 24));
        double tradePriceScale = _game.Data.Defines.Get("commodity_trade_price", 0.5);
        // The sellers by country (their slots ascending), and for each buying country the seller countries best first. A buyer then walks the
        // best countries until it has its candidates instead of looking at every seller; the order is the one a stable sort of all sellers gives.
        var countryStart = RentZero(ref _scrCountryStart, countryCount + 1);
        foreach (int o in sellerOwner) countryStart[o + 1]++;
        for (int i = 0; i < countryCount; i++) countryStart[i + 1] += countryStart[i];
        var flat = new int[sellerOwner.Length];
        var fill = RentZero(ref _scrFill, countryCount + 1);
        Array.Copy(countryStart, fill, countryCount + 1);
        for (int i = 0; i < sellerOwner.Length; i++) flat[fill[sellerOwner[i]]++] = i;
        var head = RentZero(ref _scrHead, countryCount + 1);            // the first slot of the country that may still have something to sell
        Array.Copy(countryStart, head, countryCount + 1);
        var alive = RentZero(ref _scrAlive, countryCount);                 // sellers of the country with something left
        for (int i = 0; i < left.Length; i++) if (left[i] > 0.001) alive[sellerOwner[i]]++;
        // the countries that sell, in the order of their numbers (a buyer looks at these, not at every country of the data)
        var sellingCountries = _scrSelling; sellingCountries.Clear();
        for (int sc = 0; sc < countryCount; sc++) if (countryStart[sc + 1] > countryStart[sc]) sellingCountries.Add(sc);
        var orderOf = new int[countryCount][];
        var orderAgreed = new int[countryCount][];
        var orderPriority = new double[countryCount][];
        var orderStart = RentZero(ref _scrOrderStart, countryCount);

        void BuildOrder(Country buyerCountry, double importPriority)
        {
            long order0 = _game.Profiling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            int pairBase = _pairNumber[buyerCountry.Index] * pairSide;
            var rows = _scrOrder;
            int n = 0;
            if (rows.Length < sellingCountries.Count) rows = _scrOrder = new OrderRow[sellingCountries.Count * 2];
            foreach (int sc in sellingCountries)
            {
                if (alive[sc] <= 0) continue;
                int pairAt = pairBase + _pairNumber[sc];
                if (_pairStamp[pairAt] != stamp)
                {
                    var seller = _game.World.Countries[sc];
                    _pairAgreement[pairAt] = AgreementOf(sc * countryCount + buyerCountry.Index, definition.Key);
                    _pairPriority[pairAt] = pairAt < _preStamp.Length && _preStamp[pairAt] == _preStampNow ? _prePriority[pairAt] : priority(seller, buyerCountry);
                    _pairCanTrade[pairAt] = -1;
                    _pairStamp[pairAt] = stamp;
                }
                rows[n++] = new OrderRow(sc, _pairAgreement[pairAt] != null ? 1 : 0, _pairPriority[pairAt] * importPriority);
            }
            // (agreement, priority, number): a total order, so any sort gives the one result
            new Span<OrderRow>(rows, 0, n).Sort(default(OrderRowComparer));
            var ids = new int[n]; var agreedOut = new int[n]; var prioOut = new double[n];
            for (int i = 0; i < n; i++) { ids[i] = rows[i].Seller; agreedOut[i] = rows[i].Agreed; prioOut[i] = rows[i].Priority; }
            orderOf[buyerCountry.Index] = ids;
            orderAgreed[buyerCountry.Index] = agreedOut;
            orderPriority[buyerCountry.Index] = prioOut;
            if (_game.Profiling) _game.ProfileElapsed("Match.Order", System.Diagnostics.Stopwatch.GetTimestamp() - order0);
        }

        void CollectCandidates(int buyerIndex)
        {
            _top.Clear();
            var order = orderOf[buyerIndex]!; var orderA = orderAgreed[buyerIndex]; var orderP = orderPriority[buyerIndex];
            int pos = orderStart[buyerIndex];
            while (pos < order.Length && alive[order[pos]] <= 0) pos++;   // a country that has sold out stays sold out
            orderStart[buyerIndex] = pos;
            for (int p = pos; p < order.Length && _top.Count < candidateLimit;)
            {
                int q = p + 1;   // the countries p..q-1 rank equal: their sellers are interleaved by slot, as in one list
                while (q < order.Length && orderA[q] == orderA[p] && orderP[q].CompareTo(orderP[p]) == 0) q++;
                if (q - p == 1)
                {
                    int c = order[p];
                    if (alive[c] > 0)
                        for (int i = head[c]; i < countryStart[c + 1] && _top.Count < candidateLimit; i++)
                        {
                            int slot = flat[i];
                            if (left[slot] <= 0.001) { if (i == head[c]) head[c]++; continue; }
                            _top.Add((slot, orderA[p], orderP[p]));
                        }
                }
                else
                {
                    _tieCursorScratch.Clear();
                    for (int k = 0; k < q - p; k++) _tieCursorScratch.Add(head[order[p + k]]);
                    while (_top.Count < candidateLimit)
                    {
                        int bestK = -1, bestSlot = int.MaxValue;
                        for (int k = 0; k < _tieCursorScratch.Count; k++)
                        {
                            int c = order[p + k], end = countryStart[c + 1];
                            while (_tieCursorScratch[k] < end && left[flat[_tieCursorScratch[k]]] <= 0.001) _tieCursorScratch[k]++;
                            if (_tieCursorScratch[k] < end && flat[_tieCursorScratch[k]] < bestSlot) { bestSlot = flat[_tieCursorScratch[k]]; bestK = k; }
                        }
                        if (bestK < 0) break;
                        _tieCursorScratch[bestK]++;
                        _top.Add((bestSlot, orderA[p], orderP[p]));
                    }
                }
                p = q;
            }
        }

        // Preparation is complete before mutation: the stable state-index tie-break keeps the
        // commit order identical across save/load and multiplayer peers.
        buyers.Sort(static (x, y) =>
        {
            int cmp = y.Need.CompareTo(x.Need);
            return cmp != 0 ? cmp : x.Market.State.Index.CompareTo(y.Market.State.Index);
        });
        yield return 0;
        foreach (var (buyer, deficit) in buyers)
        {
            double need = deficit;
            // (spread over hours, the land may have changed hands since the matching began)
            if (buyer.State.Owner is not { } buyerCountry || buyerCountry.Index >= orderOf.Length || !PairNumbered(buyerCountry)) continue;
            var buyerPolicy = PolicyOf(buyerCountry);
            double importPriority = Math.Clamp(buyerPolicy.ImportPriority, 0.1, 4);
            // Top candidates by (agreement, priority) with original order breaking ties,
            // the same order a stable descending sort gives, without sorting every seller.
            _top.Clear();
            int pairBase = _pairNumber[buyerCountry.Index] * pairSide;
            if (orderOf[buyerCountry.Index] == null) BuildOrder(buyerCountry, importPriority);
            CollectCandidates(buyerCountry.Index);
            double tariffRateBase = Math.Clamp(buyerPolicy.TariffRate + buyerPolicy.Protectionism * 0.5, 0, 5);
            double importProtectionFactor = 1 - Math.Clamp(buyerPolicy.Protectionism, 0, 0.9);
            for (int candidateIndex = 0; candidateIndex < _top.Count; candidateIndex++)
            {
                var (sellerSlot, _, _) = _top[candidateIndex];
                var sellerMarket = sellerMarkets[sellerSlot];
                if (sellerMarket.State.Owner is not { } seller || seller == buyerCountry || !PairNumbered(seller)) continue;
                int pairAt = pairBase + _pairNumber[seller.Index];
                if (_pairCanTrade[pairAt] < 0) _pairCanTrade[pairAt] = canTrade(seller, buyerCountry) ? (sbyte)1 : (sbyte)0;
                if (_pairCanTrade[pairAt] == 0) continue;
                var route = RouteBetween(sellerMarket.State, buyer.State);
                if (route == null || IsBlocked(route, seller, buyerCountry)) continue;
                double capacity = Math.Max(0, route.Capacity - _routeUse.GetValueOrDefault(route.Key));
                var agreement = _pairAgreement[pairAt];
                double unitBase = definition.Price * tradePriceScale;
                double unitTransport = TransportCost(route, agreement);
                double tariffRate = tariffRateBase * (agreement?.TariffDiscount ?? 1);
                double unitTariff = unitBase * tariffRate;
                double affordability = !_game.Features.Has(FeatureSet.Economy) ? double.MaxValue
                    : Math.Max(0, buyerCountry.Gold) / Math.Max(0.0001, unitBase + unitTransport + unitTariff);
                double contractLeft = agreement is { Commodity.Length: > 0, MonthlyQuantity: > 0 }
                    ? Math.Max(0, agreement.MonthlyQuantity - _agreementUse.GetValueOrDefault(agreement)) : double.MaxValue;
                if (agreement is { Commodity.Length: > 0 } && contractLeft <= 0.001) continue;
                double protectionFactor = seller == buyerCountry ? 1 : importProtectionFactor;
                double take = Math.Min(need, Math.Min(left[sellerSlot], Math.Min(capacity, Math.Min(affordability, Math.Min(contractLeft, need * protectionFactor + 0.001)))));
                if (take <= 0.001) continue;

                double basePrice = take * unitBase;
                double transport = take * unitTransport;
                double tariff = basePrice * tariffRate;
                var match = new TradeMatch
                {
                    Seller = seller, Buyer = buyerCountry, From = sellerMarket.State, To = buyer.State,
                    Route = route, Quantity = take, BasePrice = basePrice, TransportCost = transport, Tariff = tariff,
                };
                matches.Add(match);
                _routeUse[route.Key] = _routeUse.GetValueOrDefault(route.Key) + take;
                if (agreement is { Commodity.Length: > 0, MonthlyQuantity: > 0 }) _agreementUse[agreement] = _agreementUse.GetValueOrDefault(agreement) + take;
                bool wasAlive = left[sellerSlot] > 0.001;
                left[sellerSlot] -= take;
                if (wasAlive && left[sellerSlot] <= 0.001) alive[sellerOwner[sellerSlot]]--;
                sellerMarket.Export += take;
                buyer.Import += take;
                need -= take;
                if (need <= 0.001) break;
            }
            yield return 0;
        }

        long m1 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var market in _current)
        {
            if (market.State.Owner?.Exists != true) continue;
            if (market.SellerSlot >= 0)
                market.Stockpile = Math.Min(market.Demand * market.StorageMonths, market.Stockpile + Math.Max(0, left[market.SellerSlot]));
            double supplied = market.Output + market.OpeningStockpile + market.Import;
            market.Satisfied = market.Demand <= 0.0001 ? 1 : Math.Clamp(supplied / market.Demand, 0, 1);
            market.Price = definition.Price * Math.Clamp(market.Demand / Math.Max(0.1, supplied), 0.5, 3);
        }
        foreach (var match in matches)
        {
            PolicyOf(match.Buyer).CustomsRevenue += match.Tariff;
            if (_game.Features.Has(FeatureSet.Economy))
            {
                match.Buyer.Gold -= match.BasePrice + match.TransportCost + match.Tariff;
                match.Seller.Gold += match.BasePrice;
                // Tariff is a transfer from the buyer to its customs authority, not a second market expense.
                match.Buyer.Gold += match.Tariff;
            }
            History.Add(new TradeTransaction
            {
                Date = _game.World.Date, Commodity = definition.Key, Seller = match.Seller, Buyer = match.Buyer,
                From = match.From, To = match.To, RouteKind = match.Route.Kind, Quantity = match.Quantity,
                DistanceKm = match.Route.DistanceKm, Price = match.BasePrice, TransportCost = match.TransportCost,
                Tariff = match.Tariff,
            });
        }
        if (History.Count > 6000) History.RemoveRange(0, History.Count - 6000);
        InvalidateTotalsCache(definition.Key);
        if (_game.Profiling)
        {
            _game.ProfileElapsed("Match.Pairing", m1 - m0);
            _game.ProfileElapsed("Match.Settle", System.Diagnostics.Stopwatch.GetTimestamp() - m1);
        }
    }

    public (double Output, double Demand, double Import, double Export, double Stockpile, double Price, double Satisfied) Totals(Country country, string commodity)
    {
        if (!_totalsByCommodity.TryGetValue(commodity, out var t)) _totalsByCommodity[commodity] = t = new CommodityTotals();
        if (_totalsOwnership != _game.World.OwnershipVersion && _game.RelaxedTiming) { _totalsOwnership = _game.World.OwnershipVersion; _totalsEpoch++; }
        if (t.Epoch != _totalsEpoch) SumTotals(t, commodity);
        int i = country.Index;
        if ((uint)i < (uint)t.OwnerStamp.Length && t.OwnerStamp[i] == t.Stamp) return t.OwnerTotals[i];
        return (0, 0, 0, 0, 0, 1, 1);   // no market of it in the country
    }

    // One pass over the markets of the commodity gives the totals of every country that owns one, each added up in the order of Markets.
    private void SumTotals(CommodityTotals t, string commodity)
    {
        if (t.Shape != _marketsShape)
        {
            int n = 0;
            foreach (var (state, markets) in Markets)
                if (markets.TryGetValue(commodity, out var market))
                {
                    if (n == t.Rows.Length)
                    {
                        int size = Math.Max(64, n * 2);
                        Array.Resize(ref t.Rows, size); Array.Resize(ref t.States, size);
                    }
                    t.States[n] = state; t.Rows[n] = market; n++;
                }
            Array.Clear(t.States, n, t.Count > n ? t.Count - n : 0);
            Array.Clear(t.Rows, n, t.Count > n ? t.Count - n : 0);
            t.Count = n;
            t.Shape = _marketsShape;
        }
        int countries = _game.World.Countries.Count;
        if (_sumRows.Length < countries)
        {
            int size = Math.Max(countries, _sumRows.Length * 2);
            _sumOutput = new double[size]; _sumDemand = new double[size]; _sumImport = new double[size]; _sumExport = new double[size];
            _sumStockpile = new double[size]; _sumPrice = new double[size]; _sumSatisfied = new double[size]; _sumRows = new int[size];
        }
        _sumOwners.Clear();
        for (int r = 0; r < t.Count; r++)
        {
            if (t.States[r].Owner is not { } owner) continue;
            int o = owner.Index;
            if (o >= _sumRows.Length)
            {
                int size = Math.Max(o + 1, _sumRows.Length * 2);
                Array.Resize(ref _sumOutput, size); Array.Resize(ref _sumDemand, size); Array.Resize(ref _sumImport, size); Array.Resize(ref _sumExport, size);
                Array.Resize(ref _sumStockpile, size); Array.Resize(ref _sumPrice, size); Array.Resize(ref _sumSatisfied, size); Array.Resize(ref _sumRows, size);
            }
            if (_sumRows[o] == 0)
            {
                _sumOwners.Add(owner);
                _sumOutput[o] = _sumDemand[o] = _sumImport[o] = _sumExport[o] = _sumStockpile[o] = _sumPrice[o] = _sumSatisfied[o] = 0;
            }
            var x = t.Rows[r];
            _sumRows[o]++;
            _sumDemand[o] += x.Demand; _sumOutput[o] += x.Output; _sumImport[o] += x.Import; _sumExport[o] += x.Export; _sumStockpile[o] += x.Stockpile; _sumPrice[o] += x.Price; _sumSatisfied[o] += x.Demand * x.Satisfied;
        }
        int stamp = ++_totalsStamp;
        if (stamp == int.MaxValue) { _totalsStamp = stamp = 1; foreach (var other in _totalsByCommodity.Values) { Array.Clear(other.OwnerStamp); other.Epoch = -1; } }
        t.Stamp = stamp;
        foreach (var owner in _sumOwners)
        {
            int o = owner.Index;
            if (o >= t.OwnerStamp.Length)
            {
                int size = Math.Max(o + 1, Math.Max(countries, t.OwnerStamp.Length * 2));
                Array.Resize(ref t.OwnerStamp, size); Array.Resize(ref t.OwnerTotals, size);
            }
            double demand = _sumDemand[o];
            int rows = _sumRows[o];
            t.OwnerTotals[o] = (_sumOutput[o], demand, _sumImport[o], _sumExport[o], _sumStockpile[o], _sumPrice[o] / rows, demand <= 0.0001 ? 1 : _sumSatisfied[o] / demand);
            t.OwnerStamp[o] = stamp;
            _sumRows[o] = 0;
        }
        _sumOwners.Clear();
        t.Epoch = _totalsEpoch;
    }

    public double CountrySatisfaction(Country country, string commodity)
    {
        var total = Totals(country, commodity);
        return total.Demand <= 0.0001 ? 1 : total.Satisfied;
    }

    public double ProductionOutput(Country country, State state, CommodityDef commodity, double baseOutput)
    {
        double result = baseOutput;
        var recipe = ActiveRecipe(country, state, commodity.Key);
        if (recipe == null) return result;
        int buildings = state.Buildings.GetValueOrDefault(recipe.Building);
        double population = _game.Population.Enabled && _game.Population.HasPops(state) ? _game.Population.Of(state).Total : Math.Max(1000, state.Manpower * 1000);
        double workers = population / 1000.0 * recipe.WorkforcePerThousand * Math.Max(1, buildings);
        double capacity = Math.Max(0.1, buildings * recipe.CapacityPerBuilding);
        double inputFactor = 1;
        if (recipe.Inputs.Count > 0)
        {
            inputFactor = double.PositiveInfinity;
            foreach (var k in recipe.Inputs.Keys) inputFactor = Math.Min(inputFactor, _game.Commodities.BalanceOf(country, k).Satisfied);
        }
        double efficiency = Math.Clamp(Math.Min(1, workers / capacity) * Math.Max(0.15, inputFactor), 0.05, 1);
        Workforce[(state, recipe.Key)] = new WorkforceAllocation { State = state, Recipe = recipe.Key, Workers = workers, Capacity = capacity, Efficiency = efficiency };
        if (buildings <= 0) return result;
        return baseOutput > 0 ? result * efficiency : result + workers * recipe.OutputPerWorkforce * recipe.Amount * efficiency;
    }

    private void BuildRecipeIndex()
    {
        _recipesByOutput = new(StringComparer.OrdinalIgnoreCase);
        _recipesByInput = new(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Recipes.Values)
        {
            if (!_recipesByOutput.TryGetValue(r.Output, out var outs)) _recipesByOutput[r.Output] = outs = new();
            outs.Add(r);
            foreach (var input in r.Inputs.Keys)
            {
                if (!_recipesByInput.TryGetValue(input, out var ins)) _recipesByInput[input] = ins = new();
                ins.Add(r);
            }
        }
        _recipeOutputs = _recipesByOutput.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly List<IndustryRecipe> NoRecipes = new();
    private List<IndustryRecipe> RecipesFor(string output) { if (_recipesByOutput == null) BuildRecipeIndex(); return _recipesByOutput!.GetValueOrDefault(output) ?? NoRecipes; }
    private List<IndustryRecipe> ConsumersOf(string input) { if (_recipesByInput == null) BuildRecipeIndex(); return _recipesByInput!.GetValueOrDefault(input) ?? NoRecipes; }
    private List<string> RecipeOutputs() { if (_recipeOutputs == null) BuildRecipeIndex(); return _recipeOutputs!; }

    /// <summary>Methods currently available for a state's output, ordered from the newest era to the oldest.</summary>
    public IReadOnlyList<IndustryRecipe> MethodsFor(Country country, State state, string output)
        => RecipesFor(output).Where(r => r.Active(_game.World.Date) && TechnologyAvailable(country, r))
                        .OrderByDescending(r => r.StartYear).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Sets a player-controlled method. An empty recipe key clears the explicit choice.</summary>
    public bool SetMethod(Country country, State state, string output, string recipeKey)
    {
        if (state.Owner != country) return false;
        if (recipeKey.Length == 0) { SelectedMethods.Remove((state, output)); return true; }
        var recipe = MethodsFor(country, state, output).FirstOrDefault(r => r.Key.Equals(recipeKey, StringComparison.OrdinalIgnoreCase));
        if (recipe == null) return false;
        SelectedMethods[(state, output)] = recipe.Key;
        return true;
    }

    /// <summary>Set while one commodity is settled (technology and date cannot change in between): the methods of each country and output are worked out once.</summary>
    internal Dictionary<(Country, string), IReadOnlyList<IndustryRecipe>>? MethodMemo;

    private IndustryRecipe? ActiveRecipe(Country country, State state, string output)
    {
        if (RecipesFor(output).Count == 0) return null;   // (most commodities are made by no recipe)
        IReadOnlyList<IndustryRecipe> methods;
        if (MethodMemo == null) methods = MethodsFor(country, state, output);
        else if (!MethodMemo.TryGetValue((country, output), out methods!)) MethodMemo[(country, output)] = methods = MethodsFor(country, state, output); // the methods do not depend on the state
        if (methods.Count == 0) return null;
        if (SelectedMethods.Count == 0) return methods[0];
        var selected = SelectedMethods.GetValueOrDefault((state, output));
        return selected == null ? methods[0] : MethodByKey(methods, selected) ?? methods[0];
    }

    /// <summary>The first of the methods with the key (a loop: a lambda here made a closure on every call, chosen method or not).</summary>
    private static IndustryRecipe? MethodByKey(IReadOnlyList<IndustryRecipe> methods, string key)
    {
        for (int i = 0; i < methods.Count; i++)
            if (methods[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return methods[i];
        return null;
    }

    /// <summary>Input demand generated by active recipes in this state. Inputs are real market demand, not only an efficiency penalty.</summary>
    public double IndustryInputDemand(Country country, State state, string commodity)
    {
        // Most states have no relevant industry. Avoid selecting and sorting every
        // production method when none of its buildings can consume this input.
        double demand = 0;
        bool consumes = false;
        if (state.Buildings.Count > 0)
            foreach (var consumer in ConsumersOf(commodity))
                if (state.Buildings.GetValueOrDefault(consumer.Building) > 0) { consumes = true; break; }
        if (consumes)
        {
            foreach (var output in RecipeOutputs())
            {
                var recipe = ActiveRecipe(country, state, output);
                if (recipe == null || !recipe.Inputs.ContainsKey(commodity)) continue;
                int buildings = state.Buildings.GetValueOrDefault(recipe.Building);
                if (buildings <= 0) continue;
                double population = _game.Population.Enabled && _game.Population.HasPops(state) ? _game.Population.Of(state).Total : Math.Max(1000, state.Manpower * 1000);
                double workers = population / 1000.0 * recipe.WorkforcePerThousand * Math.Max(1, buildings);
                demand += workers * recipe.OutputPerWorkforce * recipe.Amount * recipe.Inputs[commodity];
            }
        }
        return demand + ConstructionInputDemand(country, state, commodity);
    }

    private Country? _constructionGroupsOf;
    private readonly Dictionary<State, List<ConstructionProject>> _constructionGroups = new();
    private readonly List<List<ConstructionProject>> _constructionGroupPool = new();

    /// <summary>While a country is set here, its construction demand reads its projects grouped by state, in queue order, instead of searching the whole queue for every state.
    /// Only for a caller that changes no project before it sets null again.</summary>
    internal void GroupConstruction(Country? country)
    {
        foreach (var list in _constructionGroups.Values) { list.Clear(); _constructionGroupPool.Add(list); }
        _constructionGroups.Clear();
        _constructionGroupsOf = country;
        if (country == null) return;
        foreach (var p in country.Construction)
        {
            if (p.State == null) continue;
            if (!_constructionGroups.TryGetValue(p.State, out var list))
            {
                if (_constructionGroupPool.Count > 0) { list = _constructionGroupPool[^1]; _constructionGroupPool.RemoveAt(_constructionGroupPool.Count - 1); }
                else list = new();
                _constructionGroups[p.State] = list;
            }
            list.Add(p);
        }
        foreach (var list in _constructionGroups.Values) SortConstruction(list);
    }

    /// <summary>Sorts projects as OrderBy building key, then method (both ordinal ignoring case), then progress, then building cost, keeping queue order on ties, without allocating.</summary>
    private static void SortConstruction(List<ConstructionProject> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            var x = list[i];
            int j = i - 1;
            while (j >= 0 && CompareConstruction(list[j], x) > 0) { list[j + 1] = list[j]; j--; }
            list[j + 1] = x;
        }
    }

    private static int CompareConstruction(ConstructionProject a, ConstructionProject b)
    {
        int c = StringComparer.OrdinalIgnoreCase.Compare(a.Building?.Key ?? "", b.Building?.Key ?? "");
        if (c != 0) return c;
        c = StringComparer.OrdinalIgnoreCase.Compare(a.Method ?? "", b.Method ?? "");
        if (c != 0) return c;
        c = a.Progress.CompareTo(b.Progress);
        if (c != 0) return c;
        return (a.Building?.Cost ?? 0).CompareTo(b.Building?.Cost ?? 0);
    }

    private readonly List<ConstructionProject> _constructionScratch = new();
    private bool _constructionScratchBusy;

    /// <summary>Input demand from queued construction projects that can actually progress in the current state and era.</summary>
    private double ConstructionInputDemand(Country country, State state, string commodity)
    {
        if (!_game.Production.Enabled || !country.Exists || state.Owner != country || country.Construction.Count == 0)
            return 0;
        List<ConstructionProject> projects;
        bool scratch = false;
        if (ReferenceEquals(_constructionGroupsOf, country))
        {
            if (!_constructionGroups.TryGetValue(state, out var group)) return 0;
            projects = group;   // (sorted when grouped)
        }
        else
        {
            // (a country builds in a few of its states: most states have no project)
            bool any = false;
            foreach (var p in country.Construction) if (p.State == state) { any = true; break; }
            if (!any) return 0;
            scratch = !_constructionScratchBusy;
            projects = scratch ? _constructionScratch : new List<ConstructionProject>();
            projects.Clear();
            foreach (var p in country.Construction) if (p.State == state) projects.Add(p);
            SortConstruction(projects);
        }

        double demand = 0;
        var rules = _game.Rules.Production;
        if (scratch) _constructionScratchBusy = true;
        try
        {
            for (int i = 0; i < projects.Count; i++)
            {
                var project = projects[i];
                if (!ConstructionProjectEligible(country, project, rules)) continue;
                var method = rules.ResolveConstructionMethod(_game, project.Building);
                if (!method.Inputs.TryGetValue(commodity, out var amount) || !double.IsFinite(amount) || amount <= 0)
                    continue;
                demand += amount;
            }
        }
        finally { if (scratch) { _constructionScratchBusy = false; projects.Clear(); } }
        return demand;
    }

    private bool ConstructionProjectEligible(Country country, ConstructionProject project, ProductionRules rules)
    {
        if (project.Building == null || project.State == null || project.State.Owner != country)
            return false;
        if (project.Progress >= project.Building.Cost || project.Building.Cost <= 0)
            return false;
        if (!string.IsNullOrWhiteSpace(project.BlockedReason))
            return false;
        if (!project.Building.Active(_game.World.Date) || project.State.ControlledShare(country) < 0.5)
            return false;
        if (!_game.Tech.CanUse(country, project.Building.Definition))
            return false;

        var method = rules.ResolveConstructionMethod(_game, project.Building);
        if (!method.Active(_game.World.Date)) return false;
        if (_game.Tech.Enabled && !_game.Tech.HasAll(country, method.RequiresTech)) return false;
        if (project.Building.Potential != null && !_game.Script.EvalTrigger(project.Building.Potential, _game.Context(project.State))) return false;
        return true;
    }

    private bool TechnologyAvailable(Country country, IndustryRecipe recipe)
    {
        if (recipe.RequiresTech.Length == 0 || !_game.Tech.Enabled) return true;
        return _game.Tech.Known.TryGetValue(country, out var known) && known.Contains(recipe.RequiresTech);
    }

    public TradeRoute? RouteBetween(State from, State to)
    {
        if (!_game.Profiling) return FindRoute(from, to);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _routeMissed = false;
        try { return FindRoute(from, to); }
        finally { _game.ProfileElapsed(_routeMissed ? "Trade.Route.Miss" : "Trade.Route", System.Diagnostics.Stopwatch.GetTimestamp() - start); }
    }

    private TradeRoute? FindRoute(State from, State to)
    {
        if (from == to) return new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Land, DistanceKm = 0, Capacity = double.MaxValue, Path = new() { from } };
        if (_routesMapVersion != _game.MapVersion)
        {
            _routesMapVersion = _game.MapVersion;
            // In a war the map changes all the time, and every route of the world would be worked out again each time. The live clock keeps them for the month, unless the states themselves changed.
            if (_game.Commodities.SpreadWork) EnsureLandEdges();
            else _routes.Clear();
        }
        long key = ((long)from.Index << 32) | (uint)to.Index;
        if (_routes.TryGetValue(key, out var cached)) return cached;
        _routeMissed = true;
        EnsureLandEdges();
        // the way between two states only changes with the map's states; what changes each month is its capacity
        if (_routeWays.TryGetValue(key, out var way))
            return _routes[key] = new TradeRoute { From = from, To = to, Kind = way.Kind, DistanceKm = way.Distance, Capacity = RouteCapacity(way.Kind, way.Stops, way.Distance), Path = way.Path };
        var tree = LandTree(from);
        if (tree[to.Index] != NotReached)
        {
            // the way back through the parents; its length is added up from the origin outwards, as the search went
            // (counted first, so the list is made at its size and filled from the end instead of grown and reversed)
            var states = _game.World.States;
            int length = 1;
            for (int cursor = to.Index; cursor != from.Index; cursor = tree[cursor]) length++;
            var wayStates = new State[length];
            for (int cursor = to.Index, at = length - 1; ; cursor = tree[cursor], at--) { wayStates[at] = states[cursor]; if (cursor == from.Index) break; }
            var path = new List<State>(wayStates);
            double distance = 0;
            for (int k = 0; k + 1 < path.Count; k++)
            {
                int next = path[k + 1].Index;
                var edges = _landEdgeTo[path[k].Index];
                for (int e = 0; e < edges.Length; e++)
                    if (edges[e] == next) { distance += _landEdgeKm[path[k].Index][e]; break; }
            }
            var stops = StopsOf(path);
            _routeWays[key] = (TradeRouteKind.Land, path, stops, distance);
            return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Land, DistanceKm = distance, Capacity = RouteCapacity(TradeRouteKind.Land, stops, distance), Path = path };
        }
        bool coastal = from.Locations.Any(l => l.IsCoastal && l.IsLand) && to.Locations.Any(l => l.IsCoastal && l.IsLand);
        if (coastal)
        {
            double distance = CentreDistance(from, to);
            var seaPath = new List<State> { from, to };
            var seaStops = StopsOf(seaPath);
            _routeWays[key] = (TradeRouteKind.Sea, seaPath, seaStops, distance);
            return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Sea, DistanceKm = distance, Capacity = RouteCapacity(TradeRouteKind.Sea, seaStops, distance), Path = seaPath };
        }
        double caravanDistance = Math.Max(100, CentreDistance(from, to));
        var caravanPath = new List<State> { from, to };
        var caravanStops = StopsOf(caravanPath);
        _routeWays[key] = (TradeRouteKind.Caravan, caravanPath, caravanStops, caravanDistance);
        return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Caravan, DistanceKm = caravanDistance, Capacity = RouteCapacity(TradeRouteKind.Caravan, caravanStops, caravanDistance), Path = caravanPath };
    }

    private void EnsureLandEdges()
    {
        if (_landEdgesVersion == _game.MapVersion) return;
        // (in a war the map changes many times a day, and each time every location of the world was gone through to see whether one had moved to another state:
        // a single player game looks once a day)
        int today = _game.World.Date.TotalDays;
        if (_game.RelaxedTiming && _landTopologyDay == today && _landEdgeTo.Length == _game.World.States.Count) { _landEdgesVersion = _game.MapVersion; return; }
        _landTopologyDay = today;
        // Ownership changes bump the map version far more often than the state graph changes.
        // Keep the edges and search trees unless a location moved between states.
        long topology = 17;
        foreach (var st in _game.World.States)
        {
            topology = topology * 31 + st.Index;
            foreach (var l in st.Locations) topology = topology * 31 + l.Index * 7 + l.Neighbors.Count;
        }
        if (topology == _landTopology && _landEdgeTo.Length == _game.World.States.Count) { _landEdgesVersion = _game.MapVersion; return; }
        _landTopology = topology;
        _routes.Clear();
        _routeWays.Clear();
        _landCentres.Clear();
        DropLandTrees();
        int stateCount = _game.World.States.Count;
        _landEdgeTo = new int[stateCount][];
        _landEdgeKm = new double[stateCount][];
        var seen = new HashSet<State>();
        var to = new List<int>();
        var km = new List<double>();
        foreach (var state in _game.World.States)
        {
            seen.Clear(); to.Clear(); km.Clear();
            // Preserve the first province edge and encounter order: route selection
            // and distance must remain identical to the original breadth-first search.
            foreach (var location in state.Locations)
                foreach (var adjacency in location.Neighbors)
                    if (adjacency.Kind != AdjacencyKind.Sea && adjacency.To.State is { } next
                        && next != state && seen.Add(next))
                    {
                        to.Add(next.Index);
                        km.Add(Math.Max(1, adjacency.DistanceKm));
                    }
            _landEdgeTo[state.Index] = to.ToArray();
            _landEdgeKm[state.Index] = km.ToArray();
        }
        _landEdgesVersion = _game.MapVersion;
    }

    private const ushort NotReached = ushort.MaxValue;

    /// <summary>
    /// The breadth-first tree of the land from one state: for each state the one it was reached from. A whole world in two bytes a state; the trees of a month's origins are dropped
    /// when the next month starts (the ways they gave are kept in <see cref="_routeWays"/>, so a later month rarely needs one).
    /// </summary>
    private ushort[] LandTree(State from)
    {
        if (_landPaths.TryGetValue(from, out var tree)) return tree;
        long bfs0 = _game.Profiling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        int n = _landEdgeTo.Length;
        tree = _landTreePool.Count > 0 && _landTreePool.Peek().Length == n ? _landTreePool.Pop() : new ushort[n];
        Array.Fill(tree, NotReached);
        if (_landQueue.Length < n) _landQueue = new int[n];
        var queue = _landQueue;
        int head = 0, tail = 0;
        tree[from.Index] = (ushort)from.Index;
        queue[tail++] = from.Index;
        var edgeTo = _landEdgeTo;
        while (head < tail)
        {
            int current = queue[head++];
            var edges = edgeTo[current];
            for (int e = 0; e < edges.Length; e++)
            {
                int next = edges[e];
                if (tree[next] != NotReached) continue;
                tree[next] = (ushort)current;
                queue[tail++] = next;
            }
        }
        _landPaths[from] = tree;
        if (_game.Profiling) _game.ProfileElapsed("Trade.BFS", System.Diagnostics.Stopwatch.GetTimestamp() - bfs0);
        return tree;
    }

    private static int[] StopsOf(List<State> path)
    {
        var stops = new int[path.Count];
        for (int i = 0; i < stops.Length; i++) stops[i] = path[i].Index;
        return stops;
    }

    // A war changes the map all the time, and every change makes all routes be worked out again (thousands for a commodity, each along tens of states): what a route's capacity is made of
    // is read once per settlement, per state and per owner, into arrays. Nothing it reads changes during a settlement.
    private int[] _capInfrastructure = Array.Empty<int>(), _capMarkets = Array.Empty<int>(), _capOwner = Array.Empty<int>(), _capStateStamp = Array.Empty<int>();
    private int[] _capOwnerStamp = Array.Empty<int>(), _capOwnerSeen = Array.Empty<int>();
    private double[] _capOwnerSatisfied = Array.Empty<double>();
    private int _capStamp, _capWalk;
    private bool _capHold;

    private double RouteCapacity(TradeRouteKind kind, int[] stops, double distance)
    {
        double baseCapacity = _game.Data.Defines.Get(kind == TradeRouteKind.Sea ? "trade_sea_capacity" : kind == TradeRouteKind.Caravan ? "trade_caravan_capacity" : "trade_land_capacity", kind == TradeRouteKind.Sea ? 1500 : kind == TradeRouteKind.Caravan ? 400 : 900);
        var states = _game.World.States;
        var countries = _game.World.Countries;
        if (_capStateStamp.Length < states.Count)
        {
            int n = states.Count;
            _capInfrastructure = new int[n]; _capMarkets = new int[n]; _capOwner = new int[n]; _capStateStamp = new int[n];
        }
        if (_capOwnerStamp.Length < countries.Count)
        {
            int n = countries.Count + 64;
            _capOwnerStamp = new int[n]; _capOwnerSeen = new int[n]; _capOwnerSatisfied = new double[n];
            _capWalk = 0;
        }
        int stamp = _capHold ? _capStamp : ++_capStamp;
        int walk = ++_capWalk;
        int infrastructure = 0, markets = 0, ownerCount = 0;
        double satisfiedSum = 0;
        foreach (int i in stops)
        {
            if (_capStateStamp[i] != stamp)
            {
                var s = states[i];
                _capStateStamp[i] = stamp;
                _capInfrastructure[i] = s.Buildings.GetValueOrDefault("infrastructure");
                _capMarkets[i] = s.Buildings.GetValueOrDefault("market");
                _capOwner[i] = s.Owner is { Exists: true } stateOwner ? stateOwner.Index : -1;
            }
            infrastructure += _capInfrastructure[i];
            markets += _capMarkets[i];
            int o = _capOwner[i];
            if (o >= 0 && _capOwnerSeen[o] != walk)
            {
                _capOwnerSeen[o] = walk;
                ownerCount++;
                if (_capOwnerStamp[o] != stamp)
                {
                    _capOwnerStamp[o] = stamp;
                    _capOwnerSatisfied[o] = _game.Commodities.BalanceOf(countries[o], "transport_services").Satisfied;
                }
                satisfiedSum += _capOwnerSatisfied[o];
            }
        }
        double transportServices = ownerCount == 0 ? 0.5 : satisfiedSum / ownerCount;
        return baseCapacity * (1 + infrastructure * 0.08 + markets * 0.12) * (0.75 + transportServices * 0.5)
            / (1 + distance / (kind == TradeRouteKind.Sea ? 5000 : 1000));
    }

    // Matches Enumerable.Average over floats: double accumulator, float result.
    private (float X, float Y) LandCentre(State s)
    {
        if (_landCentres.TryGetValue(s, out var c)) return c;
        double sx = 0, sy = 0; long n = 0;
        foreach (var l in s.Locations)
            if (l.IsLand) { sx += l.CenterX; sy += l.CenterY; n++; }
        return _landCentres[s] = n == 0 ? (0f, 0f) : ((float)(sx / n), (float)(sy / n));
    }

    private double CentreDistance(State a, State b)
    {
        var (pa, qa) = LandCentre(a);
        var (pb, qb) = LandCentre(b);
        return Math.Max(100, Math.Sqrt(Math.Pow(pa - pb, 2) + Math.Pow(qa - qb, 2)) * 10);
    }

    public bool IsRouteBlocked(TradeRoute route)
    {
        if (route.From.Owner is not { Exists: true } seller || route.To.Owner is not { Exists: true } buyer) return false;
        return IsBlocked(route, seller, buyer);
    }

    private bool IsBlocked(TradeRoute route, Country seller, Country buyer)
    {
        if (_blockades.Count > 0 && _blockades.Contains(BlockadeKey(seller, buyer))) return true;
        if (route.Kind != TradeRouteKind.Sea) return false;
        if (seller.Vars.Flags.Count > 0 && seller.Vars.HasFlag("blockade_" + buyer.Tag) || buyer.Vars.Flags.Count > 0 && buyer.Vars.HasFlag("blockade_" + seller.Tag)) return true;
        if (!_game.Navy.Enabled) return false;
        // (only a fleet at war with one of the two can block: usually there is none, and looking at each port and sea for it is dear)
        bool anyHostile = false;
        foreach (var fleet in _game.Navy.Fleets)
            if (fleet.Owner != seller && fleet.Owner != buyer && (_game.World.AtWar(fleet.Owner, seller) || _game.World.AtWar(fleet.Owner, buyer))) { anyHostile = true; break; }
        if (!anyHostile) return false;

        // A hostile fleet in the sea immediately outside either trading coast blocks
        // the route. The route layer stays independent of naval movement, but naval
        // patrols now have a deterministic economic consequence.
        foreach (var port in route.From.Locations.Concat(route.To.Locations).Where(l => l.IsLand && l.IsCoastal))
            foreach (var sea in port.Neighbors.Select(a => a.To).Where(l => l.IsWater))
                foreach (var fleet in _game.Navy.FleetsAt(sea))
                    if (fleet.Owner != seller && fleet.Owner != buyer
                        && (_game.World.AtWar(fleet.Owner, seller) || _game.World.AtWar(fleet.Owner, buyer)))
                        return true;
        return false;
    }

    private static string BlockadeKey(Country a, Country b) => a.Tag + ">" + b.Tag;
    public void SetBlockade(Country country, Country target, bool blocked)
    {
        string key = BlockadeKey(country, target);
        if (blocked) _blockades.Add(key); else _blockades.Remove(key);
    }

    private TradeAgreement? Agreement(Country from, Country to, string commodity = "")
    {
        TradeAgreement? generic = null;
        var date = _game.World.Date;
        foreach (var a in Agreements)
        {
            if (a.From != from || a.To != to || !a.Active(date)) continue;
            if (a.Commodity.Length == 0) { generic ??= a; continue; }
            if (a.Commodity.Equals(commodity, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return generic;
    }

    public bool Negotiate(Country from, Country to, double tariffDiscount = 0.5, int months = 12, string commodity = "", double monthlyQuantity = 0)
    {
        if (from == to || _game.World.AtWar(from, to) || Agreement(from, to, commodity) != null) return false;
        if (from.Vars.HasFlag("embargo_" + to.Tag) || to.Vars.HasFlag("embargo_" + from.Tag)) return false;
        Agreements.Add(new TradeAgreement { From = from, To = to, Commodity = commodity, MonthlyQuantity = Math.Max(0, monthlyQuantity), TariffDiscount = Math.Clamp(tariffDiscount, 0, 1), TransportDiscount = 0.1, ExpiresDay = _game.World.Date.AddMonths(months).TotalDays });
        PolicyOf(from).AgreementsSigned++;
        PolicyOf(to).AgreementsSigned++;
        return true;
    }

    private double TransportCost(TradeRoute route, TradeAgreement? agreement)
    {
        double baseCost = _game.Data.Defines.Get("trade_transport_cost", 0.0005) * route.DistanceKm;
        if (route.Kind == TradeRouteKind.Sea) baseCost *= 0.6;
        if (route.Kind == TradeRouteKind.Caravan) baseCost *= 1.4;
        return baseCost * (1 - (agreement?.TransportDiscount ?? 0));
    }

    public void AiMonthly(Country country)
    {
        if (!Enabled || country.IsPlayer || !country.Exists) return;
        var plan = Plans.TryGetValue(country, out var existing) ? existing : Plans[country] = new AiEconomicPlan { Country = country };
        plan.Budget = Math.Max(0, country.Gold - _game.Data.Defines.Get("ai_industry_gold_reserve", 20));
        var shortage = _game.Commodities.Shortages(country).FirstOrDefault();
        if (shortage.Commodity == null) { plan.LastDecision = "hold"; plan.TargetCommodity = ""; return; }
        plan.TargetCommodity = shortage.Commodity;
        plan.LastReviewDay = _game.World.Date.TotalDays;
        var buyerState = country.OwnedStates.OrderByDescending(_game.Population.TotalOf).FirstOrDefault();
        // FindRoute always yields a land, sea or caravan route, so a seller with any state is reachable; building the routes here only filled the cache.
        bool HasRoute(Country sellerCountry) => buyerState != null && sellerCountry.OwnedStates.Any();
        // First willing seller by index, one whose opinion of us is not hostile ahead of one that is: a single pass instead of sorting every country.
        Country? seller = null; bool sellerHostile = false;
        bool Consider(Country c)
        {
            bool hostile = _game.Diplomacy.Opinion(country, c) < -60;
            if (seller != null && (!sellerHostile || hostile)) return false;
            seller = c; sellerHostile = hostile;
            return !hostile;
        }
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists || c == country) continue;
            var b = _game.Commodities.BalanceOf(c, shortage.Commodity);
            if (b.Output > b.Demand && HasRoute(c) && Consider(c)) break;
        }
        // A country may be short of a service that nobody exports yet. It still signs a
        // strategic agreement for the first routed surplus, keeping negotiation distinct
        // from the later import-or-build decision.
        if (seller == null && buyerState != null)
            foreach (var c in _game.World.Countries)
            {
                if (!c.Exists || c == country || !HasRoute(c)) continue;
                bool surplus = false;
                foreach (var def in _game.Commodities.Defs.Values)
                {
                    if (!def.IsActive(_game.World.Date)) continue;
                    var b = _game.Commodities.BalanceOf(c, def.Key);
                    if (b.Output > b.Demand) { surplus = true; break; }
                }
                if (surplus && Consider(c)) break;
            }
        double missing = Math.Max(0.1, _game.Commodities.BalanceOf(country, shortage.Commodity).Demand - _game.Commodities.BalanceOf(country, shortage.Commodity).Output);
        bool sellerHasCommodity = seller != null && _game.Commodities.BalanceOf(seller, shortage.Commodity).Output > _game.Commodities.BalanceOf(seller, shortage.Commodity).Demand;
        double importScore = !sellerHasCommodity ? double.PositiveInfinity : _game.Commodities.BalanceOf(seller!, shortage.Commodity).Output / missing;
        var buildingKey = shortage.Commodity is "grain" or "fish" or "animal_products" ? "farm"
            : shortage.Commodity is "salt" or "textiles" or "pottery" or "glass" ? "workshop"
            : shortage.Commodity is "education" ? "school"
            : shortage.Commodity is "healthcare" ? "clinic"
            : shortage.Commodity is "administration" ? "bureaucracy"
            : shortage.Commodity is "transport_services" ? "caravanserai" : "market";
        var building = _game.Data.BuildingTypes.GetValueOrDefault(buildingKey);
        double buildCost = building == null ? double.PositiveInfinity : _game.Population.GoldCost(building);
        double buildScore = buildCost / missing;
        // Save files and the multiplayer wire format reject non-finite numbers; keep the planning sentinel local.
        plan.ImportScore = double.IsFinite(importScore) ? importScore : 1e12;
        plan.BuildScore = double.IsFinite(buildScore) ? buildScore : 1e12;
        // Keep a general diplomatic channel open even when the partner cannot currently cover this exact shortage;
        // the commodity-specific lane below is only signed when it can produce a real import next month.
        if (!sellerHasCommodity && seller != null) Negotiate(country, seller);
        if (sellerHasCommodity && seller != null && (importScore < buildScore * 2 || !double.IsFinite(buildScore)))
        {
            if (Negotiate(country, seller, 0.5, 12, shortage.Commodity, missing)) plan.ImportsOrdered++;
            plan.LastDecision = "import " + shortage.Commodity;
            plan.Priority = shortage.Satisfied < 0.5 ? "survival" : "growth";
            return;
        }
        if (building != null && plan.Budget >= buildCost)
        {
            foreach (var state in country.OwnedStates.OrderByDescending(_game.Population.TotalOf).ThenByDescending(s => s.Tax))
            {
                bool useProductionConstruction = _game.Production.Enabled;
                string? reason = useProductionConstruction
                    ? _game.Production.CannotBuildReason(country, building, state)
                    : _game.Population.CannotGoldBuild(country, building, state);
                if (reason == null)
                {
                    if (useProductionConstruction)
                        _game.Production.QueueBuilding(country, building, state);
                    else
                        _game.Population.GoldBuild(country, building, state);
                    plan.BuildsOrdered++;
                    plan.LastDecision = "build " + buildingKey;
                    plan.Priority = "self-sufficiency";
                    return;
                }
            }
        }
        plan.LastDecision = "hold for " + shortage.Commodity;
    }

    public IEnumerable<TradeTransaction> RecentHistory(int count = 50) => History.TakeLast(count);

    private void Save(Node root)
    {
        foreach (var (country, policy) in Policies)
        {
            var n = root.AddBlock("policy"); n.Add("tag", country.Tag); n.Add("tariff", F(policy.TariffRate)); n.Add("priority", F(policy.ImportPriority)); n.Add("protection", F(policy.Protectionism)); n.Add("customs", F(policy.CustomsRevenue)); n.Add("agreements", policy.AgreementsSigned.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var (country, plan) in Plans)
        {
            var n = root.AddBlock("plan"); n.Add("tag", country.Tag); n.Add("budget", F(plan.Budget)); n.Add("decision", plan.LastDecision, true); n.Add("priority", plan.Priority, true); n.Add("imports", plan.ImportsOrdered.ToString(CultureInfo.InvariantCulture)); n.Add("builds", plan.BuildsOrdered.ToString(CultureInfo.InvariantCulture)); n.Add("target", plan.TargetCommodity, true); n.Add("import_score", F(plan.ImportScore)); n.Add("build_score", F(plan.BuildScore)); n.Add("review_day", plan.LastReviewDay.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var ((state, output), recipe) in SelectedMethods)
        {
            var n = root.AddBlock("method"); n.Add("state", state.Key, true); n.Add("output", output, true); n.Add("recipe", recipe, true);
        }
        foreach (var key in _blockades)
        {
            var parts = key.Split('>', 2);
            if (parts.Length != 2) continue;
            var n = root.AddBlock("blockade"); n.Add("from", parts[0]); n.Add("to", parts[1]);
        }
        foreach (var agreement in Agreements)
        {
            var n = root.AddBlock("agreement"); n.Add("from", agreement.From.Tag); n.Add("to", agreement.To.Tag); n.Add("commodity", agreement.Commodity, true); n.Add("monthly_quantity", F(agreement.MonthlyQuantity)); n.Add("tariff_discount", F(agreement.TariffDiscount)); n.Add("transport_discount", F(agreement.TransportDiscount)); n.Add("expires", agreement.ExpiresDay.ToString(CultureInfo.InvariantCulture));
        }
        // capacity the commodities settled so far this month already used up: the rest of the settlement must see the same
        foreach (var (key, use) in _routeUse) { var n = root.AddBlock("route_use"); n.Add("key", key, true); n.Add("amount", F(use)); }
        for (int i = 0; i < Agreements.Count; i++)
            if (_agreementUse.TryGetValue(Agreements[i], out var au)) { var n = root.AddBlock("agreement_use"); n.Add("index", i.ToString(CultureInfo.InvariantCulture)); n.Add("amount", F(au)); }
        // Multiplayer join/resync saves have a bounded wire budget; keep a recent audit window,
        // while the live session retains the full bounded history for the ledger.
        foreach (var transaction in History.TakeLast(50))
        {
            var n = root.AddBlock("transaction"); n.Add("date", transaction.Date.ToScript()); n.Add("commodity", transaction.Commodity); n.Add("seller", transaction.Seller.Tag); n.Add("buyer", transaction.Buyer.Tag); n.Add("from", transaction.From.Key); n.Add("to", transaction.To.Key); n.Add("kind", transaction.RouteKind.ToString()); n.Add("quantity", F(transaction.Quantity)); n.Add("distance", F(transaction.DistanceKm)); n.Add("price", F(transaction.Price)); n.Add("transport", F(transaction.TransportCost)); n.Add("tariff", F(transaction.Tariff));
        }
    }

    private void Load(Node root)
    {
        Policies.Clear(); Agreements.Clear(); History.Clear(); Plans.Clear(); SelectedMethods.Clear(); _blockades.Clear();
        foreach (var n in root.GetAll("policy"))
            if (_game.World.GetCountry(n.GetString("tag")) is { } c)
            {
                var p = PolicyOf(c); p.TariffRate = n.GetDouble("tariff", p.TariffRate); p.ImportPriority = n.GetDouble("priority", p.ImportPriority); p.Protectionism = n.GetDouble("protection", p.Protectionism); p.CustomsRevenue = n.GetDouble("customs", p.CustomsRevenue); p.AgreementsSigned = n.GetInt("agreements");
        }
        foreach (var n in root.GetAll("method"))
        {
            var state = _game.World.GetState(n.GetString("state"));
            string output = n.GetString("output", "")!, recipe = n.GetString("recipe", "")!;
            if (state?.Owner != null && output.Length > 0 && recipe.Length > 0 && Recipes.ContainsKey(recipe)) SelectedMethods[(state, output)] = recipe;
        }
        foreach (var n in root.GetAll("plan"))
            if (_game.World.GetCountry(n.GetString("tag")) is { } c)
                Plans[c] = new AiEconomicPlan { Country = c, Budget = n.GetDouble("budget"), LastDecision = n.GetString("decision", "hold")!, Priority = n.GetString("priority", "balanced")!, ImportsOrdered = n.GetInt("imports"), BuildsOrdered = n.GetInt("builds"), TargetCommodity = n.GetString("target", "")!, ImportScore = n.GetDouble("import_score"), BuildScore = n.GetDouble("build_score"), LastReviewDay = n.GetInt("review_day") };
        foreach (var n in root.GetAll("blockade"))
            if (_game.World.GetCountry(n.GetString("from")) is { } from && _game.World.GetCountry(n.GetString("to")) is { } to)
                _blockades.Add(BlockadeKey(from, to));
        foreach (var n in root.GetAll("agreement"))
            if (_game.World.GetCountry(n.GetString("from")) is { } from && _game.World.GetCountry(n.GetString("to")) is { } to)
                Agreements.Add(new TradeAgreement { From = from, To = to, Commodity = n.GetString("commodity", "")!, MonthlyQuantity = n.GetDouble("monthly_quantity"), TariffDiscount = n.GetDouble("tariff_discount", 0.5), TransportDiscount = n.GetDouble("transport_discount", 0.1), ExpiresDay = n.GetInt("expires", _game.World.Date.TotalDays) });
        _routeUse.Clear(); _agreementUse.Clear();
        foreach (var n in root.GetAll("route_use")) _routeUse[n.GetString("key", "")!] = n.GetDouble("amount");
        foreach (var n in root.GetAll("agreement_use"))
            if (n.GetInt("index", -1) is >= 0 and var ix && ix < Agreements.Count) _agreementUse[Agreements[ix]] = n.GetDouble("amount");
        foreach (var n in root.GetAll("transaction"))
            if (_game.World.GetCountry(n.GetString("seller")) is { } seller
                && _game.World.GetCountry(n.GetString("buyer")) is { } buyer
                && _game.World.GetState(n.GetString("from")) is { } from
                && _game.World.GetState(n.GetString("to")) is { } to
                && Enum.TryParse<TradeRouteKind>(n.GetString("kind"), true, out var kind))
                History.Add(new TradeTransaction
                {
                    Date = GameDate.Parse(n.GetString("date") ?? "", _game.World.Date), Commodity = n.GetString("commodity") ?? "",
                    Seller = seller, Buyer = buyer, From = from, To = to, RouteKind = kind,
                    Quantity = n.GetDouble("quantity"), DistanceKm = n.GetDouble("distance"), Price = n.GetDouble("price"),
                    TransportCost = n.GetDouble("transport"), Tariff = n.GetDouble("tariff"),
                });
    }
}

public sealed class StateMarket
{
    public State State = null!;
    public string Commodity = "";
    public double Output, Demand, OpeningStockpile, Stockpile, Import, Export, Price = 1, Satisfied = 1, StorageMonths;
    /// <summary>Scratch for the matching: this market's place in the list of sellers, or -1.</summary>
    internal int SellerSlot = -1;
}

public sealed class TradeMatch
{
    public Country Seller = null!;
    public Country Buyer = null!;
    public State From = null!;
    public State To = null!;
    public TradeRoute Route = null!;
    public double Quantity, BasePrice, TransportCost, Tariff;
}
