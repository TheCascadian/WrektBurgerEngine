using System.Numerics;
using System.Collections.Immutable;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Multiplayer;
using Burger.Core.Population;
using Burger.Core.Production;
using Burger.Core.Simulation;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The population menu, the people of a state in the location panel, and the Ledger charts (population features).</summary>
public sealed partial class Hud
{
    private string _ledgerMetric = "population";
    private int _ledgerTab = 1;
    private string? _ledgerCommodity;
    private bool _ledgerWorld;
    private GlobalTradeLedgerCache? _globalTradeLedgerCache;
    private IndustryLedgerCache? _industryLedgerCache;
    private Task<GlobalTradeLedgerCache?>? _globalLedgerBuild;
    private Task<IndustryLedgerCache?>? _industryLedgerBuild;
    private ImmutableArray<RenderCommodityBalance> _ledgerBalanceSource;
    private Dictionary<string, Dictionary<string, RenderCommodityBalance>> _ledgerBalanceIndex = new(StringComparer.OrdinalIgnoreCase);
    private Game? _ledgerFallbackGame;
    private Country? _ledgerFallbackSubject;
    private int _ledgerFallbackMapVersion = -1;
    public bool LedgerWorld { get => _ledgerWorld; set => _ledgerWorld = value; }
    public string LedgerMetric
    {
        get => _ledgerMetric;
        set
        {
            _ledgerMetric = value;
            if (value.Equals("industry", StringComparison.OrdinalIgnoreCase)) _ledgerTab = 2;
            else if (value.Equals("global", StringComparison.OrdinalIgnoreCase) || value.Equals("global_trade", StringComparison.OrdinalIgnoreCase)) _ledgerTab = 1;
        }
    }
    private string? _releaseArmed;

    private sealed class GlobalTradeLedgerCache
    {
        public Game Game = null!;
        public int Day;
        public int MapVersion;
        public int SubjectStamp;
        public Country? Subject;
        public float Scale;
        public float PanelWidth;
        public int HistoryCount;
        public int ActiveRouteCount;
        public List<Country> Countries = new();
        public List<GlobalTradeLedgerRow> Rows = new();
        public double TradeVolume;
        public int Shortages;
        public string TradeSummary = "";
        public string ShortageSummary = "";
        public string PolicySummary = "";
        public string RouteSummary = "";
        public string RecentRouteText = "";
        public string? DrilldownCommodity;
        public List<TradeCountryLedgerRow> Drilldown = new();
    }

    private sealed class GlobalTradeLedgerRow
    {
        public CommodityDef Def = null!;
        public double Satisfied;
        public double AveragePrice;
        public string Name = "";
        public string Output = "";
        public string Demand = "";
        public string Trade = "";
        public string Stockpile = "";
        public string Price = "";
        public string Met = "";
        public string Tooltip = "";
    }

    private sealed class TradeCountryLedgerRow
    {
        public Country Country = null!;
        public string Name = "";
        public string Trade = "";
        public string Met = "";
        public double Satisfied;
    }

    private sealed class IndustryLedgerCache
    {
        public Game Game = null!;
        public int Day;
        public int MapVersion;
        public int SubjectStamp;
        public Country Country = null!;
        public float Scale;
        public float PanelWidth;
        public List<IndustryLedgerRow> Rows = new();
        public int ProducingStates;
        public double Output;
        public double PopulationDemand;
        public double IndustryDemand;
        public int Shortages;
        public int Farms;
        public int Workshops;
        public int Markets;
        public int Infrastructure;
        public int QueuedProjects;
        public string IdentityName = "";
        public float IdentityNameWidth;
        public string Summary = "";
        public string DemandSummary = "";
        public string BuildingSummary = "";
        public string PlanSummary = "";
        public List<ConstructionLedgerRow> Construction = new();
        public double ConstructionCapacity;
        public double ConstructionLabour;
        public int ConstructionFactories;
        public int BlockedProjects;
        public string ConstructionSummary = "";
        public string? ShortageCommodity;
        public double ShortageAmount;
        public Country? Supplier;
        public BuildingType? Build;
        public State? BuildState;
        public bool CanBuild;
        public State? MethodState;
        public List<IndustryMethodChoice> Methods = new();
    }

    private sealed class IndustryLedgerRow
    {
        public CommodityDef Def = null!;
        public double Output;
        public double PopulationUse;
        public double IndustryDemand;
        public double Stockpile;
        public double Demand;
        public double Satisfied;
        public double Price;
        public string Name = "";
        public string OutputText = "";
        public string PopulationText = "";
        public string IndustryText = "";
        public string StockpileText = "";
        public string Met = "";
        public string Tooltip = "";
    }

    private sealed class IndustryMethodChoice
    {
        public State State = null!;
        public string Output = "";
        public List<IndustryRecipe> Methods = new();
    }

    private sealed class ConstructionLedgerRow
    {
        public string Target = "";
        public string Method = "";
        public string Allocation = "";
        public string Capacity = "";
        public string Progress = "";
        public string Status = "";
        public double ProgressRatio;
        public bool Blocked;
    }

    private void InvalidateLedgerCache()
    {
        _globalTradeLedgerCache = null;
        _industryLedgerCache = null;
    }

    private int LedgerPolicyStamp(Country? country)
    {
        if (country == null) return 0;
        var policy = G.TradeNetwork.PolicyOf(country);
        var hash = new HashCode();
        hash.Add(country);
        hash.Add(policy.TariffRate);
        hash.Add(policy.ImportPriority);
        hash.Add(policy.Protectionism);
        hash.Add(policy.CustomsRevenue);
        hash.Add(policy.AgreementsSigned);
        return hash.ToHashCode();
    }

    private Dictionary<string, Dictionary<string, RenderCommodityBalance>> LedgerBalanceIndex(RenderSnapshot snapshot)
    {
        // Build once per immutable balance array. BuildFast snapshots reuse this array between
        // economic refreshes, so movement ticks do not rebuild the index while a ledger is open.
        if (_ledgerBalanceSource.Equals(snapshot.CommodityBalances)) return _ledgerBalanceIndex;
        var index = new Dictionary<string, Dictionary<string, RenderCommodityBalance>>(StringComparer.OrdinalIgnoreCase);
        foreach (var balance in snapshot.CommodityBalances)
        {
            if (!index.TryGetValue(balance.CountryTag, out var byCommodity))
                index[balance.CountryTag] = byCommodity = new Dictionary<string, RenderCommodityBalance>(StringComparer.OrdinalIgnoreCase);
            byCommodity[balance.CommodityKey] = balance;
        }
        _ledgerBalanceSource = snapshot.CommodityBalances;
        _ledgerBalanceIndex = index;
        return index;
    }

    /// <summary>Detects same-day building and method changes without re-reading market balances.</summary>
    private int LedgerSubjectStamp(Country? country)
    {
        if (country == null) return 0;
        var hash = new HashCode();
        hash.Add(country);
        foreach (var state in country.OwnedStates)
        {
            hash.Add(state);
            hash.Add(state.Buildings.Count);
            foreach (var building in state.Buildings)
            {
                hash.Add(building.Key);
                hash.Add(building.Value);
            }
        }
        foreach (var project in country.Construction)
        {
            hash.Add(project.State);
            hash.Add(project.Building.Key);
            hash.Add(project.Progress);
            hash.Add(project.Factories);
            hash.Add(project.Method);
            hash.Add(project.LabourAllocated);
            hash.Add(project.Capacity);
            hash.Add(project.BlockedReason);
        }
        foreach (var project in G.Population.GoldProjects.Where(p => p.Country == country))
        {
            hash.Add(project.State);
            hash.Add(project.Building.Key);
            hash.Add(project.MonthsLeft);
        }
        foreach (var method in G.TradeNetwork.SelectedMethods)
        {
            if (method.Key.State.Owner != country) continue;
            hash.Add(method.Key.State);
            hash.Add(method.Key.Output);
            hash.Add(method.Value);
        }
        return hash.ToHashCode();
    }

    /// <summary>The country shown by the Industry ledger, including observer mode.</summary>
    private Country? LedgerSubject =>
        G.World.Player
        ?? (Ui.SelectedCountry is { Exists: true } selected ? selected : null)
        ?? (Ui.SelectedLocation?.State?.Owner is { Exists: true } owner ? owner : null)
        ?? FallbackLedgerSubject();

    private Country? FallbackLedgerSubject()
    {
        if (_ledgerFallbackGame == G && _ledgerFallbackMapVersion == G.MapVersion) return _ledgerFallbackSubject;
        _ledgerFallbackGame = G;
        _ledgerFallbackMapVersion = G.MapVersion;
        _ledgerFallbackSubject = G.World.Countries.Where(c => c.Exists).OrderByDescending(c => c.OwnedStates.Count).FirstOrDefault();
        return _ledgerFallbackSubject;
    }

    private static string ConstructionMethodLabel(ConstructionProject project)
    {
        string key = !string.IsNullOrWhiteSpace(project.Method) ? project.Method : project.Building.Method;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = project.Building.Category.Equals("factory", StringComparison.OrdinalIgnoreCase)
                ? "civilian_factory"
                : project.Building.Category.Equals("infrastructure", StringComparison.OrdinalIgnoreCase)
                    || project.Building.Category.Equals("service", StringComparison.OrdinalIgnoreCase)
                    ? "public_works"
                    : project.Building.Key.Equals("workshop", StringComparison.OrdinalIgnoreCase)
                        ? "workshop"
                        : "manual";
        }
        return key.Replace('_', ' ');
    }

    private static bool IsFactoryConstruction(string method) => method.Equals("civilian factory", StringComparison.OrdinalIgnoreCase)
        || method.Equals("civilian-factory", StringComparison.OrdinalIgnoreCase);

    public static string People(double n) => n >= 1e9 ? $"{n / 1e9:0.##}B" : n >= 1e6 ? $"{n / 1e6:0.##}M" : n >= 1e4 ? $"{n / 1e3:0}k" : n >= 1e3 ? $"{n / 1e3:0.#}k" : $"{n:0}";

    /// <summary>A stacked bar of shares with a tooltip listing them.</summary>
    private void ShareBar(Rectangle bar, List<(string Key, double Size)> parts, Func<string, (string Name, Rgb Color)> info, string title)
    {
        double total = parts.Sum(p => p.Size);
        if (total <= 0) return;
        float bx = bar.X;
        foreach (var (key, size) in parts)
        {
            float bw = (float)(size / total * bar.Width);
            if (bw <= 0) continue;
            var (_, col) = info(key);
            Raylib.DrawRectangleRec(new Rectangle(bx, bar.Y, bw, bar.Height), C(col));
            bx += bw;
        }
        Raylib.DrawRectangleLinesEx(bar, 1, new Color(0, 0, 0, 180));
        if (Gui.Hover(bar))
            Gui.Tooltip(title + "\n" + string.Join("\n", parts.Take(12).Select(p => $"{info(p.Key).Name}: {People(p.Size)} ({p.Size / total:P1})")) + (parts.Count > 12 ? $"\n... and {parts.Count - 12} more" : ""));
    }

    /// <summary>A pie chart; the hovered slice grows and its share shows in a tooltip. Returns the hovered slice or -1.</summary>
    private int Pie(Vector2 c, float r, IReadOnlyList<(string Name, double Value, Rgb Color)> parts, string title, Func<double, string>? amount = null)
    {
        double total = parts.Sum(p => Math.Max(0, p.Value));
        if (total <= 0) return -1;
        var d = Viewport.Mouse - c;
        bool inside = d.Length() <= r && Gui.Hover(new Rectangle(c.X - r, c.Y - r, 2 * r, 2 * r));
        float mouseAngle = MathF.Atan2(d.Y, d.X) * 180 / MathF.PI;
        if (mouseAngle < -90) mouseAngle += 360;
        int hover = -1;
        float a = -90;
        Raylib.DrawCircleV(c, r + 1.5f * S, new Color(0, 0, 0, 150));
        for (int i = 0; i < parts.Count; i++)
        {
            float sweep = (float)(Math.Max(0, parts[i].Value) / total * 360);
            if (sweep <= 0) continue;
            bool hov = inside && mouseAngle >= a && mouseAngle < a + sweep;
            if (hov) hover = i;
            Raylib.DrawCircleSector(c, hov ? r + 3 * S : r, a, a + sweep, Math.Max(2, (int)(sweep / 3)), C(parts[i].Color));
            a += sweep;
        }
        if (hover >= 0)
        {
            var h = parts[hover];
            Gui.Tooltip($"{title}\n{h.Name}: {h.Value / total:P1}" + (amount != null ? $" ({amount(h.Value)})" : ""));
        }
        return hover;
    }

    private List<(string, double, Rgb)> Parts(List<(string Key, double Size)> parts, Func<string, (string Name, Rgb Color)> info, int max = 8)
    {
        var result = parts.Take(max).Select(p => { var (n, col) = info(p.Key); return (n, p.Size, col); }).ToList();
        double rest = parts.Skip(max).Sum(p => p.Size);
        if (rest > 0) result.Add(("Others", rest, new Rgb(110, 110, 110)));
        return result;
    }

    private (string, Rgb) CultInfo(string k) => G.Population.Culture(k) is { } c ? (c.Name, c.Color) : (k, Rgb.Gray);
    private (string, Rgb) RelInfo(string k) => G.Population.Religion(k) is { } r ? (r.Name, r.Color) : (k, Rgb.Gray);
    private (string, Rgb) ClassInfo(string k) => G.Population.PopTypeByKey.TryGetValue(k, out var t) ? (t.Name, t.Color) : (k, Rgb.Gray);

    // ------------------------------------------------------------------------------------- population menu
    private bool _popAllStates;

    private void DrawPopulationPanel()
    {
        var player = PoliticsSubject;
        var P = G.Population;
        if (player == null || !P.Enabled) { CloseLeft(); return; }
        var r = FitRect("pop");
        r.Width = 450 * S;
        Gui.Panel(r, "Population");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var snapshotPlayer = _app.Snapshot.Player;
        var states = player.OwnedStates;
        double total = snapshotPlayer?.Population ?? P.TotalOf(player);
        double growth = snapshotPlayer?.PopulationGrowth ?? states.Sum(s => P.HasPops(s) ? P.Of(s).LastGrowth : 0);
        Gui.Label($"{People(total)} people", x, y, 18, Gui.Gold, FontKind.Bold);
        string gtxt = $"{(growth >= 0 ? "+" : "-")}{People(Math.Abs(growth))}";
        float gw = Gui.Measure(gtxt, 14, FontKind.Bold).X + 24 * S;
        Gui.Label(gtxt + " / month", x + w - Gui.Measure(gtxt + " / month", 14).X, y + 3 * S, 14, growth >= 0 ? Gui.Good : Gui.Bad);
        y += 28 * S;
        // the cultures, religions and classes below count the people outside our colonies
        var home = P.HomeStates(player).ToList();
        double homeTotal = snapshotPlayer?.HomePopulation ?? home.Sum(P.TotalOf);
        double colonial = total - homeTotal;
        if (colonial >= 1)
        {
            string ct = $"{People(homeTotal)} at home, {People(colonial)} in colonies (not counted below)";
            Gui.Label(Gui.Fit(ct, w, 13), x, y, 13, Gui.TextDim);
            if (Gui.Hover(new Rectangle(x, y, w, 18 * S))) Gui.Tooltip("Colonies: states marked as colonies, or states that are not our cores and not connected by land to the capital.\nTheir people do not count for our cultures, religions and stability, and their cultures cannot be accepted.");
            y += 20 * S;
        }
        var cp = P.Of(player);
        Gui.Label(Gui.Fit($"{P.Culture(cp.Culture)?.Name ?? "-"}" + (P.ReligionEnabled ? $" · {P.Religion(cp.Religion)?.Name ?? "-"}" : ""), w, 14), x, y, 14);
        y += 22 * S;
        var (acc, faith) = snapshotPlayer is { } ps
            ? (ps.AcceptedCultureShare, ps.StateReligionShare)
            : P.Shares(player);
        // only worth a line when something is off; the numbers stay in the tooltip of the line above
        bool shares = acc < 0.99 || (P.ReligionEnabled && faith < 0.9);
        if (shares) Gui.Label($"Accepted culture {acc:P0}" + (P.ReligionEnabled ? $" · state religion {faith:P0}" : ""), x, y, 13, acc >= 0.9 ? Gui.TextDim : Gui.Bad);
        if (Gui.Hover(new Rectangle(x, y - 22 * S, w, shares ? 40 * S : 22 * S)))
            Gui.Tooltip("People of unaccepted cultures pay less tax, give less manpower and slowly take your culture.\n" +
                        (P.ReligionEnabled ? "People of other faiths pay a little less tax and slowly convert.\n" : "") +
                        "Both lower stability (with politics).\nCultures of your culture group count as accepted.");
        y += shares ? 24 * S : 4 * S;

        Gui.Section("Cultures", x, y, w, "ui_cultures");
        y += 26 * S;
        var cultureSnapshot = snapshotPlayer?.CultureBreakdown ?? ImmutableArray<RenderPopulationShare>.Empty;
        var cultures = snapshotPlayer is { } ? cultureSnapshot.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(home, p => p.Culture);
        float pr = 52 * S, listX = x + 2 * pr + 14 * S;
        float pieTop = y;
        Pie(new Vector2(x + pr, y + pr), pr, Parts(cultures, CultInfo), "Cultures", People);
        float lw = x + w - listX;
        foreach (var (key, size) in cultures.Take(5))
        {
            var share = cultureSnapshot.FirstOrDefault(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var (name, col) = snapshotPlayer is { } ? (share.Name, share.Color) : CultInfo(key);
            bool accepted = snapshotPlayer is { } ? share.Accepted : P.IsAccepted(player, key);
            Raylib.DrawRectangleRec(new Rectangle(listX, y + 4 * S, 10 * S, 10 * S), C(col));
            Gui.Label(Gui.Fit($"{name}{(key == cp.Culture ? " (primary)" : accepted ? " (accepted)" : "")}", lw - 130 * S, 13), listX + 14 * S, y, 13, accepted ? Gui.Text : Gui.TextDim);
            Gui.Label($"{size / Math.Max(1, homeTotal):P0}", listX + lw - 112 * S, y, 13);
            if (!accepted)
            {
                var reason = P.CannotAccept(player, key);
                var (cost, cur) = P.AcceptCost(player);
                if (ActionButton(new Rectangle(x + w - 64 * S, y - 2 * S, 64 * S, 19 * S), "Accept", reason == null,
                        reason ?? $"Accept {name} culture: its people count as ours (full tax and manpower){(cost > 0 ? $"\nCost: {cost:0} {cur}" : "")}", false, 12))
                {
                    var err = P.AcceptCulture(player, key);
                    if (err != null) Ui.SetStatus(err);
                }
            }
            y += 21 * S;
        }
        y = Math.Max(y, pieTop + 2 * pr) + 10 * S;
        if (G.CultureLife.Enabled) y = DrawCultureContent(player, x, y, w) + 8 * S;
        if (P.ReligionEnabled)
        {
            Gui.Section("Religions", x, y, w, "ui_religions");
            y += 26 * S;
            var religionSnapshot = snapshotPlayer?.ReligionBreakdown ?? ImmutableArray<RenderPopulationShare>.Empty;
            var rel = snapshotPlayer is { } ? religionSnapshot.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(home, p => p.Religion);
            pieTop = y;
            Pie(new Vector2(x + pr, y + pr), pr, Parts(rel, RelInfo), "Religions", People);
            foreach (var (key, size) in rel.Take(5))
            {
                var share = religionSnapshot.FirstOrDefault(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var (name, col) = snapshotPlayer is { } ? (share.Name, share.Color) : RelInfo(key);
                Raylib.DrawRectangleRec(new Rectangle(listX, y + 4 * S, 10 * S, 10 * S), C(col));
                Gui.Label(Gui.Fit(name + (key == cp.Religion ? " (state religion)" : ""), lw - 60 * S, 13), listX + 14 * S, y, 13);
                string pct = $"{size / Math.Max(1, homeTotal):P0}";
                Gui.Label(pct, x + w - Gui.Measure(pct, 13).X, y, 13);
                y += 21 * S;
            }
            y = Math.Max(y, pieTop + 2 * pr) + 10 * S;
        }
        if (P.ClassesEnabled)
        {
            Gui.Section("Social classes", x, y, w, "ui_classes");
            y += 26 * S;
            var classSnapshot = snapshotPlayer?.ClassBreakdown ?? ImmutableArray<RenderPopulationShare>.Empty;
            ShareBar(new Rectangle(x, y, w, 16 * S), snapshotPlayer is { } ? classSnapshot.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(home, p => p.Type), ClassInfo, "Social classes");
            y += 26 * S;
        }
        if (P.ColonizationEnabled)
        {
            var colonies = P.ColoniesOf(player).ToList();
            Gui.Section($"Colonies ({colonies.Count} settling, {P.MaxColonies(player)} per region)", x, y, w, "ui_colonies");
            y += 26 * S;
            if (colonies.Count == 0) { Gui.Label("Click an unowned state on the map to send colonists.", x, y, 13, Gui.TextDim); y += 22 * S; }
            foreach (var s in colonies)
            {
                var sp = P.Of(s);
                var party = sp.PartyOf(player)!;
                string rival = sp.IsContested ? (sp.Main.Country == player ? sp.Counter.Country : sp.Main.Country)?.Name ?? "" : "";
                Gui.Label(Gui.Fit(s.DisplayName + (rival.Length > 0 ? " · vs " + rival : P.RegionOf(s) is { } rg ? " · " + rg.Name : ""), w * 0.34f, 14), x, y, 14);
                Gui.Bar(new Rectangle(x + w * 0.36f, y + 4 * S, w * 0.22f, 10 * S), (float)(party.Progress / 100), new Color(110, 190, 90, 255));
                var why = P.CannotReinforce(player, s);
                string rtip = why ?? $"Send another wave of settlers for {P.ReinforceGold(s, player):0} gold: +{G.Rules.Population.ReinforceProgress(G, s, player):0}% now, and it may stop the next native attack.";
                if (ActionButton(new Rectangle(x + w * 0.6f, y - 2 * S, w * 0.19f, 20 * S), $"+{party.Reinforcements}", why == null, rtip, false, 12)) SettleCommand(GameCommand.Reinforce, s, () => P.Reinforce(player, s));
                if (ActionButton(new Rectangle(x + w - 70 * S, y - 2 * S, 70 * S, 20 * S), "Abandon", true, "Call the colonists back", false, 12)) SettleCommand(GameCommand.AbandonColony, s, () => { P.AbandonColony(s, player); return null; });
                y += 22 * S;
            }
            y += 6 * S;
        }
        if (P.IndependenceEnabled)
        {
            var moves = P.IndependenceOf(player).OrderByDescending(m => m.Progress).ToList();
            if (moves.Count > 0)
            {
                Gui.Section("Independence movements", x, y, w, "ui_separatists");
                y += 26 * S;
                foreach (var m in moves.Take(4))
                {
                    var (name, col) = CultInfo(m.Culture);
                    int n = P.SeparatistStates(player, m.Culture).Count;
                    Raylib.DrawRectangleRec(new Rectangle(x, y + 4 * S, 10 * S, 10 * S), C(col));
                    Gui.Label(Gui.Fit($"{name} ({n} states)", w * 0.4f, 13), x + 14 * S, y, 13);
                    var bar = new Rectangle(x + w * 0.42f, y + 4 * S, w * 0.2f, 10 * S);
                    Gui.Bar(bar, (float)(m.Progress / 100), new Color(200, 80, 70, 255));
                    if (Gui.Hover(bar)) Gui.Tooltip($"{m.Progress:0}% - at 100% they declare independence (and fight for it).\nAccepting their culture ends the movement.");
                    var why = P.CannotSuppressIndependence(m);
                    if (ActionButton(new Rectangle(x + w * 0.64f, y - 2 * S, w * 0.18f, 19 * S), "Suppress", why == null,
                            why ?? $"Set the movement back ({P.SuppressIndependenceCost(m):0}); costs some stability. Each time costs more.", false, 11))
                        P.SuppressIndependence(m);
                    bool armed = _releaseArmed == m.Culture;
                    if (ActionButton(new Rectangle(x + w * 0.83f, y - 2 * S, w * 0.17f, 19 * S), armed ? "Sure?" : "Release", true,
                            $"Let their {n} states go in peace as an independent country" + (armed ? "\nClick again to confirm" : ""), armed, 11))
                    {
                        if (armed) { P.GrantIndependence(m); _releaseArmed = null; }
                        else _releaseArmed = m.Culture;
                    }
                    y += 22 * S;
                }
                y += 6 * S;
            }
        }
        // most populous states
        Gui.Section("States by population", x, y, w, "ui_states_pop");
        y += 26 * S;
        var stateSnapshots = _app.Snapshot.States.Where(s => s.OwnerTag != null && s.OwnerTag.Equals(player.Tag, StringComparison.OrdinalIgnoreCase) && s.PopulationTotal > 0)
            .OrderByDescending(s => s.PopulationTotal).ToList();
        const int topN = 6;
        if (stateSnapshots.Count > topN)
        {
            if (Gui.Button(new Rectangle(x + w - 110 * S, y - 26 * S, 110 * S, 20 * S), _popAllStates ? "Show top 6" : $"Show all {stateSnapshots.Count}", true, null, false, 12)) _popAllStates = !_popAllStates;
            if (!_popAllStates) stateSnapshots = stateSnapshots.Take(topN).ToList();
        }
        FitEnd("pop", r, y + stateSnapshots.Count * 22 * S);
        float top = Gui.BeginScroll("popstates", new Rectangle(x, y, w, Math.Min(stateSnapshots.Count * 22 * S, r.Y + r.Height - y - 10 * S)), stateSnapshots.Count * 22 * S);
        float ly = top;
        foreach (var stateSnapshot in stateSnapshots)
        {
            var s = states.FirstOrDefault(q => q.Key.Equals(stateSnapshot.Key, StringComparison.OrdinalIgnoreCase));
            if (s == null) continue;
            double totalState = stateSnapshot.PopulationTotal;
            double cap = stateSnapshot.PopulationCapacity;
            var row = new Rectangle(x, ly, w - 10 * S, 20 * S);
            bool hover = Gui.Hover(row);
            if (hover) Raylib.DrawRectangleRec(row, new Color(255, 255, 255, 18));
            bool core = s.Cores.Contains(player);
            if (core) Raylib.DrawRectangleRec(new Rectangle(x, ly + 2 * S, 3 * S, 16 * S), Gui.Gold);
            Gui.Label(Gui.Fit(s.DisplayName, w * 0.5f, 14), x + 8 * S, ly + 1 * S, 14, core ? Gui.Text : Gui.TextDim);
            string pt = People(totalState);
            Gui.Label(pt, x + w * 0.66f - Gui.Measure(pt, 14, FontKind.Bold).X, ly + 1 * S, 14, Gui.Text, FontKind.Bold);
            Gui.Bar(new Rectangle(x + w * 0.70f, ly + 6 * S, w * 0.26f, 8 * S), (float)Math.Min(1, totalState / Math.Max(1, cap)), new Color(200, 170, 90, 255));
            if (hover)
            {
                Gui.Tooltip($"{s.DisplayName}: {People(totalState)} people (room for {People(cap)})\n{(s.Cores.Contains(player) ? "Core state" : "Not a core state")}\nClick to look at it");
                if (Gui.Clicked(row))
                {
                    var loc = s.Locations.FirstOrDefault(l => l.IsLand) ?? s.Locations[0];
                    Ui.SelectedLocation = loc;
                    Map.Selected = loc;
                    Cam.LookAt(Map.LocationPos(loc));
                    Ui.Left = LeftPanel.Location;
                }
            }
            ly += 22 * S;
        }
        Gui.EndScroll();
    }

    /// <summary>People, colonization and gold buildings of a state (in the location panel). Returns the new y.</summary>
    /// <summary>The three largest pop groups of an owned state: what they want and how they react to the government.</summary>
    private float DrawStateMoods(State st, float x, float y, float w)
    {
        if (st.Owner == null || !G.Politics.PopsEnabled) return y;
        var stateSnapshot = _app.Snapshot.States.FirstOrDefault(s => s.Key == st.Key);
        var moods = stateSnapshot.Key == st.Key ? stateSnapshot.Moods : ImmutableArray<RenderPopMood>.Empty;
        if (moods.Length == 0) return y;
        foreach (var m in moods)
        {
            var tone = m.Reaction switch { "Approve" => Gui.Good, "Comply" => Gui.Text, "Disapprove" => Gui.Gold, _ => Gui.Bad };
            var row = new Rectangle(x, y, w, 18 * S);
            string line = $"{m.Name}: {m.Reaction} ({m.Opinion:+0;-0;0})";
            Gui.Label(Gui.Fit(line, w, 13), x, y, 13, tone);
            if (Gui.Hover(row))
                Gui.Tooltip($"{m.Name} · {People(m.Size)}\nWants: {Localisation.GetOrPretty(m.Objective)}\nOpinion {m.Opinion:0} · grievance {m.Grievance:0} · radicalism {m.Radicalism:0}"
                    + (m.PreferredParty.Length > 0 ? $"\nLeans to: {G.Politics.PartyDisplayName(st.Owner, m.PreferredParty)}" : ""));
            y += 18 * S;
        }
        return y + 2 * S;
    }

    private float DrawStatePopulation(State st, float x, float y, float w, bool header = true)
    {
        var P = G.Population;
        if (!P.Enabled) return y;
        var player = G.World.Player;
        var sp = P.Of(st);
        var stateSnapshot = _app.Snapshot.States.FirstOrDefault(s => s.Key == st.Key);
        bool hasSnapshot = stateSnapshot.Key == st.Key;
        if (header) { Gui.Section("People", x, y, w, "ui_people"); y += 26 * S; }
        double total = hasSnapshot ? stateSnapshot.PopulationTotal : sp.Total;
        double growth = hasSnapshot ? stateSnapshot.PopulationGrowth : sp.LastGrowth;
        double cap = hasSnapshot ? stateSnapshot.PopulationCapacity : P.Capacity(st);
        Gui.Label($"{People(total)} people", x, y, 15, Gui.Gold);
        string g = $"{(growth >= 0 ? "+" : "-")}{People(Math.Abs(growth))}/month";
        float gx = x + 110 * S;
        Gui.Label(g, gx, y + 2 * S, 13, DeltaColor(growth, 0.5));
        Gui.Label($"room for {People(cap)}", gx + Gui.Measure(g, 13).X + 10 * S, y + 2 * S, 13, total >= cap ? Gui.Bad : Gui.TextDim);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S)))
            Gui.Tooltip("People grow towards the state's room (capacity); buildings and modifiers (pop_growth, pop_capacity) change both.\nTax and manpower grow with the people.");
        y += 22 * S;
        if (hasSnapshot ? !stateSnapshot.CultureBreakdown.IsDefaultOrEmpty : sp.Pops.Count > 0)
        {
            // small pies: cultures, religions, classes
            float r = 30 * S, px = x;
            void SmallPie(string title, List<(string Key, double Size)> parts, Func<string, (string, Rgb)> info)
            {
                Pie(new Vector2(px + r, y + r), r, Parts(parts, info), title, People);
                string top = parts.Count > 0 ? info(parts[0].Key).Item1 : "-";
                Gui.Label(Gui.Fit(title, 2 * r + 60 * S, 11), px + 2 * r + 6 * S, y + r - 16 * S, 11, Gui.TextDim);
                Gui.Label(Gui.Fit(top, 2 * r + 60 * S, 12), px + 2 * r + 6 * S, y + r - 2 * S, 12);
                px += 2 * r + 76 * S;
            }
            var cultureParts = hasSnapshot ? stateSnapshot.CultureBreakdown.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(new[] { st }, p => p.Culture);
            SmallPie("Cultures", cultureParts, key => hasSnapshot ? (stateSnapshot.CultureBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Name,
                stateSnapshot.CultureBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Color) : CultInfo(key));
            if (P.ReligionEnabled)
            {
                var religionParts = hasSnapshot ? stateSnapshot.ReligionBreakdown.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(new[] { st }, p => p.Religion);
                SmallPie("Religions", religionParts, key => hasSnapshot ? (stateSnapshot.ReligionBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Name,
                    stateSnapshot.ReligionBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Color) : RelInfo(key));
            }
            if (P.ClassesEnabled)
            {
                var classParts = hasSnapshot ? stateSnapshot.ClassBreakdown.Select(q => (q.Key, q.Size)).ToList() : P.Breakdown(new[] { st }, p => p.Type);
                SmallPie("Classes", classParts, key => hasSnapshot ? (stateSnapshot.ClassBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Name,
                    stateSnapshot.ClassBreakdown.First(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Color) : ClassInfo(key));
            }
            y += 2 * r + 8 * S;
            string major = hasSnapshot
                ? stateSnapshot.CultureBreakdown.FirstOrDefault(q => q.Primary).Name ?? "-"
                : P.Culture(P.MajorityCulture(st))?.Name ?? "-";
            string religion = hasSnapshot
                ? stateSnapshot.ReligionBreakdown.FirstOrDefault(q => q.Primary).Name ?? "-"
                : P.Religion(P.MajorityReligion(st))?.Name ?? "-";
            string text = $"Mostly {major}" + (P.ReligionEnabled ? $", {religion}" : "");
            Gui.Label(Gui.Fit(text, w, 13), x, y, 13, Gui.TextDim);
            y += 20 * S;
            y = DrawStateMoods(st, x, y, w);
            // what the state and its people hold against whoever did it to them
            var who = new List<Burger.Core.Memories.Subject> { Burger.Core.Memories.Subject.Of(st) };
            who.AddRange(sp.Pops.Select(p => Burger.Core.Memories.Subject.Pop(st.ScopeKey, p.Culture, p.Religion, p.Type)));
            y += DrawRememberedActs(G.Memory.Strongest(who, 4).ToList(), "The people remember", x, y, w);
        }
        // colonization of unowned land
        if (P.ColonizationEnabled && st.Owner == null && player != null)
        {
            if (P.RegionOf(st) is { } region)
            {
                Gui.Label($"Colonial region: {region.Name}", x, y, 13, Gui.TextDim);
                if (Gui.Hover(new Rectangle(x, y, w, 18 * S))) Gui.Tooltip($"Colonists settle a region under a limit per region. With {(int)G.Data.Defines.Get("colonial_nation_states", 5)} colonized states here, a colonial nation forms as your subject and pays you {region.TaxShare:P0} of its income.");
                y += 20 * S;
            }
            y = DrawNativePeoples(st, x, y, w);
            var mine = sp.PartyOf(player);
            foreach (var party in new[] { sp.Main, sp.Counter })
            {
                if (party.Country == null) continue;
                Gui.Label($"{(party == sp.Counter ? "Counter-settled by" : "Being settled by")} {party.Country.Name}" + (party.Reinforcements > 0 ? $"  ({party.Reinforcements} guarding)" : ""), x, y, 14);
                y += 20 * S;
                Gui.Bar(new Rectangle(x, y, w, 10 * S), (float)(party.Progress / 100), party == sp.Counter ? new Color(200, 150, 70, 255) : new Color(110, 190, 90, 255));
                y += 16 * S;
            }
            if (sp.IsContested)
            {
                double lead = G.Rules.Population.CounterSettleMainShare(G, st, Math.Max(sp.Main.Progress, sp.Counter.Progress), Math.Min(sp.Main.Progress, sp.Counter.Progress));
                Gui.Label($"When settled the leader keeps {lead:P0} of the land, the rival {1 - lead:P0}.", x, y, 13, Gui.TextDim);
                y += 20 * S;
            }
            if (mine != null)
            {
                var why = P.CannotReinforce(player, st);
                if (Gui.Button(new Rectangle(x, y, w, 26 * S), $"Reinforce ({P.ReinforceGold(st, player):0} gold)", why == null,
                        why ?? $"Another wave of settlers: +{G.Rules.Population.ReinforceProgress(G, st, player):0}% now, and it may stop the next native attack (it is then spent).", false, 14))
                    SettleCommand(GameCommand.Reinforce, st, () => P.Reinforce(player, st));
                y += 32 * S;
            }
            else if (!sp.IsContested || sp.Counter.Country == null)
            {
                bool counter = sp.Main.Country != null;
                var reason = G.Exploration.Enabled && !st.Locations.Any(l => l.IsLand && G.Exploration.IsCharted(l))
                    ? "Terra incognita: chart this land first (send a fleet or an army)" : P.CannotColonize(player, st);
                string cost = P.ColonizeCost > 0 ? $" ({P.ColonizeCost:0} gold)" : "";
                if (Gui.Button(new Rectangle(x, y, w, 26 * S), (counter ? "Counter-settle" : "Send colonists") + cost, reason == null,
                        reason ?? (counter ? $"Settle the same land: whoever is ahead when it is settled keeps the larger part, the other gets the rest as a state of its own. {NativeLine(st)}"
                            : "Colonists settle the land over the next years (they cost gold every month); natives may resist. Then it becomes your state."), false, 14))
                    SettleCommand(GameCommand.Colonize, st, () => P.Colonize(player, st));
                y += 32 * S;
            }
        }
        // buildings bought with gold (no production feature)
        if (player != null && st.Owner == player)
        {
            var gold = P.GoldBuildings().ToList();
            if (gold.Count > 0)
            {
                Gui.Section("Build", x, y, w, "ui_build");
                y += 26 * S;
                float bx = x;
                foreach (var b in gold)
                {
                    int queued = P.GoldProjects.Count(p => p.State == st && p.Building == b);
                    string label = $"{b.Name} ({P.GoldCost(b):0})" + (queued > 0 ? $" +{queued}" : "");
                    float bw = Math.Max(120 * S, Gui.Measure(label, 12).X + 16 * S + 24 * S);
                    if (bx + bw > x + w) { bx = x; y += 28 * S; }
                    var reason = P.CannotGoldBuild(player, b, st);
                    string tip = $"{b.Name}\n{b.Description}\nLevel {st.Buildings.GetValueOrDefault(b.Key)}/{b.MaxLevel}, {P.BuildMonths(b)} months to build, {P.GoldCost(b):0} gold" +
                                 (b.Definition.Get("modifier") is { } m && m.Children != null ? "\nPer level: " + string.Join(", ", m.All.Where(c => c.Value != null).Select(c => ModifierText(c.Key, c.AsDouble()))) : "") +
                                 (reason != null ? $"\n\n{reason}" : "");
                    if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), label, reason == null, tip, false, 12))
                    {
                        var err = P.GoldBuild(player, b, st);
                        if (err != null) Ui.SetStatus(err);
                    }
                    BuildingIcon(b.Key, new Rectangle(bx + 3 * S, y + 2 * S, 20 * S, 20 * S), reason != null);
                    bx += bw + 6 * S;
                }
                y += 30 * S;
            }
        }
        return y;
    }

    // ------------------------------------------------------------------------------------- ledger
    private GlobalTradeLedgerCache? GetGlobalTradeLedgerCache(float w)
    {
        var cache = _globalTradeLedgerCache;
        bool stale = cache == null || cache.Game != G || cache.Day != G.World.Date.TotalDays ||
                     cache.MapVersion != G.MapVersion || cache.Scale != S || cache.PanelWidth != w ||
                     cache.HistoryCount != G.TradeNetwork.History.Count ||
                     cache.ActiveRouteCount != G.TradeNetwork.ActiveRoutes.Count;
        if (_globalLedgerBuild is { IsCompleted: true } task)
        {
            _globalLedgerBuild = null;
            try { cache = task.GetAwaiter().GetResult(); _globalTradeLedgerCache = cache; }
            catch (Exception ex) { Log.Error($"Ledger cache build failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        // First-open and active-play refreshes never block the render thread; the last complete cache remains visible until replacement completes.
        if (stale && _globalLedgerBuild == null)
            _globalLedgerBuild = Task.Run(() => BuildGlobalTradeLedgerCache(w));
        return cache;
    }

    private GlobalTradeLedgerCache? BuildGlobalTradeLedgerCache(float w)
    {
        var market = G.Commodities;
        var balanceIndex = LedgerBalanceIndex(_app.Snapshot);
        (double Output, double Import, double Export, double Demand, double PopulationUse, double MilitaryUse, double Stockpile, double Price, double Satisfied) BalanceOf(Country country, string key)
        {
            if (balanceIndex.TryGetValue(country.Tag, out var byCommodity) && byCommodity.TryGetValue(key, out var snapshot))
                return (snapshot.Output, snapshot.Import, snapshot.Export, snapshot.Demand, snapshot.PopulationUse, snapshot.MilitaryUse, snapshot.Stockpile, snapshot.Price, snapshot.Satisfied);
            var live = market.BalanceOf(country, key);
            return (live.Output, live.Import, live.Export, live.Demand, live.PopulationUse, live.MilitaryUse, live.Stockpile, live.Price, live.Satisfied);
        }
        var subject = LedgerSubject;
        int day = G.World.Date.TotalDays;
        int subjectStamp = LedgerPolicyStamp(subject);
        var cache = _globalTradeLedgerCache;
        bool rebuild = cache == null
            || cache.Game != G
            || cache.Day != day
            || cache.MapVersion != G.MapVersion
            || cache.Subject != subject
            || cache.SubjectStamp != subjectStamp
            || cache.Scale != S
            || cache.PanelWidth != w
            || cache.HistoryCount != G.TradeNetwork.History.Count
            || cache.ActiveRouteCount != G.TradeNetwork.ActiveRoutes.Count;
        if (rebuild)
        {
            float nameW = w * 0.27f;
            cache = new GlobalTradeLedgerCache
            {
                Game = G,
                Day = day,
                MapVersion = G.MapVersion,
                Subject = subject,
                SubjectStamp = subjectStamp,
                Scale = S,
                PanelWidth = w,
                HistoryCount = G.TradeNetwork.History.Count,
                ActiveRouteCount = G.TradeNetwork.ActiveRoutes.Count,
                Countries = G.World.Countries.Where(c => c.Exists).ToList(),
            };
            foreach (var def in market.Defs.Values.Where(k => k.IsActive(G.World.Date)).OrderBy(k => k.Category).ThenBy(k => k.Name))
            {
                double output = 0, imports = 0, exports = 0, demand = 0, stockpile = 0, satisfied = 0, price = 0;
                foreach (var country in cache.Countries)
                {
                    var balance = BalanceOf(country, def.Key);
                    output += balance.Output;
                    imports += balance.Import;
                    exports += balance.Export;
                    demand += balance.Demand;
                    stockpile += balance.Stockpile;
                    satisfied += balance.Satisfied * balance.Demand;
                    price += balance.Price * balance.Demand;
                }
                double averagePrice = demand > 0 ? price / demand : def.Price;
                double met = demand > 0 ? satisfied / demand : 1;
                cache.Rows.Add(new GlobalTradeLedgerRow
                {
                    Def = def,
                    Satisfied = met,
                    AveragePrice = averagePrice,
                    Name = Gui.Fit(def.Name, nameW - 16 * S, 13),
                    Output = Num(output, 1),
                    Demand = Num(demand, 1),
                    Trade = $"{Num(imports, 1)} / {Num(exports, 1)}",
                    Stockpile = Num(stockpile, 1),
                    Price = averagePrice.ToString("0.##"),
                    Met = $"{met:P0}",
                    Tooltip = $"{def.Name} · {def.Category}\nOutput: {output:0.##}\nDemand: {demand:0.##}\nImports / exports: {imports:0.##} / {exports:0.##}\nStockpile: {stockpile:0.##}\nAverage price: {averagePrice:0.##}\nDemand met: {met:P1}",
                });
                cache.TradeVolume += imports;
            }
            cache.Shortages = cache.Rows.Count(row => row.Satisfied < 0.85);
            cache.TradeSummary = $"{cache.Rows.Count} active commodities · {cache.Countries.Count} markets · {cache.TradeVolume:0.#} units traded this month";
            cache.ShortageSummary = cache.Shortages == 0 ? "No global shortages" : $"{cache.Shortages} commodities below 85% satisfaction";
            cache.RouteSummary = $"Routes {cache.ActiveRouteCount} · history {cache.HistoryCount} transactions";
            var recent = G.TradeNetwork.RecentHistory(3).ToList();
            cache.RecentRouteText = recent.Count == 0 ? "" : string.Join("  ·  ", recent.Select(t => $"{t.From.DisplayName}→{t.To.DisplayName} {t.Commodity} {t.Quantity:0.#} ({t.RouteKind.ToString().ToLowerInvariant()})"));
            if (subject != null)
            {
                var policy = G.TradeNetwork.PolicyOf(subject);
                cache.PolicySummary = $"{subject.Name} policy · tariff {policy.TariffRate:P0} · imports {policy.ImportPriority:0.0} · protection {policy.Protectionism:P0} · customs {policy.CustomsRevenue:0.#} · agreements {policy.AgreementsSigned}";
            }
            _globalTradeLedgerCache = cache;
        }

        if (cache == null) throw new InvalidOperationException("Global trade ledger cache was not created");

        if (!string.Equals(cache.DrilldownCommodity, _ledgerCommodity, StringComparison.OrdinalIgnoreCase))
        {
            cache.DrilldownCommodity = _ledgerCommodity;
            cache.Drilldown.Clear();
            if (_ledgerCommodity != null && market.Defs.TryGetValue(_ledgerCommodity, out var selectedDef))
            {
                foreach (var (country, balance) in cache.Countries
                    .Select(country => (country, balance: BalanceOf(country, selectedDef.Key)))
                    .OrderByDescending(item => item.balance.Demand)
                    .Take(4))
                {
                    cache.Drilldown.Add(new TradeCountryLedgerRow
                    {
                        Country = country,
                        Name = Gui.Fit(country.Name, 130 * S, 12),
                        Trade = $"{balance.Import:0.#} in · {balance.Export:0.#} out",
                        Met = $"{balance.Satisfied:P0}",
                        Satisfied = balance.Satisfied,
                    });
                }
            }
        }
        return cache;
    }

    private IndustryLedgerCache? GetIndustryLedgerCache(float w)
    {
        var cache = _industryLedgerCache;
        bool stale = cache == null || cache.Game != G || cache.Day != G.World.Date.TotalDays ||
                     cache.MapVersion != G.MapVersion || cache.Country != LedgerSubject ||
                     cache.Scale != S || cache.PanelWidth != w;
        if (_industryLedgerBuild is { IsCompleted: true } task)
        {
            _industryLedgerBuild = null;
            try { cache = task.GetAwaiter().GetResult(); _industryLedgerCache = cache; }
            catch (Exception ex) { Log.Error($"Industry ledger cache build failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        if (stale && _industryLedgerBuild == null)
            _industryLedgerBuild = Task.Run(() => BuildIndustryLedgerCache(w));
        return cache;
    }

    private IndustryLedgerCache? BuildIndustryLedgerCache(float w)
    {
        var country = LedgerSubject;
        if (country == null) return null;
        var market = G.Commodities;
        var balanceIndex = LedgerBalanceIndex(_app.Snapshot);
        (double Output, double Import, double Export, double Demand, double PopulationUse, double MilitaryUse, double Stockpile, double Price, double Satisfied) BalanceOf(Country owner, string key)
        {
            if (balanceIndex.TryGetValue(owner.Tag, out var byCommodity) && byCommodity.TryGetValue(key, out var snapshot))
                return (snapshot.Output, snapshot.Import, snapshot.Export, snapshot.Demand, snapshot.PopulationUse, snapshot.MilitaryUse, snapshot.Stockpile, snapshot.Price, snapshot.Satisfied);
            var live = market.BalanceOf(owner, key);
            return (live.Output, live.Import, live.Export, live.Demand, live.PopulationUse, live.MilitaryUse, live.Stockpile, live.Price, live.Satisfied);
        }
        int day = G.World.Date.TotalDays;
        int subjectStamp = LedgerSubjectStamp(country);
        var cache = _industryLedgerCache;
        bool rebuild = cache == null
            || cache.Game != G
            || cache.Day != day
            || cache.MapVersion != G.MapVersion
            || cache.Country != country
            || cache.SubjectStamp != subjectStamp
            || cache.Scale != S
            || cache.PanelWidth != w;
        if (!rebuild) return cache;

        float flagW = 42 * S;
        float identityNameWidth = Math.Max(80 * S, w - flagW - 18 * S);
        cache = new IndustryLedgerCache
        {
            Game = G,
            Day = day,
            MapVersion = G.MapVersion,
            SubjectStamp = subjectStamp,
            Country = country,
            Scale = S,
            PanelWidth = w,
            IdentityNameWidth = identityNameWidth,
            IdentityName = Gui.Fit(country.Name, identityNameWidth, 18, FontKind.Bold),
        };
        foreach (var def in market.Defs.Values.Where(k => k.IsActive(G.World.Date)).OrderBy(k => k.Category).ThenBy(k => k.Name))
        {
            var balance = BalanceOf(country, def.Key);
            double industryDemand = Math.Max(0, balance.Demand - balance.PopulationUse);
            double met = balance.Demand > 0.0001 ? balance.Satisfied : 1;
            cache.Rows.Add(new IndustryLedgerRow
            {
                Def = def,
                Output = balance.Output,
                PopulationUse = balance.PopulationUse,
                IndustryDemand = industryDemand,
                Stockpile = balance.Stockpile,
                Demand = balance.Demand,
                Satisfied = balance.Satisfied,
                Price = balance.Price,
                Name = Gui.Fit(def.Name, w * 0.29f - 16 * S, 13),
                OutputText = Num(balance.Output, 1),
                PopulationText = Num(balance.PopulationUse, 1),
                IndustryText = Num(industryDemand, 1),
                StockpileText = Num(balance.Stockpile, 1),
                Met = $"{met:P0}",
                Tooltip = $"{def.Name} · {def.Category}\nState output: {balance.Output:0.##}\nPopulation demand: {balance.PopulationUse:0.##}\nIndustry and military demand: {industryDemand:0.##}\nStockpile: {balance.Stockpile:0.##}\nPrice: {balance.Price:0.##}\nDemand met: {met:P1}",
            });
        }
        cache.ProducingStates = country.OwnedStates.Count(state => market.Deposits.ContainsKey(state));
        cache.Output = cache.Rows.Sum(row => row.Output);
        cache.PopulationDemand = cache.Rows.Sum(row => row.PopulationUse);
        cache.IndustryDemand = cache.Rows.Sum(row => row.IndustryDemand);
        cache.Shortages = cache.Rows.Count(row => row.Demand > 0.0001 && row.Satisfied < 0.85);
        cache.Farms = country.OwnedStates.Sum(state => state.Buildings.GetValueOrDefault("farm"));
        cache.Workshops = country.OwnedStates.Sum(state => state.Buildings.GetValueOrDefault("workshop"));
        cache.Markets = country.OwnedStates.Sum(state => state.Buildings.GetValueOrDefault("market"));
        cache.Infrastructure = country.OwnedStates.Sum(state => state.Buildings.GetValueOrDefault("infrastructure"));
        cache.QueuedProjects = G.Population.GoldProjects.Count(project => project.Country == country);
        cache.Summary = $"{cache.ProducingStates} resource states · {cache.Output:0.#} units output / month";
        cache.DemandSummary = $"Population {cache.PopulationDemand:0.#} · industry and military {cache.IndustryDemand:0.#}";
        cache.BuildingSummary = $"Farms {cache.Farms} · workshops {cache.Workshops} · markets {cache.Markets} · infrastructure {cache.Infrastructure} · queued {cache.QueuedProjects}";
        var plan = G.TradeNetwork.Plans.GetValueOrDefault(country);
        cache.PlanSummary = plan == null ? "Economic plan: evaluating import versus build" : $"Economic plan: {plan.LastDecision} · priority {plan.Priority} · budget {plan.Budget:0.#}";

        foreach (var project in country.Construction)
        {
            string method = ConstructionMethodLabel(project);
            bool factory = IsFactoryConstruction(method);
            double ratio = project.Building.Cost > 0 ? Math.Clamp(project.Progress / project.Building.Cost, 0, 1) : 0;
            string allocation = factory
                ? $"{project.Factories} factories"
                : project.LabourAllocated > 0 ? $"{project.LabourAllocated:0.#} labour" : "0 labour";
            string status = string.IsNullOrWhiteSpace(project.BlockedReason)
                ? project.Capacity > 0 ? "active" : "awaiting capacity"
                : project.BlockedReason;
            cache.Construction.Add(new ConstructionLedgerRow
            {
                Target = $"{project.Building.Name} - {project.State.DisplayName}",
                Method = method,
                Allocation = allocation,
                Capacity = $"{project.Capacity:0.##}/day capacity",
                Progress = $"{project.Progress:0.#}/{project.Building.Cost:0.#} ({ratio:P0})",
                Status = status,
                ProgressRatio = ratio,
                Blocked = !string.IsNullOrWhiteSpace(project.BlockedReason) || project.Capacity <= 0,
            });
            cache.ConstructionCapacity += Math.Max(0, project.Capacity);
            cache.ConstructionLabour += Math.Max(0, project.LabourAllocated);
            cache.ConstructionFactories += Math.Max(0, project.Factories);
            if (!string.IsNullOrWhiteSpace(project.BlockedReason)) cache.BlockedProjects++;
        }
        cache.ConstructionSummary = cache.Construction.Count == 0
            ? "No queued construction projects"
            : $"{cache.Construction.Count} queued / {cache.ConstructionCapacity:0.##}/day capacity applied / {cache.ConstructionLabour:0.#} labour / {cache.ConstructionFactories} factories";

        var shortageRow = cache.Rows.FirstOrDefault(row => row.Demand > 0.0001 && row.Satisfied < 0.85);
        if (shortageRow != null)
        {
            cache.ShortageCommodity = shortageRow.Def.Key;
            cache.ShortageAmount = Math.Max(0.1, shortageRow.Demand - shortageRow.Output);
            cache.Supplier = G.World.Countries
                .Where(c => c.Exists && c != country)
                .Select(c => (country: c, balance: BalanceOf(c, shortageRow.Def.Key)))
                .Where(item => item.balance.Output > item.balance.Demand)
                .OrderBy(item => item.country.Index)
                .Select(item => item.country)
                .FirstOrDefault();
            string buildKey = cache.ShortageCommodity is "grain" or "fish" or "animal_products" ? "farm" : cache.ShortageCommodity is "salt" or "textiles" or "pottery" or "glass" ? "workshop" : "market";
            cache.Build = G.Data.BuildingTypes.GetValueOrDefault(buildKey);
            cache.BuildState = country.OwnedStates.OrderByDescending(G.Population.TotalOf).FirstOrDefault();
            cache.CanBuild = cache.Build != null && cache.BuildState != null && (cache.Build.Category == "factory" && G.Production.Enabled && G.Production.CivilianFactories(country) > 0
                ? G.Production.CannotBuildReason(country, cache.Build, cache.BuildState) == null
                : G.Population.CannotGoldBuild(country, cache.Build, cache.BuildState) == null);
        }

        if (country.IsPlayer)
        {
            cache.MethodState = country.OwnedStates.OrderByDescending(G.Population.TotalOf).FirstOrDefault();
            if (cache.MethodState != null)
            {
                foreach (var output in G.TradeNetwork.Recipes.Values.Select(recipe => recipe.Output).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var methods = G.TradeNetwork.MethodsFor(country, cache.MethodState, output).ToList();
                    if (methods.Count <= 1) continue;
                    cache.Methods.Add(new IndustryMethodChoice { State = cache.MethodState, Output = output, Methods = methods });
                    if (cache.Methods.Count == 3) break;
                }
            }
        }
        _industryLedgerCache = cache;
        return cache;
    }

    private void DrawLedgerPanel()
    {
        bool commodities = G.Commodities.Enabled;
        bool noData = !commodities && (G.Statistics.Available.Count() == 0 || G.Statistics.Dates.Count < 2);
        var r = RightRect(2000);
        r.Width = Math.Max(r.Width, 620 * S);
        r.X = Raylib.GetScreenWidth() - r.Width - 8 * S;
        Gui.Panel(r, "Ledger");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        if (commodities)
        {
            float tabW = (w - 8 * S) / 3;
            if (Gui.Button(new Rectangle(x, y, tabW, 27 * S), "History", true, "Population, income and other recorded history", _ledgerTab == 0, 13)) _ledgerTab = 0;
            if (Gui.Button(new Rectangle(x + tabW + 4 * S, y, tabW, 27 * S), "Global Trade", true, "Every active commodity in the world market", _ledgerTab == 1, 13)) _ledgerTab = 1;
            if (Gui.Button(new Rectangle(x + 2 * (tabW + 4 * S), y, tabW, 27 * S), "Industry", true, "Your country's production and industrial inputs", _ledgerTab == 2, 13)) _ledgerTab = 2;
            y += 37 * S;
            if (_ledgerTab == 1) { DrawGlobalTradeLedger(x, y, w, r); return; }
            if (_ledgerTab == 2) { DrawIndustryLedger(x, y, w, r); return; }
        }
        var st = G.Statistics;
        var metrics = st.Available.ToList();
        if (metrics.Count == 0 || st.Dates.Count < 2) { Gui.Wrapped("Statistics are recorded every month. Come back after a few months.", x, y, w, 15, Gui.TextDim); return; }
        if (!metrics.Any(m => m.Key == _ledgerMetric)) _ledgerMetric = metrics[0].Key;
        float bx = x;
        foreach (var m in metrics)
        {
            float bw = Math.Max(80 * S, Gui.Measure(m.Name, 13).X + 16 * S);
            if (bx + bw > x + w) { bx = x; y += 30 * S; }
            if (Gui.Button(new Rectangle(bx, y, bw, 26 * S), m.Name, true, null, m.Key == _ledgerMetric, 13)) _ledgerMetric = m.Key;
            bx += bw + 4 * S;
        }
        y += 36 * S;
        // countries or the whole world
        if (Gui.Button(new Rectangle(x, y, 110 * S, 24 * S), "Countries", true, "The biggest countries and us", !_ledgerWorld, 13)) _ledgerWorld = false;
        if (Gui.Button(new Rectangle(x + 114 * S, y, 110 * S, 24 * S), "World", true, "The whole world (total, or average for levels like stability)", _ledgerWorld, 13)) _ledgerWorld = true;
        // which charts to show
        float tx = x + 240 * S;
        foreach (var (key, label, tip) in new[] { ("trend", "Trend", "Line chart over time"), ("rank", "Ranking", "Bars of the latest values"), ("share", "Share", "Each country's slice of the total"), ("growth", "Growth", "Change over the last year, in percent") })
        {
            bool on = _ledgerShow.Contains(key);
            float bw = Gui.Measure(label, 13).X + 26 * S;
            if (Gui.Button(new Rectangle(tx, y, bw, 24 * S), (on ? "✓ " : "") + label, true, tip + (on ? " (shown)" : " (hidden)"), on, 13))
            { if (on) _ledgerShow.Remove(key); else _ledgerShow.Add(key); }
            tx += bw + 4 * S;
        }
        y += 32 * S;
        var player = G.World.Player;
        var series = new List<(string Name, Rgb Color, List<double> Values, bool Mine)>();
        if (_ledgerWorld)
        {
            bool sum = Burger.Core.Mechanics.StatisticsSystem.Summable.Contains(_ledgerMetric);
            var total = st.WorldSeries(_ledgerMetric);
            if (!sum)
            {
                // average of the countries that have a value
                var counts = new int[st.Dates.Count];
                if (st.Values.TryGetValue(_ledgerMetric, out var pc))
                    foreach (var l in pc.Values) for (int i = 0; i < l.Count && i < counts.Length; i++) if (!double.IsNaN(l[i])) counts[i]++;
                for (int i = 0; i < total.Count; i++) if (counts[i] > 0) total[i] /= counts[i];
            }
            series.Add((sum ? "World" : "World average", new Rgb(230, 210, 140), total, true));
        }
        else
        {
            // the 8 biggest countries now, and the player
            var tags = G.World.Countries.Where(c => c.Exists)
                .Select(c => (c, v: st.Series(_ledgerMetric, c.Tag).LastOrDefault(v => !double.IsNaN(v))))
                .OrderByDescending(t => t.v).Take(8).Select(t => t.c).ToList();
            if (player != null && player.Exists && !tags.Contains(player)) tags.Add(player);
            foreach (var c in tags) series.Add((c.Name, c.Color, st.Series(_ledgerMetric, c.Tag), c == player));
        }
        double max = series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max();
        if (max <= 0) max = 1;
        int extras = (_ledgerShow.Contains("rank") ? 1 : 0) + (_ledgerShow.Contains("share") ? 1 : 0) + (_ledgerShow.Contains("growth") ? 1 : 0);
        bool trend = _ledgerShow.Contains("trend");
        float chartH = trend ? Math.Clamp(r.Y + r.Height - y - 190 * S - extras * 120 * S, 120 * S, 330 * S) : 0;
        var chart = new Rectangle(x + 46 * S, y, w - 50 * S, chartH);
        if (trend) DrawLedgerTrend(chart, st, series, max);
        // legend
        y = chart.Y + chart.Height + (trend ? 24 * S : 0);
        float colW = w / 3;
        int k = 0;
        foreach (var (name, colr, values, mine) in series)
        {
            float lx = x + (k % 3) * colW, lyy = y + (k / 3) * 22 * S;
            Raylib.DrawRectangleRec(new Rectangle(lx, lyy + 5 * S, 14 * S, 6 * S), C(colr));
            double v = values.LastOrDefault(t => !double.IsNaN(t));
            Gui.Label(Gui.Fit($"{name} {LedgerFmt(v)}", colW - 22 * S, 13), lx + 20 * S, lyy, 13, mine ? Gui.Gold : Gui.Text);
            k++;
        }
        y += (series.Count + 2) / 3 * 22 * S + 10 * S;
        double Last(List<double> v) => v.LastOrDefault(t => !double.IsNaN(t));
        if (_ledgerShow.Contains("rank"))
        {
            Gui.Section("Ranking", x, y, w, "ui_ranking"); y += 26 * S;
            double top = series.Select(q => Last(q.Values)).DefaultIfEmpty(1).Max(); if (top <= 0) top = 1;
            foreach (var q in series.OrderByDescending(q => Last(q.Values)).Take(8))
            {
                Gui.Label(Gui.Fit(q.Name, 130 * S, 13), x, y, 13, q.Mine ? Gui.Gold : Gui.Text);
                var bar = new Rectangle(x + 138 * S, y + 3 * S, (w - 220 * S) * (float)Math.Max(0, Last(q.Values) / top), 10 * S);
                Raylib.DrawRectangleRec(bar, C(q.Color));
                Gui.Label(LedgerFmt(Last(q.Values)), bar.X + bar.Width + 6 * S, y, 13, Gui.TextDim);
                y += 20 * S;
            }
            y += 8 * S;
        }
        if (_ledgerShow.Contains("share"))
        {
            Gui.Section("Share of the total", x, y, w, "ui_share"); y += 26 * S;
            double tot = series.Sum(q => Math.Max(0, Last(q.Values)));
            if (tot > 0 && !_ledgerWorld)
            {
                float sx = x;
                foreach (var q in series.OrderByDescending(q => Last(q.Values)))
                {
                    double v = Math.Max(0, Last(q.Values));
                    float sw = (float)(v / tot) * w;
                    var seg = new Rectangle(sx, y, sw, 22 * S);
                    Raylib.DrawRectangleRec(seg, C(q.Color));
                    if (Gui.Hover(seg)) Gui.Tooltip($"{q.Name}\n{v / tot:P1} ({LedgerFmt(v)})");
                    sx += sw;
                }
                y += 30 * S;
            }
            else { Gui.Label("Pick Countries to compare shares.", x, y, 13, Gui.TextDim); y += 24 * S; }
            y += 6 * S;
        }
        if (_ledgerShow.Contains("growth"))
        {
            Gui.Section("Change over the last year", x, y, w, "ui_change"); y += 26 * S;
            int n2 = st.Dates.Count;
            int back = 0;
            for (int j = n2 - 1; j >= 0; j--) { if (st.Dates[n2 - 1].TotalDays - st.Dates[j].TotalDays >= 360) { back = j; break; } }
            float gx = x, gw = w / 2;
            int gi = 0;
            foreach (var q in series)
            {
                double a0 = back < q.Values.Count ? q.Values[back] : double.NaN, a1 = Last(q.Values);
                if (double.IsNaN(a0) || a0 == 0) continue;
                double ch = a1 / a0 - 1;
                float cx2 = x + (gi % 2) * gw, cy2 = y + (gi / 2) * 20 * S;
                Gui.Label(Gui.Fit(q.Name, gw - 80 * S, 13), cx2, cy2, 13, q.Mine ? Gui.Gold : Gui.Text);
                string txt = $"{(ch >= 0 ? "+" : "")}{ch:P1}";
                Gui.Label(txt, cx2 + gw - 12 * S - Gui.Measure(txt, 13, FontKind.Bold).X, cy2, 13, DeltaColor(ch, 0.0005), FontKind.Bold);
                gi++;
            }
            y += (gi + 1) / 2 * 20 * S;
        }
    }

    private void DrawGlobalTradeLedger(float x, float y, float w, Rectangle panel)
    {
        var market = G.Commodities;
        var cache = GetGlobalTradeLedgerCache(w);
        if (cache == null)
        {
            Gui.Label("Updating world market…", x, y, 14, Gui.TextDim);
            return;
        }
        var countries = cache.Countries;
        var rows = cache.Rows;
        var subject = cache.Subject;
        Gui.Label(cache.TradeSummary, x, y, 14, Gui.TextDim);
        Gui.Label(cache.ShortageSummary, x + w - Gui.Measure(cache.ShortageSummary, 13).X, y + 1 * S, 13, cache.Shortages == 0 ? Gui.Good : Gui.Bad);
        y += 25 * S;
        if (subject != null)
        {
            var policy = G.TradeNetwork.PolicyOf(subject);
            Gui.Label(cache.PolicySummary, x, y, 13, Gui.TextDim);
            if (subject.IsPlayer)
            {
                float buttonY = y + 17 * S, buttonW = 58 * S;
                bool SetPolicy(double tariff, double priority, double protection)
                {
                    string t = tariff.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    string p = priority.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    string r = protection.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    if (Send(GameCommand.SetTradePolicy, t, p, r)) { InvalidateLedgerCache(); return true; }
                    policy.TariffRate = tariff; policy.ImportPriority = priority; policy.Protectionism = protection;
                    InvalidateLedgerCache();
                    return false;
                }
                if (Gui.Button(new Rectangle(x + w - 378 * S, buttonY, buttonW, 23 * S), "Tariff -", true, "Reduce import tariff by 1 percentage point", false, 11))
                {
                    double value = Math.Max(0, policy.TariffRate - 0.01);
                    SetPolicy(value, policy.ImportPriority, policy.Protectionism);
                }
                if (Gui.Button(new Rectangle(x + w - 315 * S, buttonY, buttonW, 23 * S), "Tariff +", true, "Increase import tariff by 1 percentage point", false, 11))
                {
                    double value = Math.Min(0.50, policy.TariffRate + 0.01);
                    SetPolicy(value, policy.ImportPriority, policy.Protectionism);
                }
                if (Gui.Button(new Rectangle(x + w - 252 * S, buttonY, buttonW, 23 * S), "Import -", true, "Lower the priority given to imports", false, 11))
                    SetPolicy(policy.TariffRate, Math.Max(0.1, policy.ImportPriority - 0.1), policy.Protectionism);
                if (Gui.Button(new Rectangle(x + w - 189 * S, buttonY, buttonW, 23 * S), "Import +", true, "Raise the priority given to imports", false, 11))
                    SetPolicy(policy.TariffRate, Math.Min(4, policy.ImportPriority + 0.1), policy.Protectionism);
                if (Gui.Button(new Rectangle(x + w - 126 * S, buttonY, buttonW, 23 * S), "Protect -", true, "Lower protectionism and import suppression", false, 11))
                    SetPolicy(policy.TariffRate, policy.ImportPriority, Math.Max(0, policy.Protectionism - 0.05));
                if (Gui.Button(new Rectangle(x + w - 63 * S, buttonY, buttonW, 23 * S), "Protect +", true, "Raise protectionism and import suppression", false, 11))
                    SetPolicy(policy.TariffRate, policy.ImportPriority, Math.Min(0.9, policy.Protectionism + 0.05));
                y += 22 * S;
            }
            y += 22 * S;
        }
        Gui.Label(cache.RouteSummary, x, y, 12, Gui.TextDim);
        if (cache.RecentRouteText.Length > 0)
        {
            Gui.Label(Gui.Fit(cache.RecentRouteText, w, 12), x, y + 16 * S, 12, Gui.Gold);
            y += 34 * S;
        }
        Gui.Section("World market balances", x, y, w, "ui_global_trade");
        y += 27 * S;

        if (_ledgerCommodity != null && market.Defs.TryGetValue(_ledgerCommodity, out var selectedDef))
        {
            Gui.Label($"Country drill-down · {selectedDef.Name}", x, y, 13, Gui.Gold, FontKind.Bold);
            y += 20 * S;
            foreach (var row in cache.Drilldown)
            {
                Gui.Flag(row.Country, new Rectangle(x, y, 26 * S, 17 * S));
                Gui.Label(row.Name, x + 32 * S, y, 12, row.Country == subject ? Gui.Gold : Gui.Text);
                Gui.Label($"{row.Trade} · {row.Met}", x + 165 * S, y, 12, row.Satisfied >= 0.85 ? Gui.TextDim : Gui.Bad);
                y += 19 * S;
            }
            y += 4 * S;
        }

        float nameW = w * 0.27f, outputW = w * 0.13f, demandW = w * 0.13f, tradeW = w * 0.15f, stockW = w * 0.12f;
        void Header(string text, float at, float width) => Gui.Label(Gui.Fit(text, width - 4 * S, 12), at, y, 12, Gui.TextDim, FontKind.Bold);
        Header("Commodity", x, nameW);
        Header("Output", x + nameW, outputW);
        Header("Demand", x + nameW + outputW, demandW);
        Header("Trade in / out", x + nameW + outputW + demandW, tradeW);
        Header("Stockpile", x + nameW + outputW + demandW + tradeW, stockW);
        Header("Price", x + nameW + outputW + demandW + tradeW + stockW, w * 0.08f);
        Header("Met", x + w - 52 * S, 52 * S);
        y += 20 * S;

        float rowH = 27 * S;
        float contentH = Math.Max(rowH, rows.Count * rowH);
        float top = Gui.BeginScroll("global-trade-ledger", new Rectangle(x, y, w, panel.Bottom() - y - 10 * S), contentH);
        float ry = top;
        foreach (var row in rows)
        {
            var rowRect = new Rectangle(x, ry, w - 8 * S, rowH - 2 * S);
            if (Gui.Hover(rowRect)) Raylib.DrawRectangleRec(rowRect, new Color(255, 255, 255, 14));
            if (Gui.Hover(rowRect) && Raylib.IsMouseButtonPressed(MouseButton.Left)) _ledgerCommodity = row.Def.Key;
            Raylib.DrawRectangleRec(new Rectangle(x, ry + 7 * S, 8 * S, 8 * S), C(row.Def.Color));
            Gui.Label(row.Name, x + 13 * S, ry + 3 * S, 13, Gui.Text);
            Gui.Label(row.Output, x + nameW, ry + 3 * S, 13, Gui.Text);
            Gui.Label(row.Demand, x + nameW + outputW, ry + 3 * S, 13, Gui.Text);
            Gui.Label(row.Trade, x + nameW + outputW + demandW, ry + 3 * S, 13, Gui.TextDim);
            Gui.Label(row.Stockpile, x + nameW + outputW + demandW + tradeW, ry + 3 * S, 13, Gui.TextDim);
            Gui.Label(row.Price, x + nameW + outputW + demandW + tradeW + stockW, ry + 3 * S, 13, row.AveragePrice > row.Def.Price * 1.5 ? Gui.Gold : Gui.Text);
            Gui.Label(row.Met, x + w - 52 * S, ry + 3 * S, 13, row.Satisfied >= 0.85 ? Gui.Good : Gui.Bad);
            if (Gui.Hover(rowRect)) Gui.Tooltip(row.Tooltip);
            ry += rowH;
        }
        Gui.EndScroll();
    }

    private void DrawIndustryLedger(float x, float y, float w, Rectangle panel)
    {
        var country = LedgerSubject;
        if (country == null)
        {
            Gui.Wrapped("Choose a country or start a game to inspect its industry ledger.", x, y, w, 15, Gui.TextDim);
            return;
        }

        var cache = GetIndustryLedgerCache(w);
        if (cache == null) return;
        var rows = cache.Rows;
        const float identityNameSize = 18;
        float flagW = 42 * S, flagH = 28 * S, nameX = x + flagW + 9 * S;
        float badgeW = Math.Min(w, nameX - x + Gui.Measure(cache.IdentityName, identityNameSize, FontKind.Bold).X + 14 * S);
        var badge = new Rectangle(x - 5 * S, y - 5 * S, badgeW, 38 * S);
        Raylib.DrawRectangleRec(badge, new Color(117, 82, 26, 230));
        Raylib.DrawRectangleLinesEx(badge, 1 * S, Gui.Gold);
        Gui.Flag(country, new Rectangle(x, y, flagW, flagH));
        Gui.LabelShadow(cache.IdentityName, nameX, y + 3 * S, identityNameSize, Gui.Text, FontKind.Bold);
        Gui.Label(cache.Summary, x, y + 32 * S, 13, Gui.TextDim);
        Gui.Label(cache.DemandSummary, x, y + 47 * S, 13, Gui.TextDim);
        if (cache.Shortages > 0) Gui.Label($"{cache.Shortages} inputs below 85%", x + w - Gui.Measure($"{cache.Shortages} inputs below 85%", 13).X, y + 47 * S, 13, Gui.Bad);
        Gui.Label(cache.BuildingSummary, x, y + 62 * S, 13, Gui.TextDim);
        Gui.Label(cache.PlanSummary, x, y + 77 * S, 12, cache.PlanSummary.Contains("hold", StringComparison.OrdinalIgnoreCase) ? Gui.Bad : Gui.Gold);
        if (country.IsPlayer)
        {
            if (cache.ShortageCommodity != null && Gui.Button(new Rectangle(x, y + 91 * S, 150 * S, 23 * S), cache.Supplier == null ? "Import unavailable" : $"Import {Gui.Fit(cache.ShortageCommodity, 82 * S, 11)}", cache.Supplier != null, cache.Supplier == null ? "No current surplus supplier" : $"Negotiate a 12-month agreement with {cache.Supplier.Name}", false, 11))
            {
                if (cache.Supplier != null && !Send(GameCommand.NegotiateTrade, cache.Supplier.Tag, "0.5", cache.ShortageCommodity, cache.ShortageAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))) G.TradeNetwork.Negotiate(country, cache.Supplier, 0.5, 12, cache.ShortageCommodity, cache.ShortageAmount);
                InvalidateLedgerCache();
            }
            if (Gui.Button(new Rectangle(x + 156 * S, y + 91 * S, 150 * S, 23 * S), cache.Build == null ? "Build unavailable" : $"Build {cache.Build.Name}", cache.CanBuild, "Spend treasury on the local capacity for the current shortage", false, 11))
            {
                if (cache.Build != null && cache.BuildState != null && !Send(GameCommand.BuildIndustry, cache.Build.Key, cache.BuildState.Key)) G.Population.GoldBuild(country, cache.Build, cache.BuildState);
                InvalidateLedgerCache();
            }
        }
        if (country.IsPlayer)
        {
            if (cache.MethodState != null && cache.Methods.Count > 0)
            {
                Gui.Label($"Methods in {cache.MethodState.DisplayName}", x, y + 118 * S, 12, Gui.TextDim, FontKind.Bold);
                float mx = x;
                foreach (var method in cache.Methods)
                {
                    var chosen = G.TradeNetwork.SelectedMethods.GetValueOrDefault((method.State, method.Output)) ?? method.Methods[0].Key;
                    var current = method.Methods.FirstOrDefault(recipe => recipe.Key.Equals(chosen, StringComparison.OrdinalIgnoreCase)) ?? method.Methods[0];
                    string label = Gui.Fit($"{method.Output}: {current.Method}", 145 * S, 10);
                    if (Gui.Button(new Rectangle(mx, y + 135 * S, 145 * S, 21 * S), label, true, $"Select production method for {method.Output}", false, 10))
                    {
                        int next = method.Methods.IndexOf(current);
                        next = (next + 1) % method.Methods.Count;
                        if (!Send(GameCommand.SetIndustryMethod, method.State.Key, method.Output, method.Methods[next].Key)) G.TradeNetwork.SetMethod(country, method.State, method.Output, method.Methods[next].Key);
                        InvalidateLedgerCache();
                    }
                    mx += 151 * S;
                }
                y += 64 * S;
            }
        }
        y += country.IsPlayer ? 122 * S : 95 * S;
        DrawConstructionCapacityLedger(cache, x, ref y, w);
        y += 8 * S;
        Gui.Section("Production and inputs", x, y, w, "ui_industry");
        y += 27 * S;

        float nameW = w * 0.29f, outputW = w * 0.13f, popW = w * 0.14f, inputW = w * 0.17f, stockW = w * 0.12f;
        void Header(string text, float at, float width) => Gui.Label(Gui.Fit(text, width - 4 * S, 12), at, y, 12, Gui.TextDim, FontKind.Bold);
        Header("Commodity", x, nameW);
        Header("Output", x + nameW, outputW);
        Header("People", x + nameW + outputW, popW);
        Header("Industry / army", x + nameW + outputW + popW, inputW);
        Header("Stockpile", x + nameW + outputW + popW + inputW, stockW);
        Header("Met", x + w - 52 * S, 52 * S);
        y += 20 * S;

        float rowH = 27 * S;
        float contentH = Math.Max(rowH, rows.Count * rowH);
        const float noteH = 62;
        float noteY = panel.Bottom() - noteH * S + 6 * S;
        float top = Gui.BeginScroll("industry-ledger", new Rectangle(x, y, w, noteY - y - 8 * S), contentH);
        float ry = top;
        foreach (var row in rows)
        {
            var rowRect = new Rectangle(x, ry, w - 8 * S, rowH - 2 * S);
            if (Gui.Hover(rowRect)) Raylib.DrawRectangleRec(rowRect, new Color(255, 255, 255, 14));
            Raylib.DrawRectangleRec(new Rectangle(x, ry + 7 * S, 8 * S, 8 * S), C(row.Def.Color));
            Gui.Label(row.Name, x + 13 * S, ry + 3 * S, 13, Gui.Text);
            Gui.Label(row.OutputText, x + nameW, ry + 3 * S, 13, Gui.Text);
            Gui.Label(row.PopulationText, x + nameW + outputW, ry + 3 * S, 13, Gui.TextDim);
            Gui.Label(row.IndustryText, x + nameW + outputW + popW, ry + 3 * S, 13, row.IndustryDemand > 0 ? Gui.Text : Gui.TextDim);
            Gui.Label(row.StockpileText, x + nameW + outputW + popW + inputW, ry + 3 * S, 13, Gui.TextDim);
            Gui.Label(row.Met, x + w - 52 * S, ry + 3 * S, 13, row.Demand <= 0.0001 ? Gui.TextDim : row.Satisfied >= 0.85 ? Gui.Good : Gui.Bad);
            if (Gui.Hover(rowRect)) Gui.Tooltip(row.Tooltip);
            ry += rowH;
        }
        Gui.EndScroll();
        Gui.Wrapped("Industry demand combines population, AI-built capacity and military use. Farms, workshops, markets and infrastructure now change local output; workshop input shortages reduce processed goods.", x, noteY, w, 12, Gui.TextDim);
    }

    private void DrawConstructionCapacityLedger(IndustryLedgerCache cache, float x, ref float y, float w)
    {
        Gui.Section("Construction capacity", x, y, w, "ui_construction");
        y += 27 * S;
        Gui.Label(cache.ConstructionSummary, x, y, 13, cache.Construction.Count == 0 ? Gui.TextDim : Gui.Gold);
        y += 20 * S;
        if (cache.Construction.Count == 0)
        {
            Gui.Wrapped("No projects are queued. Preindustrial construction uses manual labour, public works and workshops; civilian factories are only one later method.", x, y, w, 12, Gui.TextDim);
            y += 30 * S;
            return;
        }

        int visible = Math.Min(6, cache.Construction.Count);
        float rowH = 38 * S;
        for (int i = 0; i < visible; i++)
        {
            var row = cache.Construction[i];
            var box = new Rectangle(x, y, w - 8 * S, rowH - 2 * S);
            Card(box, new Color(36, 38, 44, 255));
            string target = Gui.Fit(row.Target, w * 0.46f, 12, FontKind.Bold);
            string method = Gui.Fit(row.Method, w * 0.24f, 12, FontKind.Bold);
            Gui.Label(target, box.X + 7 * S, box.Y + 3 * S, 12, Gui.Text, FontKind.Bold);
            Gui.Label(method, box.X + w * 0.48f, box.Y + 3 * S, 12, Gui.Gold, FontKind.Bold);
            float barW = w * 0.25f;
            Gui.Bar(new Rectangle(box.X + 7 * S, box.Y + 21 * S, barW, 7 * S), (float)row.ProgressRatio, row.Blocked ? Gui.Bad : Gui.Good);
            Gui.Label(row.Progress, box.X + 7 * S + barW + 6 * S, box.Y + 17 * S, 11, Gui.TextDim);
            string detail = $"{row.Allocation} / {row.Capacity}";
            Gui.Label(Gui.Fit(detail, w * 0.34f, 11), box.X + w * 0.53f, box.Y + 17 * S, 11, Gui.TextDim);
            string status = row.Blocked ? $"Blocked: {row.Status}" : row.Status;
            Gui.Label(Gui.Fit(status, box.Width - 14 * S, 10), box.X + 7 * S, box.Y + 29 * S, 10, row.Blocked ? Gui.Bad : Gui.Good);
            if (Gui.Hover(box)) Gui.Tooltip($"{row.Target}\nMethod: {row.Method}\nProgress: {row.Progress}\n{row.Allocation}\n{row.Capacity}\n{status}");
            y += rowH;
        }
        if (cache.Construction.Count > visible)
        {
            Gui.Label($"+ {cache.Construction.Count - visible} more queued projects", x, y + 2 * S, 11, Gui.TextDim);
            y += 18 * S;
        }
    }

    private readonly HashSet<string> _ledgerShow = new() { "trend", "rank" };

    private void DrawLedgerTrend(Rectangle chart, Burger.Core.Mechanics.StatisticsSystem st, List<(string Name, Rgb Color, List<double> Values, bool Mine)> series, double max)
    {
        Raylib.DrawRectangleRec(chart, new Color(0, 0, 0, 70));
        for (int i = 0; i <= 4; i++)
        {
            float gy = chart.Y + chart.Height * (1 - i / 4f);
            Raylib.DrawLineEx(new Vector2(chart.X, gy), new Vector2(chart.X + chart.Width, gy), 1, new Color(255, 255, 255, 25));
            string lbl = LedgerFmt(max * i / 4);
            Gui.Label(lbl, chart.X - Gui.Measure(lbl, 11).X - 4 * S, gy - 7 * S, 11, Gui.TextDim);
        }
        int n = st.Dates.Count;
        Gui.Label(st.Dates[0].ToDisplay(), chart.X, chart.Y + chart.Height + 4 * S, 11, Gui.TextDim);
        string last = st.Dates[^1].ToDisplay();
        Gui.Label(last, chart.X + chart.Width - Gui.Measure(last, 11).X, chart.Y + chart.Height + 4 * S, 11, Gui.TextDim);
        // x follows the date, so thinned old samples keep their place in time
        int d0 = st.Dates[0].TotalDays, span = Math.Max(1, st.Dates[^1].TotalDays - d0);
        Vector2 Pt(int i, double v) => new(chart.X + chart.Width * (st.Dates[i].TotalDays - d0) / (float)span, chart.Y + chart.Height * (float)(1 - v / max));
        foreach (var (_, colr, values, mine) in series)
        {
            var col = C(colr);
            float thick = mine ? 3.2f * S : 1.8f * S;
            for (int i = 1; i < n && i < values.Count; i++)
                if (!double.IsNaN(values[i - 1]) && !double.IsNaN(values[i])) Raylib.DrawLineEx(Pt(i - 1, values[i - 1]), Pt(i, values[i]), thick, col);
        }
        // hover: values at that month
        if (Gui.Hover(chart))
        {
            int day = d0 + (int)Math.Round((Viewport.Mouse.X - chart.X) / chart.Width * span);
            int i = 0;
            for (int j = 1; j < n; j++) if (Math.Abs(st.Dates[j].TotalDays - day) < Math.Abs(st.Dates[i].TotalDays - day)) i = j;
            float hx = Pt(i, 0).X;
            Raylib.DrawLineEx(new Vector2(hx, chart.Y), new Vector2(hx, chart.Y + chart.Height), 1, new Color(255, 255, 255, 90));
            Gui.Tooltip(st.Dates[i].ToDisplay() + "\n" + string.Join("\n", series.Select(s => (s.Name, v: i < s.Values.Count ? s.Values[i] : double.NaN))
                .Where(t => !double.IsNaN(t.v)).OrderByDescending(t => t.v).Select(t => $"{t.Name}: {LedgerFmt(t.v)}")));
        }
    }

    private string LedgerFmt(double v) => _ledgerMetric is "population" or "manpower" or "war_dead" ? People(v) : v >= 1000 ? People(v) : v.ToString(v < 10 ? "0.#" : "0");

    /// <summary>Routes a settlement action through the lockstep session when there is one.</summary>
    private void SettleCommand(string command, State st, Func<string?> local)
    {
        if (Mp != null) { Mp.Submit(command, st.Key); return; }
        var err = local();
        if (err != null) Ui.SetStatus(err);
    }

    private string NativeLine(State st) => G.Population.NativePeoplesText(st).Trim();

    /// <summary>The people who live on land being settled, largest first: the visible side of the natives (no country needed).</summary>
    private float DrawNativePeoples(State st, float x, float y, float w)
    {
        var peoples = G.Population.NativePeoples(st);
        if (peoples.Count == 0) return y;
        Gui.Label("Native peoples", x, y, 14, Gui.Gold);
        y += 20 * S;
        foreach (var (culture, people, share) in peoples.Take(4))
        {
            Gui.Label(Gui.Fit(culture, w * 0.5f, 13), x, y, 13);
            Gui.Bar(new Rectangle(x + w * 0.52f, y + 3 * S, w * 0.28f, 9 * S), (float)share, new Color(190, 120, 90, 255));
            Gui.Label($"{people:N0}", x + w * 0.82f, y, 13, Gui.TextDim);
            y += 18 * S;
        }
        if (G.Population.NativeShare(st, G.World.Player!) > 0.05)
            Gui.Label($"They may attack settlers ({G.Rules.Population.NativeAttackChance(G, st, G.World.Player!, G.Population.NativeShare(st, G.World.Player!)):P0} a month).", x, y, 13, Gui.TextDim);
        return y + 20 * S;
    }
}
