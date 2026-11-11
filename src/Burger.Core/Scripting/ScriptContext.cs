using Burger.Core.Script;
namespace Burger.Core.Scripting;

/// <summary>
/// The scopes a trigger or effect runs in. <see cref="This"/> is the current scope, <see cref="Root"/> the
/// scope the script started in (e.g. the country an event fires for), and <see cref="Scopes"/> holds named
/// scopes such as <c>scope:actor</c>, <c>scope:recipient</c> or anything saved with <c>save_scope_as</c>.
/// </summary>
public sealed class ScriptContext
{
    public Game Game { get; }
    public IScope? Root { get; init; }
    public IScope? This { get; init; }
    public IScope? Prev { get; init; }
    public Dictionary<string, IScope?> Scopes { get; init; }
    private string? _where = "";
    private Node? _whereNode;

    /// <summary>Script file location used for error messages. A statement being evaluated only notes its node (<see cref="WhereAt"/>);
    /// the "file:line" text is made when something reads it.</summary>
    public string Where
    {
        get => _where ?? _whereNode!.Location;
        set { _where = value; _whereNode = null; }
    }

    /// <summary>The location is <paramref name="n"/>'s (the same text as <c>Where = n.Location</c>, made only when read).</summary>
    public void WhereAt(Node n) { _whereNode = n; _where = null; }

    public ScriptContext(Game game, IScope? root, Dictionary<string, IScope?>? scopes = null)
    {
        Game = game;
        Root = root;
        This = root;
        Scopes = scopes ?? new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase);
    }

    private ScriptContext(Game game, IScope? root, IScope? @this, IScope? prev, Dictionary<string, IScope?> scopes, string? where, Node? whereNode)
    {
        Game = game;
        Root = root;
        This = @this;
        Prev = prev;
        Scopes = scopes;
        _where = where;
        _whereNode = whereNode;
    }

    /// <summary>Same execution, new current scope (PREV becomes the old THIS).</summary>
    public ScriptContext Enter(IScope? scope) => new(Game, Root, scope, This, Scopes, _where, _whereNode);

    public ScriptContext WithScope(string name, IScope? scope)
    {
        Scopes[name] = scope;
        return this;
    }

    public IScope? Named(string name) => Scopes.TryGetValue(name, out var s) ? s : null;

    public Model.Country? ThisCountry => This as Model.Country;
    public Model.Character? ThisCharacter => This as Model.Character;
    public Model.State? ThisState => This as Model.State;
    public Model.Location? ThisLocation => This as Model.Location;

    /// <summary>The country "behind" the current scope (a character's employer, a state's owner...).</summary>
    public Model.Country? CountryOfThis => ScopeHelpers.CountryOf(This);
}

public static class ScopeHelpers
{
    public static Model.Country? CountryOf(IScope? s) => s switch
    {
        Model.Country c => c,
        Model.Character ch => ch.Country,
        Model.State st => st.Owner,
        Model.Location l => l.Owner,
        _ => null,
    };
}
