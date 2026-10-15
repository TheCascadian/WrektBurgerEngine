using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Multiplayer;

/// <summary>Budgets for mod code that network data can reach (<c>validate</c> on the host, <c>apply</c> everywhere).</summary>
public static class ModLimits
{
    /// <summary>Steps (function entries, loop iterations, handlers; see <see cref="Fuel"/>) one <c>validate</c> call may take.</summary>
    public static long ValidateSteps = 2_000_000;
    /// <summary>Steps one <c>apply</c> call may take. Counted, not timed, so every machine stops at the same point.</summary>
    public static long ApplySteps = 10_000_000;
    /// <summary>Bytes one call may allocate.</summary>
    public static long AllocBytes = 64L << 20;
    /// <summary>Wall-clock limit for one <c>validate</c> call (time spent inside base-library calls is not counted in steps). A rule over it is switched off afterwards.</summary>
    public static int ValidateDeadlineMs = 100;
}

/// <summary>The custom messages of all loaded mods. Mods register typed messages; the session carries them as <c>mod_message</c> commands.</summary>
internal sealed class ModNetworkRegistry
{
    public sealed record Entry(string Key, string Mod, string Name, ModMessageSchema Schema, Func<ModMessage, string?> Validate, Action<ModMessage> Apply);

    private readonly Game _game;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public const int MaxMessages = 256;
    private readonly HashSet<string> _tripped = new(StringComparer.Ordinal);
    /// <summary>Messages whose validate or apply used up its budget (switched off for this session).</summary>
    public IReadOnlyCollection<string> Tripped => _tripped;

    /// <summary>Set by a running session: hands a message to the host. Returns false when it could not be sent.</summary>
    public Func<string, string[], bool>? Outbound;
    public bool IsMultiplayer => Outbound != null;

    public ModNetworkRegistry(Game game) { _game = game; }

    public IEnumerable<string> Keys => _entries.Keys.OrderBy(k => k, StringComparer.Ordinal);

    public void Register(string mod, string name, ModMessageSchema schema, Func<ModMessage, string?> validate, Action<ModMessage> apply)
    {
        if (Sanitize.Tag(name).Length == 0) throw new ArgumentException("message names use letters, digits and underscore (at most 32)");
        if (Sanitize.Name(mod).Length == 0 && mod.Length > 96) throw new ArgumentException("mod name too long");
        string key = mod + "/" + name;
        if (key.Length > 120) throw new ArgumentException("message key too long");
        if (_entries.Count >= MaxMessages && !_entries.ContainsKey(key)) throw new InvalidOperationException("too many mod messages");
        _entries[key] = new Entry(key, mod, name, schema, validate, apply);
    }

    /// <summary>Checks a message against its schema and, on the host, the mod's own rule. Returns null when it may go ahead.</summary>
    public string? Check(Country sender, string[] args, bool runModRule)
    {
        if (args.Length == 0 || !_entries.TryGetValue(args[0], out var e)) return "unknown mod message";
        var fields = args[1..];
        if (!e.Schema.Validate(fields, _game, out var err)) return err;
        if (!runModRule) return null;
        if (_tripped.Contains(e.Key)) return "the mod's message is switched off (it ran too long)";
        // the rule runs metered on this thread: a loop, deep recursion or a large allocation stops it, the stop cannot be caught
        var msg = new ModMessage(e.Mod, e.Name, sender, e.Schema, fields);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = Fuel.Run(ModLimits.ValidateSteps, ModLimits.AllocBytes, () => e.Validate(msg));
            if (sw.ElapsedMilliseconds > ModLimits.ValidateDeadlineMs)
            {
                _tripped.Add(e.Key);
                Log.Error($"Mod message '{e.Key}' validate took {sw.ElapsedMilliseconds} ms (limit {ModLimits.ValidateDeadlineMs}) and was switched off", "scripts");
                return "the mod's check ran too long";
            }
            return result;
        }
        catch (ModBudgetException ex)
        {
            _tripped.Add(e.Key);
            Log.Error($"Mod message '{e.Key}' validate stopped ({ex.Message}) and was switched off", "scripts");
            return "the mod's check ran too long";
        }
        catch (Exception ex) { Log.Error($"Mod message '{e.Key}' validate threw {ex.GetType().Name}: {ex.Message}", "scripts"); return "the mod's check failed"; }
    }

    /// <summary>Runs the mod's handler. Called at the same tick on every machine.</summary>
    public bool Apply(Country sender, string[] args)
    {
        if (Check(sender, args, false) != null) return false;
        var e = _entries[args[0]];
        var msg = new ModMessage(e.Mod, e.Name, sender, e.Schema, args[1..]);
        try { Fuel.Run(ModLimits.ApplySteps, ModLimits.AllocBytes, () => { e.Apply(msg); return 0; }); }
        catch (ModBudgetException ex)
        {
            // steps are counted, so every machine stops here at the same point; the host refuses the message from now on
            _tripped.Add(e.Key);
            Log.Error($"Mod message '{e.Key}' apply stopped ({ex.Message}) and was switched off", "scripts");
        }
        catch (Exception ex) { Log.Error($"Mod message '{e.Key}' threw {ex.GetType().Name}: {ex.Message}", "scripts"); }
        return true;
    }

    public bool Send(string mod, string name, object[] values)
    {
        string key = mod + "/" + name;
        if (!_entries.TryGetValue(key, out var e)) return false;
        var fields = e.Schema.Encode(values, out var err);
        if (fields == null) { Log.Warn($"Mod message '{key}' not sent: {err}"); return false; }
        var args = new string[fields.Length + 1];
        args[0] = key;
        fields.CopyTo(args, 1);
        if (Outbound != null) return Outbound(key, args);
        // no session: the same checks, applied right away
        var me = _game.World.Player;
        if (me == null || Check(me, args, true) != null) return false;
        return Apply(me, args);
    }
}

/// <summary>What one mod script sees as <see cref="GameScript.Network"/>.</summary>
internal sealed class ModNetworkHandle : IModNetwork
{
    private readonly ModNetworkRegistry _reg;
    private readonly string _mod;
    private readonly bool _allowed;
    public ModNetworkHandle(ModNetworkRegistry reg, string mod, bool allowed) { _reg = reg; _mod = mod; _allowed = allowed; }

    public bool IsMultiplayer => _reg.IsMultiplayer;

    public void Register(string name, ModMessageSchema schema, Func<ModMessage, string?> validate, Action<ModMessage> apply)
    {
        if (!_allowed) throw new ModPermissionException("network");
        _reg.Register(_mod, name, schema, validate, apply);
    }

    public bool Send(string name, params object[] values)
    {
        if (!_allowed) throw new ModPermissionException("network");
        return _reg.Send(_mod, name, values);
    }
}

/// <summary>What one mod script sees as <see cref="GameScript.Content"/>: text files below <c>scripts/content/</c>.</summary>
internal sealed class ModContentHandle : IModContent
{
    private readonly string _root;
    private readonly bool _allowed;
    public const long MaxBytes = 1024 * 1024;
    public ModContentHandle(string gameRoot, bool allowed) { _root = Path.Combine(gameRoot, "scripts", "content"); _allowed = allowed; }

    public bool Exists(string relativePath)
    {
        if (!_allowed) throw new ModPermissionException("content");
        string? full = Sanitize.RelativePath(_root, relativePath);
        return full != null && File.Exists(full);
    }

    public string? ReadText(string relativePath)
    {
        if (!_allowed) throw new ModPermissionException("content");
        string? full = Sanitize.RelativePath(_root, relativePath);
        if (full == null || !File.Exists(full) || new FileInfo(full).Length > MaxBytes) return null;
        return File.ReadAllText(full);
    }
}
