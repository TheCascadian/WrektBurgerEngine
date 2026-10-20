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
    private readonly List<(int Seller, int Agreed, double Priority)> _top = new();
    // Recipes by output and by input, built on first use (the recipes only change when the data is loaded).
    private Dictionary<string, List<IndustryRecipe>>? _recipesByOutput, _recipesByInput;
    private List<string>? _recipeOutputs;
    private readonly HashSet<string> _totalsBulk = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<State, (float X, float Y)> _landCentres = new();
    private readonly Dictionary<State, List<(State To, double Distance)>> _landEdges = new();
    /// <summary>A breadth-first search from one origin that is carried on only as far as a destination needs (the first-found parents do not depend on where it stops).</summary>
    private sealed class LandSearch
    {
        public readonly Dictionary<State, (State Previous, double Distance)> Previous = new();
        public readonly Queue<State> Queue = new();
    }
    private readonly Dictionary<State, LandSearch> _landPaths = new();
    private int _landEdgesVersion = -1;
    private readonly Dictionary<string, double> _routeUse = new(StringComparer.Ordinal);
    private readonly Dictionary<TradeAgreement, double> _agreementUse = new();
    private readonly HashSet<string> _blockades = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StateMarket> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, Dictionary<string, (double Output, double Demand, double Import, double Export, double Stockpile, double Price, double Satisfied)>> _totalsCache = new();

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

    private void InvalidateTotalsCache() { if (_totalsCache.Count > 0) _totalsCache.Clear(); if (_totalsBulk.Count > 0) _totalsBulk.Clear(); }

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
        _landEdges.Clear();
        _landPaths.Clear();
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
        _landEdges.Clear();
        _landPaths.Clear();
        Markets.Clear();
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
            Markets[state] = byCommodity = new(StringComparer.OrdinalIgnoreCase);
        }
        if (!byCommodity.TryGetValue(commodity, out var market))
        {
            InvalidateTotalsCache();
            byCommodity[commodity] = market = new StateMarket { State = state, Commodity = commodity };
        }
        return market;
    }

    public void BeginMonth()
    {
        InvalidateTotalsCache();
        // Capacity depends on buildings and service satisfaction. Rebuild economic
        // routes each month so a warm cache and a freshly loaded game agree.
        // The topology/path caches remain reusable until the map changes.
        _routes.Clear();
        Markets.Clear();
        Workforce.Clear();
        _routeUse.Clear();
        _agreementUse.Clear();
        _current.Clear();
        Agreements.RemoveAll(a => !a.Active(_game.World.Date));
    }

    public void BeginCommodity(string commodity)
    {
        _current.Clear();
        foreach (var state in _game.World.States)
            _current[state.Key] = MarketOf(state, commodity);
    }

    public void AddMarket(State state, string commodity, double output, double demand, double openingStockpile, double storageMonths)
    {
        var market = MarketOf(state, commodity);
        market.Output = Math.Max(0, output);
        market.Demand = Math.Max(0, demand + (state.Owner == null ? 0 : IndustryInputDemand(state.Owner, state, commodity)));
        market.OpeningStockpile = Math.Max(0, openingStockpile);
        market.StorageMonths = Math.Max(0, storageMonths);
        market.Import = market.Export = 0;
        _current[state.Key] = market;
        InvalidateTotalsCache();
    }

    public IReadOnlyList<TradeMatch> MatchCommodity(CommodityDef definition, Func<Country, Country, bool> canTrade, Func<Country, Country, double> priority)
    {
        // Matching mutates stockpiles, imports, exports, prices and satisfaction.
        // Clear before mutation so an exception cannot leave a stale aggregate usable.
        InvalidateTotalsCache();
        var sellerMarkets = new List<StateMarket>();
        var sellerLeft = new List<double>();
        var buyers = new List<(StateMarket Market, double Need)>();
        foreach (var market in _current.Values)
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
        var matches = new List<TradeMatch>();
        // Agreement, priority and trade permission depend only on the country pair; many states share a pair.
        int countryCount = _game.World.Countries.Count;
        if (_pairStamp.Length < countryCount * countryCount)
        {
            int size = countryCount * countryCount;
            _pairAgreement = new TradeAgreement?[size]; _pairPriority = new double[size]; _pairCanTrade = new sbyte[size]; _pairStamp = new int[size];
            _pairStampNow = 0;
        }
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
        var countryStart = new int[countryCount + 1];
        foreach (int o in sellerOwner) countryStart[o + 1]++;
        for (int i = 0; i < countryCount; i++) countryStart[i + 1] += countryStart[i];
        var flat = new int[sellerOwner.Length];
        var fill = (int[])countryStart.Clone();
        for (int i = 0; i < sellerOwner.Length; i++) flat[fill[sellerOwner[i]]++] = i;
        var head = (int[])countryStart.Clone();            // the first slot of the country that may still have something to sell
        var alive = new int[countryCount];                 // sellers of the country with something left
        for (int i = 0; i < left.Length; i++) if (left[i] > 0.001) alive[sellerOwner[i]]++;
        var orderOf = new int[countryCount][];
        var orderAgreed = new int[countryCount][];
        var orderPriority = new double[countryCount][];
        var orderStart = new int[countryCount];

        void BuildOrder(Country buyerCountry, double importPriority)
        {
            long order0 = _game.Profiling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            int pairBase = buyerCountry.Index * countryCount;
            var list = new List<int>();
            for (int sc = 0; sc < countryCount; sc++)
            {
                if (alive[sc] <= 0) continue;
                int pairAt = pairBase + sc;
                if (_pairStamp[pairAt] != stamp)
                {
                    var seller = _game.World.Countries[sc];
                    _pairAgreement[pairAt] = AgreementOf(sc * countryCount + buyerCountry.Index, definition.Key);
                    _pairPriority[pairAt] = priority(seller, buyerCountry);
                    _pairCanTrade[pairAt] = -1;
                    _pairStamp[pairAt] = stamp;
                }
                list.Add(sc);
            }
            var agreed = new int[countryCount];
            var prio = new double[countryCount];
            foreach (int sc in list) { agreed[sc] = _pairAgreement[pairBase + sc] != null ? 1 : 0; prio[sc] = _pairPriority[pairBase + sc] * importPriority; }
            list.Sort((x, y) =>
            {
                int cmp = agreed[y].CompareTo(agreed[x]);
                if (cmp == 0) cmp = prio[y].CompareTo(prio[x]);
                return cmp != 0 ? cmp : x.CompareTo(y);
            });
            orderOf[buyerCountry.Index] = list.ToArray();
            orderAgreed[buyerCountry.Index] = list.Select(x => agreed[x]).ToArray();
            orderPriority[buyerCountry.Index] = list.Select(x => prio[x]).ToArray();
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
                    var cursor = new int[q - p];
                    for (int k = 0; k < cursor.Length; k++) cursor[k] = head[order[p + k]];
                    while (_top.Count < candidateLimit)
                    {
                        int bestK = -1, bestSlot = int.MaxValue;
                        for (int k = 0; k < cursor.Length; k++)
                        {
                            int c = order[p + k], end = countryStart[c + 1];
                            while (cursor[k] < end && left[flat[cursor[k]]] <= 0.001) cursor[k]++;
                            if (cursor[k] < end && flat[cursor[k]] < bestSlot) { bestSlot = flat[cursor[k]]; bestK = k; }
                        }
                        if (bestK < 0) break;
                        cursor[bestK]++;
                        _top.Add((bestSlot, orderA[p], orderP[p]));
                    }
                }
                p = q;
            }
        }

        foreach (var (buyer, deficit) in buyers.OrderByDescending(x => x.Need))
        {
            double need = deficit;
            var buyerPolicy = PolicyOf(buyer.State.Owner!);
            var buyerCountry = buyer.State.Owner!;
            double importPriority = Math.Clamp(buyerPolicy.ImportPriority, 0.1, 4);
            // Top candidates by (agreement, priority) with original order breaking ties,
            // the same order a stable descending sort gives, without sorting every seller.
            _top.Clear();
            int pairBase = buyerCountry.Index * countryCount;
            if (orderOf[buyerCountry.Index] == null) BuildOrder(buyerCountry, importPriority);
            CollectCandidates(buyerCountry.Index);
            foreach (var (sellerSlot, _, _) in _top.ToList())
            {
                var sellerMarket = sellerMarkets[sellerSlot];
                var seller = sellerMarket.State.Owner!;
                if (seller == buyerCountry) continue;
                int pairAt = pairBase + seller.Index;
                if (_pairCanTrade[pairAt] < 0) _pairCanTrade[pairAt] = canTrade(seller, buyerCountry) ? (sbyte)1 : (sbyte)0;
                if (_pairCanTrade[pairAt] == 0) continue;
                var route = RouteBetween(sellerMarket.State, buyer.State);
                if (route == null || IsBlocked(route, seller, buyerCountry)) continue;
                double capacity = Math.Max(0, route.Capacity - _routeUse.GetValueOrDefault(route.Key));
                var agreement = _pairAgreement[pairAt];
                double unitBase = definition.Price * tradePriceScale;
                double unitTransport = TransportCost(route, agreement);
                double tariffRate = Math.Clamp(PolicyOf(buyerCountry).TariffRate + PolicyOf(buyerCountry).Protectionism * 0.5, 0, 5)
                    * (agreement?.TariffDiscount ?? 1);
                double unitTariff = unitBase * tariffRate;
                double affordability = !_game.Features.Has(FeatureSet.Economy) ? double.MaxValue
                    : Math.Max(0, buyerCountry.Gold) / Math.Max(0.0001, unitBase + unitTransport + unitTariff);
                double contractLeft = agreement is { Commodity.Length: > 0, MonthlyQuantity: > 0 }
                    ? Math.Max(0, agreement.MonthlyQuantity - _agreementUse.GetValueOrDefault(agreement)) : double.MaxValue;
                if (agreement is { Commodity.Length: > 0 } && contractLeft <= 0.001) continue;
                double protectionFactor = seller == buyerCountry ? 1 : 1 - Math.Clamp(PolicyOf(buyerCountry).Protectionism, 0, 0.9);
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
                if (wasAlive && left[sellerSlot] <= 0.001) alive[seller.Index]--;
                sellerMarket.Export += take;
                buyer.Import += take;
                need -= take;
                if (need <= 0.001) break;
            }
        }

        long m1 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var market in _current.Values.Where(m => m.State.Owner?.Exists == true))
        {
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
        InvalidateTotalsCache();
        if (_game.Profiling)
        {
            _game.ProfileElapsed("Match.Pairing", m1 - m0);
            _game.ProfileElapsed("Match.Settle", System.Diagnostics.Stopwatch.GetTimestamp() - m1);
        }
        return matches;
    }

    public (double Output, double Demand, double Import, double Export, double Stockpile, double Price, double Satisfied) Totals(Country country, string commodity)
    {
        if (_totalsCache.TryGetValue(country, out var cachedByCommodity)
            && cachedByCommodity.TryGetValue(commodity, out var cached)) return cached;

        // One pass over the markets gives the totals of every country for this commodity (a pass for each country was countries times states).
        if (_totalsBulk.Add(commodity))
        {
            var rowsByOwner = new Dictionary<Country, List<StateMarket>>();
            foreach (var (state, markets) in Markets)
                if (state.Owner is { } owner && markets.TryGetValue(commodity, out var market))
                {
                    if (!rowsByOwner.TryGetValue(owner, out var list)) rowsByOwner[owner] = list = new List<StateMarket>();
                    list.Add(market);
                }
            foreach (var (owner, rows) in rowsByOwner)
                CacheTotals(owner, commodity, rows);
        }
        if (_totalsCache.TryGetValue(country, out cachedByCommodity) && cachedByCommodity.TryGetValue(commodity, out cached)) return cached;
        return CacheTotals(country, commodity, new List<StateMarket>());
    }

    private (double Output, double Demand, double Import, double Export, double Stockpile, double Price, double Satisfied) CacheTotals(Country country, string commodity, List<StateMarket> rows)
    {
        double demand = rows.Sum(x => x.Demand);
        var total = (rows.Sum(x => x.Output), demand, rows.Sum(x => x.Import), rows.Sum(x => x.Export), rows.Sum(x => x.Stockpile), rows.Count == 0 ? 1 : rows.Average(x => x.Price), demand <= 0.0001 ? 1 : rows.Sum(x => x.Demand * x.Satisfied) / demand);
        if (!_totalsCache.TryGetValue(country, out var byCommodity))
            _totalsCache[country] = byCommodity = new(StringComparer.OrdinalIgnoreCase);
        byCommodity[commodity] = total;
        return total;
    }

    public double CountrySatisfaction(Country country, string commodity)
    {
        var total = Totals(country, commodity);
        return total.Demand <= 0.0001 ? 1 : total.Satisfied;
    }

    public double ProductionOutput(Country country, State state, CommodityDef commodity, double baseOutput)
    {
        double result = baseOutput;
        foreach (var recipe in ActiveRecipes(country, state, commodity.Key))
        {
            int buildings = state.Buildings.GetValueOrDefault(recipe.Building);
            double population = _game.Population.Enabled && _game.Population.HasPops(state) ? _game.Population.Of(state).Total : Math.Max(1000, state.Manpower * 1000);
            double workers = population / 1000.0 * recipe.WorkforcePerThousand * Math.Max(1, buildings);
            double capacity = Math.Max(0.1, buildings * recipe.CapacityPerBuilding);
            double inputFactor = recipe.Inputs.Count == 0 ? 1 : recipe.Inputs.Keys.Select(k => _game.Commodities.BalanceOf(country, k).Satisfied).DefaultIfEmpty(0).Min();
            double efficiency = Math.Clamp(Math.Min(1, workers / capacity) * Math.Max(0.15, inputFactor), 0.05, 1);
            Workforce[(state, recipe.Key)] = new WorkforceAllocation { State = state, Recipe = recipe.Key, Workers = workers, Capacity = capacity, Efficiency = efficiency };
            if (buildings <= 0) continue;
            result = baseOutput > 0 ? result * efficiency : result + workers * recipe.OutputPerWorkforce * recipe.Amount * efficiency;
        }
        return result;
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

    private IEnumerable<IndustryRecipe> ActiveRecipes(Country country, State state, string output)
    {
        IReadOnlyList<IndustryRecipe> methods;
        if (MethodMemo == null) methods = MethodsFor(country, state, output);
        else if (!MethodMemo.TryGetValue((country, output), out methods!)) MethodMemo[(country, output)] = methods = MethodsFor(country, state, output); // the methods do not depend on the state
        if (methods.Count == 0) yield break;
        var selected = SelectedMethods.GetValueOrDefault((state, output));
        yield return (selected == null ? methods[0] : methods.FirstOrDefault(r => r.Key.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? methods[0]);
    }

    /// <summary>Input demand generated by active recipes in this state. Inputs are real market demand, not only an efficiency penalty.</summary>
    public double IndustryInputDemand(Country country, State state, string commodity)
    {
        // Most states have no relevant industry. Avoid selecting and sorting every
        // production method when none of its buildings can consume this input.
        double demand = 0;
        if (state.Buildings.Count > 0 && ConsumersOf(commodity).Any(r => state.Buildings.GetValueOrDefault(r.Building) > 0))
        {
            foreach (var output in RecipeOutputs())
            {
                var recipe = ActiveRecipes(country, state, output).FirstOrDefault(r => r.Inputs.ContainsKey(commodity));
                if (recipe == null) continue;
                int buildings = state.Buildings.GetValueOrDefault(recipe.Building);
                if (buildings <= 0) continue;
                double population = _game.Population.Enabled && _game.Population.HasPops(state) ? _game.Population.Of(state).Total : Math.Max(1000, state.Manpower * 1000);
                double workers = population / 1000.0 * recipe.WorkforcePerThousand * Math.Max(1, buildings);
                demand += workers * recipe.OutputPerWorkforce * recipe.Amount * recipe.Inputs[commodity];
            }
        }
        return demand + ConstructionInputDemand(country, state, commodity);
    }

    /// <summary>Input demand from queued construction projects that can actually progress in the current state and era.</summary>
    private double ConstructionInputDemand(Country country, State state, string commodity)
    {
        if (!_game.Production.Enabled || !country.Exists || state.Owner != country || country.Construction.Count == 0)
            return 0;

        double demand = 0;
        var rules = _game.Rules.Production;
        foreach (var project in country.Construction
                     .Where(p => p.State == state)
                     .OrderBy(p => p.Building?.Key ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Method ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Progress)
                     .ThenBy(p => p.Building?.Cost ?? 0))
        {
            if (!ConstructionProjectEligible(country, project, rules)) continue;
            var method = rules.ResolveConstructionMethod(_game, project.Building);
            if (!method.Inputs.TryGetValue(commodity, out var amount) || !double.IsFinite(amount) || amount <= 0)
                continue;
            demand += amount;
        }
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
        if (_game.Tech.Enabled && method.RequiresTech.Any(tech => !_game.Tech.Has(country, tech))) return false;
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
        if (_routesMapVersion != _game.MapVersion) { _routes.Clear(); _routesMapVersion = _game.MapVersion; }
        long key = ((long)from.Index << 32) | (uint)to.Index;
        if (_routes.TryGetValue(key, out var cached)) return cached;
        _routeMissed = true;
        EnsureLandEdges();
        if (!_landPaths.TryGetValue(from, out var search))
        {
            search = _landPaths[from] = new LandSearch();
            search.Previous[from] = (from, 0);
            search.Queue.Enqueue(from);
        }
        var previous = search.Previous;
        if (!previous.ContainsKey(to) && search.Queue.Count > 0)
        {
            long bfs0 = System.Diagnostics.Stopwatch.GetTimestamp();
            // One ordered search serves all destinations from this origin; it resumes where the last one stopped.
            while (search.Queue.Count > 0 && !previous.ContainsKey(to))
            {
                var current = search.Queue.Dequeue();
                foreach (var (next, distance) in _landEdges[current])
                {
                    if (previous.ContainsKey(next)) continue;
                    previous[next] = (current, previous[current].Distance + distance);
                    search.Queue.Enqueue(next);
                }
            }
            if (_game.Profiling) _game.ProfileElapsed("Trade.BFS", System.Diagnostics.Stopwatch.GetTimestamp() - bfs0);
        }
        if (previous.ContainsKey(to))
        {
            var path = new List<State>();
            for (var cursor = to;; cursor = previous[cursor].Previous) { path.Add(cursor); if (cursor == from) break; }
            path.Reverse();
            double distance = previous[to].Distance;
            return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Land, DistanceKm = distance, Capacity = RouteCapacity(TradeRouteKind.Land, path, distance), Path = path };
        }
        bool coastal = from.Locations.Any(l => l.IsCoastal && l.IsLand) && to.Locations.Any(l => l.IsCoastal && l.IsLand);
        if (coastal)
        {
            double distance = CentreDistance(from, to);
            return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Sea, DistanceKm = distance, Capacity = RouteCapacity(TradeRouteKind.Sea, new() { from, to }, distance), Path = new() { from, to } };
        }
        double caravanDistance = Math.Max(100, CentreDistance(from, to));
        return _routes[key] = new TradeRoute { From = from, To = to, Kind = TradeRouteKind.Caravan, DistanceKm = caravanDistance, Capacity = RouteCapacity(TradeRouteKind.Caravan, new() { from, to }, caravanDistance), Path = new() { from, to } };
    }

    private void EnsureLandEdges()
    {
        if (_landEdgesVersion == _game.MapVersion) return;
        // Ownership changes bump the map version far more often than the state graph changes.
        // Keep the edges and search trees unless a location moved between states.
        long topology = 17;
        foreach (var st in _game.World.States)
        {
            topology = topology * 31 + st.Index;
            foreach (var l in st.Locations) topology = topology * 31 + l.Index * 7 + l.Neighbors.Count;
        }
        if (topology == _landTopology && _landEdges.Count == _game.World.States.Count) { _landEdgesVersion = _game.MapVersion; return; }
        _landTopology = topology;
        _routes.Clear();
        _landCentres.Clear();
        _landEdges.Clear();
        _landPaths.Clear();
        foreach (var state in _game.World.States)
        {
            var edges = new List<(State To, double Distance)>();
            var seen = new HashSet<State>();
            // Preserve the first province edge and encounter order: route selection
            // and distance must remain identical to the original breadth-first search.
            foreach (var location in state.Locations)
                foreach (var adjacency in location.Neighbors)
                    if (adjacency.Kind != AdjacencyKind.Sea && adjacency.To.State is { } next
                        && next != state && seen.Add(next))
                        edges.Add((next, Math.Max(1, adjacency.DistanceKm)));
            _landEdges[state] = edges;
        }
        _landEdgesVersion = _game.MapVersion;
    }

    private double RouteCapacity(TradeRouteKind kind, List<State> path, double distance)
    {
        double baseCapacity = _game.Data.Defines.Get(kind == TradeRouteKind.Sea ? "trade_sea_capacity" : kind == TradeRouteKind.Caravan ? "trade_caravan_capacity" : "trade_land_capacity", kind == TradeRouteKind.Sea ? 1500 : kind == TradeRouteKind.Caravan ? 400 : 900);
        int infrastructure = 0, markets = 0;
        _pathOwners.Clear();
        double satisfiedSum = 0;
        foreach (var s in path)
        {
            infrastructure += s.Buildings.GetValueOrDefault("infrastructure");
            markets += s.Buildings.GetValueOrDefault("market");
            if (s.Owner is { Exists: true } owner && !_pathOwners.Contains(owner))
            {
                _pathOwners.Add(owner);
                satisfiedSum += _game.Commodities.BalanceOf(owner, "transport_services").Satisfied;
            }
        }
        double transportServices = _pathOwners.Count == 0 ? 0.5 : satisfiedSum / _pathOwners.Count;
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
        if (_blockades.Contains(BlockadeKey(seller, buyer))) return true;
        if (route.Kind != TradeRouteKind.Sea) return false;
        if (seller.Vars.HasFlag("blockade_" + buyer.Tag) || buyer.Vars.HasFlag("blockade_" + seller.Tag)) return true;
        if (!_game.Navy.Enabled) return false;

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
