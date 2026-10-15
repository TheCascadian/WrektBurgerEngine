using System.Globalization;
using Burger.Core.Model;

namespace Burger.Core.Modding;

/// <summary>What a custom network message of a mod looks like. Only these typed fields can travel; every field is range checked.</summary>
public sealed class ModMessageSchema
{
    public enum Kind { Int, Text, Bool, Country }
    public sealed record Field(string Name, Kind Kind, long Min, long Max);

    private readonly List<Field> _fields = new();
    public IReadOnlyList<Field> Fields => _fields;
    public const int MaxFields = 8;
    public const int MaxTextLength = 120;

    public static ModMessageSchema Create() => new();

    public ModMessageSchema Int(string name, long min, long max) => Add(new Field(name, Kind.Int, min, max));
    /// <summary>A line of text, at most <paramref name="maxLength"/> characters (printable characters only).</summary>
    public ModMessageSchema Text(string name, int maxLength = 60) => Add(new Field(name, Kind.Text, 0, Math.Clamp(maxLength, 1, MaxTextLength)));
    public ModMessageSchema Bool(string name) => Add(new Field(name, Kind.Bool, 0, 1));
    /// <summary>A country tag that exists in the game.</summary>
    public ModMessageSchema Country(string name) => Add(new Field(name, Kind.Country, 0, 0));

    private ModMessageSchema Add(Field f)
    {
        if (_fields.Count >= MaxFields) throw new InvalidOperationException($"a mod message has at most {MaxFields} fields");
        if (f.Name.Length == 0 || f.Name.Length > 32 || _fields.Any(x => x.Name == f.Name)) throw new ArgumentException("field names must be unique and 1-32 characters");
        _fields.Add(f);
        return this;
    }

    /// <summary>Encodes values into wire strings. Returns null (and why) when a value does not fit the schema.</summary>
    public string[]? Encode(object[] values, out string error)
    {
        error = "";
        if (values.Length != _fields.Count) { error = $"expected {_fields.Count} values, got {values.Length}"; return null; }
        var r = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var f = _fields[i];
            switch (f.Kind)
            {
                case Kind.Int:
                    if (values[i] is not (int or long or short or byte)) { error = $"'{f.Name}' must be a whole number"; return null; }
                    r[i] = Convert.ToInt64(values[i], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); break;
                case Kind.Bool:
                    if (values[i] is not bool b) { error = $"'{f.Name}' must be true or false"; return null; }
                    r[i] = b ? "1" : "0"; break;
                case Kind.Country:
                    r[i] = values[i] is Country c ? c.Tag : values[i] as string ?? ""; break;
                default:
                    r[i] = values[i] as string ?? ""; break;
            }
        }
        return Validate(r, null, out error) ? r : null;
    }

    /// <summary>Checks wire strings against the schema (sizes, ranges, characters, existing countries).</summary>
    public bool Validate(string[] raw, Game? game, out string error)
    {
        error = "";
        if (raw.Length != _fields.Count) { error = "wrong number of fields"; return false; }
        for (int i = 0; i < raw.Length; i++)
        {
            var f = _fields[i];
            string v = raw[i];
            switch (f.Kind)
            {
                case Kind.Int:
                    if (!Multiplayer.Sanitize.Int(v, f.Min, f.Max, out _)) { error = $"'{f.Name}' must be a whole number from {f.Min} to {f.Max}"; return false; }
                    break;
                case Kind.Bool:
                    if (v != "0" && v != "1") { error = $"'{f.Name}' must be true or false"; return false; }
                    break;
                case Kind.Text:
                    if (v.Length > f.Max || Multiplayer.Sanitize.Text(v, (int)f.Max) != v) { error = $"'{f.Name}' is not plain text of at most {f.Max} characters"; return false; }
                    break;
                case Kind.Country:
                    if (Multiplayer.Sanitize.Tag(v).Length == 0 || (game != null && game.World.GetCountry(v) == null)) { error = $"'{f.Name}' is not a country"; return false; }
                    break;
            }
        }
        return true;
    }
}

/// <summary>A custom mod message that arrived (validated) and is being handled. Values are read by field name.</summary>
public sealed class ModMessage
{
    private readonly ModMessageSchema _schema;
    private readonly string[] _raw;
    internal ModMessage(string mod, string name, Country sender, ModMessageSchema schema, string[] raw) { Mod = mod; Name = name; Sender = sender; _schema = schema; _raw = raw; }

    public string Mod { get; }
    public string Name { get; }
    /// <summary>The country of the player who sent it (set by the host, never by the sender).</summary>
    public Country Sender { get; }

    private string Raw(string field)
    {
        for (int i = 0; i < _schema.Fields.Count; i++) if (_schema.Fields[i].Name == field) return _raw[i];
        throw new KeyNotFoundException($"the message has no field '{field}'");
    }
    public long GetInt(string field) => long.Parse(Raw(field), CultureInfo.InvariantCulture);
    public bool GetBool(string field) => Raw(field) == "1";
    public string GetText(string field) => Raw(field);
    public string GetCountryTag(string field) => Raw(field);
}

/// <summary>
/// Network access of a mod. A mod needs the "network" permission in <c>scripts/mod.json</c>. It never sees sockets or bytes: it registers typed
/// messages, and the game does transport, validation and ordering. A message is checked by the host (<c>validate</c>, against the rules and the
/// current game state), then applied at the same tick on every machine (<c>apply</c>), so apply must be deterministic and may only change the game.
/// </summary>
public interface IModNetwork
{
    /// <summary>True while a multiplayer session runs. Messages work the same without one (they are applied locally).</summary>
    bool IsMultiplayer { get; }
    /// <summary>Declares a message. <paramref name="validate"/> runs on the host only and returns null to accept or a reason to refuse.</summary>
    void Register(string name, ModMessageSchema schema, Func<ModMessage, string?> validate, Action<ModMessage> apply);
    /// <summary>Sends a message as the local player. Returns false when it was refused locally (unknown message, bad values, no country).</summary>
    bool Send(string name, params object[] values);
}

/// <summary>Reads the data files that belong to the mod (<c>scripts/content/</c>). Read only, text only, no path can leave that folder. Needs the "content" permission.</summary>
public interface IModContent
{
    bool Exists(string relativePath);
    /// <summary>Returns the text of a file, or null when the path is refused or the file is missing or larger than 1 MB.</summary>
    string? ReadText(string relativePath);
}

public sealed class ModPermissionException : InvalidOperationException
{
    public ModPermissionException(string permission) : base($"this mod did not ask for the '{permission}' permission in scripts/mod.json") { }
}
