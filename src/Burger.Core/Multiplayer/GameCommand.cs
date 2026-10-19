using System.Globalization;
using Burger.Core.Diplomacy;
using Burger.Core.Model;

namespace Burger.Core.Multiplayer;

/// <summary>
/// One player action. Only commands travel between machines; every machine applies the same ones at the same tick.
/// <see cref="Tick"/> is the number of ticks the simulation has executed when the command is applied (it runs just before tick number Tick).
/// </summary>
public sealed record GameCommand(long Tick, string Country, string Type, string[] Args)
{
    public const string Move = "move";
    public const string DeclareWar = "declare_war";
    public const string AcceptPeace = "accept_peace";
    public const string RefusePeace = "refuse_peace";
    public const string WhitePeace = "white_peace";
    /// <summary>Answers any other proposal (args: kind, sender tag, 1 accept or 0 decline).</summary>
    public const string Respond = "respond";
    /// <summary>A custom message of a mod (args: message key, then the typed fields of its schema).</summary>
    public const string ModMessage = "mod_message";
    public const string QueueProduction = "queue_production";
    public const string SetTradePolicy = "set_trade_policy";
    public const string NegotiateTrade = "negotiate_trade";
    public const string SetTradeBlockade = "set_trade_blockade";
    public const string BuildIndustry = "build_industry";
    public const string SetIndustryMethod = "set_industry_method";
    /// <summary>Spends points on a state's development (args: state key).</summary>
    public const string Develop = "develop";
    /// <summary>Adopts (1) or drops (0) a government reform (args: reform key, 1 or 0).</summary>
    public const string Reform = "reform";
    /// <summary>Answers an event (args: event id, option index).</summary>
    public const string EventOption = "event_option";
    /// <summary>System command: a human takes this country (args: player id). The AI stops playing it.</summary>
    public const string Claim = "claim";
    /// <summary>System command: a human leaves this country; the AI takes it over.</summary>
    public const string Release = "release";

    public static bool IsPlayerCommand(string type) => type is Move or DeclareWar or AcceptPeace or RefusePeace or WhitePeace or QueueProduction or SetTradePolicy or NegotiateTrade or SetTradeBlockade or BuildIndustry or SetIndustryMethod or Develop or Reform or EventOption or Respond or ModMessage;

    public string Encode() => string.Join('|', Tick.ToString(CultureInfo.InvariantCulture), Net.Esc(Country), Net.Esc(Type), string.Join(',', Args.Select(Net.Esc)));

    public static GameCommand? Decode(string[] f, int at)
    {
        if (f.Length < at + 4 || !long.TryParse(f[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out long tick)) return null;
        var args = f[at + 3].Length == 0 ? Array.Empty<string>() : f[at + 3].Split(',').Select(Net.Unesc).ToArray();
        return new GameCommand(tick, Net.Unesc(f[at + 1]), Net.Unesc(f[at + 2]), args);
    }
}

/// <summary>Applies commands to a game. Every check is a function of sim state, so all machines reach the same verdict.</summary>
public static class CommandExecutor
{
    /// <summary>Applies the command. Returns false (and changes nothing) when it is not valid right now.</summary>
    public static bool Apply(Game g, GameCommand c)
    {
        var w = g.World;
        var country = w.GetCountry(c.Country);
        if (country == null || !country.Exists) return false;
        string A(int i) => i < c.Args.Length ? c.Args[i] : "";
        switch (c.Type)
        {
            case GameCommand.Claim: country.IsPlayer = true; return true;
            case GameCommand.Release: country.IsPlayer = false; return true;
            case GameCommand.Move:
            {
                if (!int.TryParse(A(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return false;
                var div = w.Divisions.FirstOrDefault(d => d.Id == id);
                var to = w.GetLocation(A(1));
                if (div == null || to == null || div.Owner != country) return false;
                if (div.Army != null && div.Army.FrontEnemy != null) div.FrontPost = null;
                return A(2) == "q" ? g.Warfare.QueueMove(div, to) : g.Warfare.OrderMove(div, to);
            }
            case GameCommand.DeclareWar:
            {
                var target = w.GetCountry(A(0));
                if (target == null || target == country) return false;
                return g.Diplomacy.DeclareWar(country, target) != null;
            }
            case GameCommand.AcceptPeace:
            case GameCommand.RefusePeace:
            {
                var p = g.PendingProposals.FirstOrDefault(x => x.Kind == ProposalKind.Peace && x.To == country && x.From.Tag == A(0));
                if (p == null) return false;
                g.Diplomacy.Respond(p, c.Type == GameCommand.AcceptPeace);
                return true;
            }
            case GameCommand.Respond:
            {
                if (!Enum.TryParse<ProposalKind>(A(0), out var kind)) return false;
                var p = g.PendingProposals.FirstOrDefault(x => x.Kind == kind && x.To == country && x.From.Tag == A(1));
                if (p == null) return false;
                g.Diplomacy.Respond(p, A(2) == "1");
                return true;
            }
            case GameCommand.WhitePeace:
            {
                var enemy = w.GetCountry(A(0));
                var war = enemy == null ? null : w.WarBetween(country, enemy);
                if (war == null) return false;
                var offer = new PeaceOffer { War = war, From = country, To = enemy!, FromIsWinner = false };
                return g.Diplomacy.SendPeaceOffer(offer) != null;
            }
            case GameCommand.EventOption:
            {
                var pe = g.Events.PlayerQueue.FirstOrDefault(x => x.Def.Id == A(0) && Scripting.ScopeHelpers.CountryOf(x.Context.Root) == country);
                if (pe == null || !int.TryParse(A(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int opt)) return false;
                g.Events.ChooseOption(pe, Math.Clamp(opt, 0, Math.Max(0, pe.Def.Options.Count - 1)));
                return true;
            }
            case GameCommand.ModMessage: return g.ModNet.Apply(country, c.Args);
            case GameCommand.Develop:
                return g.World.States.FirstOrDefault(s => s.Key == A(0)) is { } ds && g.Development.Develop(country, ds) == null;
            case GameCommand.Reform:
                return g.Reforms.Reforms.TryGetValue(A(0), out var rf) && (A(1) == "0" ? g.Reforms.Drop(country, rf) : g.Reforms.Adopt(country, rf)) == null;
            case GameCommand.QueueProduction:
            {
                if (!g.Data.Equipment.TryGetValue(A(0), out var eq)) return false;
                int factories = int.TryParse(A(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, 50) : 1;
                g.Production.AddLine(country, eq, factories);
                return true;
            }
            case GameCommand.SetTradePolicy:
            {
                if (c.Args.Length is < 1 or > 3) return false;
                if (!double.TryParse(A(0), NumberStyles.Float, CultureInfo.InvariantCulture, out var tariff) || tariff is < 0 or > 0.50) return false;
                var policy = g.TradeNetwork.PolicyOf(country);
                policy.TariffRate = tariff;
                if (c.Args.Length > 1 && double.TryParse(A(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var priority)) policy.ImportPriority = Math.Clamp(priority, 0.1, 4);
                if (c.Args.Length > 2 && double.TryParse(A(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var protection)) policy.Protectionism = Math.Clamp(protection, 0, 0.9);
                return true;
            }
            case GameCommand.NegotiateTrade:
            {
                var target = w.GetCountry(A(0));
                double discount = double.TryParse(A(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.5;
                double quantity = double.TryParse(A(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) ? Math.Max(0, q) : 0;
                return target != null && g.TradeNetwork.Negotiate(country, target, discount, 12, A(2), quantity);
            }
            case GameCommand.SetTradeBlockade:
            {
                var target = w.GetCountry(A(0));
                if (target == null || target == country || !w.AtWar(country, target) || (A(1) != "0" && A(1) != "1")) return false;
                g.TradeNetwork.SetBlockade(country, target, A(1) == "1");
                return true;
            }
            case GameCommand.BuildIndustry:
            {
                var building = g.Data.BuildingTypes.GetValueOrDefault(A(0));
                var state = w.GetState(A(1));
                if (c.Args.Length != 2 || building == null || state == null) return false;
                if (g.Production.Enabled)
                    return g.Production.QueueBuilding(country, building, state) == null;
                return g.Population.GoldBuild(country, building, state) == null;
            }
            case GameCommand.SetIndustryMethod:
            {
                var state = w.GetState(A(0));
                return state != null && g.TradeNetwork.SetMethod(country, state, A(1), A(2));
            }
        }
        return false;
    }
}

/// <summary>
/// The host's check of a player's request before it is stamped and broadcast: shape, ranges, ownership, and that it can happen right now.
/// A refused request changes nothing and reaches nobody. (Every machine checks again when it applies the command.)
/// </summary>
public static class CommandValidator
{
    /// <summary>Returns null when the command is acceptable for <paramref name="tag"/>, otherwise the reason.</summary>
    public static string? Check(Game g, string tag, string type, string[] args)
    {
        var w = g.World;
        var country = Sanitize.Tag(tag).Length == 0 ? null : w.GetCountry(tag);
        if (country == null || !country.Exists) return "you have no country";
        if (!GameCommand.IsPlayerCommand(type)) return "not a player command";
        if (!Wire.ArgsOk(args)) return "bad arguments";
        string A(int i) => i < args.Length ? args[i] : "";
        switch (type)
        {
            case GameCommand.Move:
            {
                if (args.Length is < 2 or > 3 || !Sanitize.Int(A(0), 0, int.MaxValue, out long id)) return "bad move";
                if (A(2) != "" && A(2) != "q") return "bad move";
                var div = w.Divisions.FirstOrDefault(d => d.Id == id);
                if (div == null || div.Owner != country) return "not your division";
                return w.GetLocation(A(1)) == null ? "unknown location" : null;
            }
            case GameCommand.DeclareWar:
            {
                if (args.Length != 1) return "bad declaration";
                var t = w.GetCountry(A(0));
                if (t == null || !t.Exists || t == country) return "no such country";
                return w.WarBetween(country, t) != null ? "already at war" : null;
            }
            case GameCommand.AcceptPeace:
            case GameCommand.RefusePeace:
                return args.Length == 1 && g.PendingProposals.Any(x => x.Kind == ProposalKind.Peace && x.To == country && x.From.Tag == A(0)) ? null : "no such proposal";
            case GameCommand.WhitePeace:
            {
                if (args.Length != 1) return "bad offer";
                var enemy = w.GetCountry(A(0));
                return enemy != null && w.WarBetween(country, enemy) != null ? null : "not at war with that country";
            }
            case GameCommand.Develop:
                return args.Length == 1 && g.World.States.FirstOrDefault(s => s.Key == A(0)) is { } vs ? g.Development.CannotDevelop(country, vs) : "no such state";
            case GameCommand.Reform:
                if (args.Length != 2 || (A(1) != "0" && A(1) != "1") || !g.Reforms.Reforms.TryGetValue(A(0), out var vr)) return "no such reform";
                return A(1) == "0" ? g.Reforms.CannotDrop(country, vr) : g.Reforms.CannotAdopt(country, vr);
            case GameCommand.QueueProduction:
                if (args.Length != 2 || !g.Data.Equipment.ContainsKey(A(0)) || !Sanitize.Int(A(1), 0, 50, out _)) return "bad production order";
                return null;
            case GameCommand.SetTradePolicy:
                if (args.Length is < 1 or > 3 || !double.TryParse(A(0), NumberStyles.Float, CultureInfo.InvariantCulture, out var tariff) || tariff is < 0 or > 0.50) return "tariff must be between 0 and 0.50";
                if (args.Length > 1 && (!double.TryParse(A(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var ip) || ip is < 0.1 or > 4)) return "import priority must be between 0.1 and 4";
                if (args.Length > 2 && (!double.TryParse(A(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var protection) || protection is < 0 or > 0.9)) return "protectionism must be between 0 and 0.9";
                return null;
            case GameCommand.NegotiateTrade:
            {
                if (args.Length is < 1 or > 4) return "bad trade agreement";
                var target = w.GetCountry(A(0));
                if (target == null || target == country || w.AtWar(country, target)) return "invalid trade partner";
                if (args.Length == 2 && (!double.TryParse(A(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var discount) || discount is < 0 or > 1)) return "bad tariff discount";
                if (args.Length > 2 && A(2).Length > 64) return "bad commodity lane";
                if (args.Length > 3 && (!double.TryParse(A(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)) return "bad commodity quantity";
                return g.TradeNetwork.Agreements.Any(a => a.From == country && a.To == target && a.Active(g.World.Date) && (args.Length < 3 || a.Commodity.Equals(A(2), StringComparison.OrdinalIgnoreCase))) ? "agreement already active" : null;
            }
            case GameCommand.SetTradeBlockade:
            {
                if (args.Length != 2 || (A(1) != "0" && A(1) != "1")) return "bad blockade command";
                var target = w.GetCountry(A(0));
                return target != null && target != country && w.AtWar(country, target) ? null : "blockade requires a country at war";
            }
            case GameCommand.BuildIndustry:
            {
                var building = g.Data.BuildingTypes.GetValueOrDefault(A(0));
                var state = w.GetState(A(1));
                if (args.Length != 2 || building == null || state == null || state.Owner != country) return "invalid industry project";
                return g.Production.Enabled
                    ? g.Production.CannotBuildReason(country, building, state)
                    : g.Population.CannotGoldBuild(country, building, state);
            }
            case GameCommand.SetIndustryMethod:
            {
                if (args.Length != 3) return "bad production method";
                var state = w.GetState(A(0));
                if (state == null || state.Owner != country) return "invalid method state";
                return g.TradeNetwork.MethodsFor(country, state, A(1)).Any(r => r.Key.Equals(A(2), StringComparison.OrdinalIgnoreCase)) ? null : "method unavailable";
            }
            case GameCommand.EventOption:
            {
                var pe = args.Length == 2 ? g.Events.PlayerQueue.FirstOrDefault(x => x.Def.Id == A(0) && Scripting.ScopeHelpers.CountryOf(x.Context.Root) == country) : null;
                if (pe == null || !Sanitize.Int(A(1), 0, Math.Max(0, pe.Def.Options.Count - 1), out _)) return "no such event option";
                return null;
            }
            case GameCommand.Respond:
                if (args.Length != 3 || !Enum.TryParse<ProposalKind>(A(0), out var kind) || !Enum.IsDefined(kind) || (A(2) != "0" && A(2) != "1")) return "bad answer";
                return g.PendingProposals.Any(x => x.Kind == kind && x.To == country && x.From.Tag == A(1)) ? null : "no such proposal";
            case GameCommand.ModMessage:
                return g.ModNet.Check(country, args, true);
        }
        return "unknown command";
    }
}
