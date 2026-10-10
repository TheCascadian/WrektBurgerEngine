namespace Burger.Core.Model;

using Burger.Core.Script;

/// <summary>A data-defined or runtime-created scope that can participate in the generic scope hierarchy.</summary>
public sealed class GenericScope : IScope
{
    public string Key { get; init; } = "";
    public string Kind { get; set; } = "generic";
    /// <summary>Raw display name; localization is resolved by <see cref="DisplayName"/>.</summary>
    public string Name { get; set; } = "";
    /// <summary>True when created during play rather than loaded from common/scopes.</summary>
    public bool Dynamic { get; set; }
    /// <summary>Original source definition used only to reconstruct hierarchy links for saves predating hierarchy data.</summary>
    public Node? Definition { get; set; }
    /// <summary>Arbitrary text attributes from data definitions. Numeric script state belongs in <see cref="Vars"/>.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public VarStore Vars { get; } = new();
    public ScopeType ScopeType => ScopeType.Generic;
    public string ScopeKey => Key;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Localisation.GetOrPretty(Key) : Localisation.Get(Name);
    public override string ToString() => Key;
}
