using System.Globalization;
using Burger.Core.Model;

namespace Burger.Core.Multiplayer;

/// <summary>The desync check: a hash of the parts of the state every machine must agree on. Never iterates an unordered collection.</summary>
public static class StateChecksum
{
    /// <summary>Human-readable differences between two games at the same tick (tests and debugging; not sent over the wire).</summary>
    public static List<string> Diff(Game a, Game b)
    {
        var o = new List<string>();
        if (a.World.Date.TotalDays != b.World.Date.TotalDays || a.World.Date.Hour != b.World.Date.Hour) o.Add($"date {a.World.Date.ToDisplay()} {a.World.Date.Hour} vs {b.World.Date.ToDisplay()} {b.World.Date.Hour}");
        foreach (var c in a.World.Countries)
        {
            var x = b.World.GetCountry(c.Tag);
            if (x == null || c.Gold != x.Gold || c.Manpower != x.Manpower || c.IsPlayer != x.IsPlayer || c.Exists != x.Exists)
                o.Add($"{c.Tag}: gold {c.Gold} vs {x?.Gold}, manpower {c.Manpower} vs {x?.Manpower}, player {c.IsPlayer}/{x?.IsPlayer}");
        }
        if (a.World.Divisions.Count != b.World.Divisions.Count) o.Add($"divisions {a.World.Divisions.Count} vs {b.World.Divisions.Count}");
        var bd = b.World.Divisions.ToDictionary(d => d.Id);
        foreach (var d in a.World.Divisions)
            if (!bd.TryGetValue(d.Id, out var y) || y.Location.Key != d.Location.Key || y.Strength != d.Strength) o.Add($"division {d.Id} ({d.Owner.Tag}): {d.Location.Key}/{d.Strength} vs {(y == null ? "missing" : y.Location.Key + "/" + y.Strength)}");
        var rb = b.Rngs.Snapshot();
        foreach (var r in a.Rngs.Snapshot()) if (!rb.Contains(r)) o.Add("rng " + r.Name);
        if (a.Events.PlayerQueue.Count != b.Events.PlayerQueue.Count) o.Add($"event queue {a.Events.PlayerQueue.Count} vs {b.Events.PlayerQueue.Count}");
        if (a.PendingProposals.Count != b.PendingProposals.Count) o.Add($"proposals {a.PendingProposals.Count} vs {b.PendingProposals.Count}");
        int ah = Math.Min(50, a.TradeNetwork.History.Count), bh = Math.Min(50, b.TradeNetwork.History.Count);
        if (ah != bh || a.TradeNetwork.Agreements.Count != b.TradeNetwork.Agreements.Count) o.Add($"trade history/agreements {ah}/{a.TradeNetwork.Agreements.Count} vs {bh}/{b.TradeNetwork.Agreements.Count}");
        foreach (var c in a.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            var ap = a.TradeNetwork.PolicyOf(c); var bc = b.World.GetCountry(c.Tag);
            if (bc == null) continue;
            if (a.Politics.Enabled)
            {
                var pa = a.Politics.Of(c); var pb = b.Politics.Of(bc);
                if (pa.Ruling != pb.Ruling) o.Add($"politics {c.Tag} ruling {pa.Ruling} vs {pb.Ruling}");
                else
                    foreach (var key in pa.Popularity.Keys.Union(pb.Popularity.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
                        if (pa.Popularity.GetValueOrDefault(key) != pb.Popularity.GetValueOrDefault(key)) { o.Add($"politics {c.Tag}/{key} {pa.Popularity.GetValueOrDefault(key):R} vs {pb.Popularity.GetValueOrDefault(key):R}"); break; }
            }
            var bp = b.TradeNetwork.PolicyOf(bc);
            if (ap.TariffRate != bp.TariffRate || ap.CustomsRevenue != bp.CustomsRevenue) o.Add($"trade policy {c.Tag}");
            foreach (var def in a.Commodities.Defs.Values.OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                var x = a.Commodities.BalanceOf(c, def.Key); var y = b.Commodities.BalanceOf(bc, def.Key);
                if (x.Stockpile != y.Stockpile || x.Satisfied != y.Satisfied || x.Import != y.Import || x.Export != y.Export) { o.Add($"commodity {c.Tag}/{def.Key}"); break; }
            }
        }
        foreach (var c in a.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            var x = b.World.GetCountry(c.Tag);
            var left = OrderedConstruction(c);
            var right = x == null ? new List<ConstructionProject>() : OrderedConstruction(x);
            if (left.Count != right.Count)
            {
                o.Add($"construction {c.Tag}: {left.Count} projects vs {right.Count}");
                continue;
            }
            for (int i = 0; i < left.Count; i++)
                if (!SameConstruction(left[i], right[i]))
                {
                    o.Add($"construction {c.Tag}: {DescribeConstruction(left[i])} vs {DescribeConstruction(right[i])}");
                    break;
                }
        }
        return o;
    }

    public static ulong Compute(Game game)
    {
        ulong h = 14695981039346656037UL;
        void U(ulong v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } }
        void S(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 124; h *= 1099511628211UL; }
        void D(double d) => U((ulong)BitConverter.DoubleToInt64Bits(d));
        var w = game.World;
        U((ulong)w.Date.TotalDays); U((ulong)w.Date.Hour);
        foreach (var c in w.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            S(c.Tag); U(c.Exists ? 1UL : 0UL); U(c.IsPlayer ? 1UL : 0UL); D(c.Gold); D(c.Manpower);
            foreach (var (key, value) in c.StatFactors.OrderBy(x => x.Key, StringComparer.Ordinal)) { S(key); D(value); }
        }
        foreach (var d in w.Divisions.OrderBy(d => d.Id))
        {
            U((ulong)d.Id); S(d.Owner.Tag); S(d.Location.Key); D(d.Strength);
        }
        U((ulong)game.Development.Checksum());
        U((ulong)game.CultureLife.Checksum()); U((ulong)game.Reforms.Checksum()); U((ulong)game.Succession.Checksum()); U((ulong)game.Dynasties.Checksum());
        foreach (var c in w.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            S(c.Tag);
            if (game.Politics.Enabled)
            {
                var politics = game.Politics.Of(c);
                S(politics.Ruling); U((ulong)(long)politics.RegimeChangedDay);
                foreach (var kv in politics.Popularity.OrderBy(k => k.Key, StringComparer.Ordinal)) { S(kv.Key); D(kv.Value); }
                foreach (var kv in politics.PartyPopularity.OrderBy(k => k.Key, StringComparer.Ordinal)) { S(kv.Key); D(kv.Value); }
                foreach (var kv in politics.Laws.OrderBy(k => k.Key, StringComparer.Ordinal)) { S(kv.Key); S(kv.Value); }
            }
            var policy = game.TradeNetwork.PolicyOf(c);
            D(policy.TariffRate); D(policy.CustomsRevenue); U((ulong)policy.AgreementsSigned);
            if (game.Commodities.Books.TryGetValue(c, out var books))
                foreach (var (key, b) in books.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) { S(key); D(b.Stockpile); D(b.Satisfied); D(b.Import); D(b.Export); }
            var construction = OrderedConstruction(c);
            S("construction"); U((ulong)construction.Count);
            foreach (var p in construction)
            {
                S(p.State.Key); S(p.Building.Key); D(p.Progress); S(p.Method ?? ""); D(p.Capacity); D(p.LabourAllocated); U((ulong)(long)p.Factories);
                S(p.BlockedReason ?? "");
                U((ulong)p.InputsConsumed.Count);
                foreach (var input in OrderedInputs(p)) { S(input.Key); D(input.Value); }
            }
        }
        foreach (var agreement in game.TradeNetwork.Agreements.OrderBy(a => a.From.Tag, StringComparer.Ordinal).ThenBy(a => a.To.Tag, StringComparer.Ordinal).ThenBy(a => a.Commodity, StringComparer.Ordinal))
        { S(agreement.From.Tag); S(agreement.To.Tag); S(agreement.Commodity); D(agreement.MonthlyQuantity); D(agreement.TariffDiscount); D(agreement.TransportDiscount); U((ulong)agreement.ExpiresDay); }
        foreach (var blockade in game.TradeNetwork.Blockades.OrderBy(k => k, StringComparer.Ordinal)) S(blockade);
        foreach (var ((state, output), recipe) in game.TradeNetwork.SelectedMethods.OrderBy(x => x.Key.State.Index).ThenBy(x => x.Key.Output, StringComparer.Ordinal))
        { S(state.Key); S(output); S(recipe); }
        foreach (var plan in game.TradeNetwork.Plans.OrderBy(x => x.Key.Tag, StringComparer.Ordinal).Select(x => x.Value))
        { S(plan.Country.Tag); S(plan.LastDecision ?? ""); S(plan.Priority ?? ""); S(plan.TargetCommodity ?? ""); D(plan.Budget); D(plan.ImportScore); D(plan.BuildScore); U((ulong)plan.ImportsOrdered); U((ulong)plan.BuildsOrdered); U((ulong)plan.LastReviewDay); }
        // Match the recent transaction window written into multiplayer saves; older audit history is intentionally not part of lockstep state.
        foreach (var transaction in game.TradeNetwork.History.TakeLast(50))
        { S(transaction.Commodity); S(transaction.Seller.Tag); S(transaction.Buyer.Tag); S(transaction.From.Key); S(transaction.To.Key); S(transaction.RouteKind.ToString()); D(transaction.Quantity); D(transaction.Tariff); D(transaction.TransportCost); }
        foreach (var (name, st) in game.Rngs.Snapshot()) { S(name); U(st); }
        return h;
    }

    private static List<ConstructionProject> OrderedConstruction(Country country) => country.Construction
        .OrderBy(p => p.State.Key, StringComparer.Ordinal)
        .ThenBy(p => p.Building.Key, StringComparer.Ordinal)
        .ThenBy(p => p.Progress)
        .ThenBy(p => p.Method ?? "", StringComparer.Ordinal)
        .ThenBy(p => p.Capacity)
        .ThenBy(p => p.LabourAllocated)
        .ThenBy(p => p.Factories)
        .ThenBy(p => p.BlockedReason ?? "", StringComparer.Ordinal)
        .ThenBy(ConstructionInputsKey, StringComparer.Ordinal)
        .ToList();

    private static IEnumerable<KeyValuePair<string, double>> OrderedInputs(ConstructionProject project) => project.InputsConsumed
        .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Key, StringComparer.Ordinal);

    private static string ConstructionInputsKey(ConstructionProject project) => string.Join(
        '\u001f', OrderedInputs(project).Select(x => x.Key + '\u001e' + x.Value.ToString("R", CultureInfo.InvariantCulture)));

    private static bool SameConstruction(ConstructionProject a, ConstructionProject b)
    {
        if (!string.Equals(a.State.Key, b.State.Key, StringComparison.Ordinal) ||
            !string.Equals(a.Building.Key, b.Building.Key, StringComparison.Ordinal) ||
            a.Progress != b.Progress || !string.Equals(a.Method ?? "", b.Method ?? "", StringComparison.Ordinal) ||
            a.Capacity != b.Capacity || a.LabourAllocated != b.LabourAllocated || a.Factories != b.Factories ||
            !string.Equals(a.BlockedReason ?? "", b.BlockedReason ?? "", StringComparison.Ordinal)) return false;
        var ai = OrderedInputs(a).ToList();
        var bi = OrderedInputs(b).ToList();
        if (ai.Count != bi.Count) return false;
        for (int i = 0; i < ai.Count; i++)
            if (!string.Equals(ai[i].Key, bi[i].Key, StringComparison.Ordinal) || ai[i].Value != bi[i].Value) return false;
        return true;
    }

    private static string DescribeConstruction(ConstructionProject p) =>
        $"state={p.State.Key}, building={p.Building.Key}, progress={p.Progress.ToString("R", CultureInfo.InvariantCulture)}, method={p.Method ?? ""}, capacity={p.Capacity.ToString("R", CultureInfo.InvariantCulture)}, labour={p.LabourAllocated.ToString("R", CultureInfo.InvariantCulture)}, factories={p.Factories}, blocked={p.BlockedReason ?? ""}, inputs=[{ConstructionInputsKey(p)}]";
}
