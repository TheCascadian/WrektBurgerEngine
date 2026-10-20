using System.Globalization;

namespace Burger.Core.Multiplayer;

/// <summary>
/// The protocol: which messages exist, who may send them and how big they may be. Every received line is checked here before any
/// handler sees it. Unknown, wrongly shaped, oversized or wrong-direction messages are refused (the sender gets a strike).
/// </summary>
public static class Wire
{
    public const int Version = 2;
    /// <summary>A line on the wire (encrypted, base64) never exceeds this; larger payloads are split into chunks.</summary>
    public const int MaxRawLine = 96 * 1024;
    /// <summary>Plain text of one message.</summary>
    public const int MaxLine = 64 * 1024;
    public const int SyncChunk = 32 * 1024;
    public const long MaxSyncBytes = 96L * 1024 * 1024;       // compressed save
    public const long MaxSaveBytes = 256L * 1024 * 1024;      // after decompression
    public const int MaxArgs = 16, MaxArgLen = 256;

    public enum Dir { ToHost, ToClient }

    private readonly record struct Spec(Dir Dir, int Fields, int MaxLen, bool NeedsReady);

    private static readonly Dictionary<string, Spec> Specs = new(StringComparer.Ordinal)
    {
        // client -> host
        ["HELLO"] = new(Dir.ToHost, 4, 1024, false),
        ["READY"] = new(Dir.ToHost, 1, 16, false),
        ["PICK"] = new(Dir.ToHost, 2, 128, true),
        ["CMD"] = new(Dir.ToHost, 5, 4096, true),
        ["VOTE"] = new(Dir.ToHost, 3, 64, true),
        ["CHAT"] = new(Dir.ToHost, 2, 2048, true),
        ["ACK"] = new(Dir.ToHost, 2, 32, false),
        ["SUM"] = new(Dir.ToHost, 3, 96, true),
        ["PONG"] = new(Dir.ToHost, 2, 64, false),
        // host -> client
        ["WELCOME"] = new(Dir.ToClient, 7, 4096, false),
        ["REJECT"] = new(Dir.ToClient, 2, 512, false),
        ["SYNC"] = new(Dir.ToClient, 4, 1024, false),
        ["SYNCDATA"] = new(Dir.ToClient, 3, SyncChunk * 2, false),
        ["SYNCEND"] = new(Dir.ToClient, 2, 64, false),
        ["PLAYERS"] = new(Dir.ToClient, 2, 8192, false),
        ["HORIZON"] = new(Dir.ToClient, 2, 32, false),
        ["PACE"] = new(Dir.ToClient, 3, 32, false),
        ["TOAST"] = new(Dir.ToClient, 2, 2048, false),
        ["PING"] = new(Dir.ToClient, 2, 64, false),
    };

    /// <summary>Returns null when the line is acceptable, otherwise a short reason.</summary>
    public static string? Check(Dir dir, string line, out string[] fields, bool ready = true)
    {
        fields = Array.Empty<string>();
        if (line.Length == 0) return "empty message";
        if (line.Length > MaxLine) return "message too large";
        fields = line.Split('|');
        string type = fields[0];
        Spec spec;
        if (dir == Dir.ToClient && type == "CMD") spec = new Spec(Dir.ToClient, 5, 4096, false);
        else if (dir == Dir.ToClient && type == "CHAT") spec = new Spec(Dir.ToClient, 3, 2048, false);
        else if (!Specs.TryGetValue(type, out spec)) return "unknown message";
        if (spec.Dir != dir) return "message not allowed in this direction";
        if (fields.Length != spec.Fields) return "wrong field count";
        if (line.Length > spec.MaxLen) return "message too large";
        if (spec.NeedsReady && !ready) return "message before the handshake finished";
        return null;
    }

    public static bool Int(string s, long min, long max, out long v) => Sanitize.Int(s, min, max, out v);

    public static bool Int(string s, int min, int max, out int v)
    {
        bool ok = Sanitize.Int(s, min, max, out long l);
        v = (int)l;
        return ok;
    }

    /// <summary>Command types a host may send to clients (system commands included).</summary>
    public static bool KnownCommandType(string type) =>
        type is GameCommand.Move or GameCommand.DeclareWar or GameCommand.AcceptPeace or GameCommand.RefusePeace or GameCommand.WhitePeace
            or GameCommand.QueueProduction or GameCommand.SetTradePolicy or GameCommand.NegotiateTrade or GameCommand.SetTradeBlockade
            or GameCommand.BuildIndustry or GameCommand.SetIndustryMethod or GameCommand.Develop or GameCommand.Colonize or GameCommand.Reinforce or GameCommand.AbandonColony or GameCommand.Reform
            or GameCommand.EventOption or GameCommand.Respond or GameCommand.ModMessage
            or GameCommand.Claim or GameCommand.Release;

    /// <summary>Shape check of the arguments of a command (the rules check happens in <see cref="CommandValidator"/>).</summary>
    public static bool ArgsOk(string[] args)
    {
        if (args.Length > MaxArgs) return false;
        foreach (var a in args) if (a.Length > MaxArgLen || a.Any(char.IsControl)) return false;
        return true;
    }
}
