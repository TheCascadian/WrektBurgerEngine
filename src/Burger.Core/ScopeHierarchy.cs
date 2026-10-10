namespace Burger.Core;

/// <summary>
/// Organizational parent/child links between script scopes. This service deliberately does not represent
/// ownership, diplomacy, permissions, or any other gameplay relationship.
/// </summary>
public sealed class ScopeHierarchy
{
    private readonly Dictionary<IScope, IScope> _parents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IScope, List<IScope>> _children = new(ReferenceEqualityComparer.Instance);

    /// <summary>Explicit parent links in insertion order.</summary>
    public IEnumerable<(IScope Child, IScope Parent)> ParentLinks
        => _parents.Select(pair => (pair.Key, pair.Value));

    /// <summary>Registers a scope without changing its existing parent.</summary>
    public void Register(IScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!_children.ContainsKey(scope)) _children.Add(scope, new List<IScope>());
    }

    /// <summary>Removes every registered scope and link.</summary>
    public void Clear()
    {
        _parents.Clear();
        _children.Clear();
    }

    /// <summary>Returns a scope's direct parent, or null when it has none.</summary>
    public IScope? ParentOf(IScope? scope)
        => scope != null && _parents.TryGetValue(scope, out var parent) ? parent : null;

    /// <summary>Returns a snapshot of direct children in insertion order.</summary>
    public IReadOnlyList<IScope> ChildrenOf(IScope? scope)
        => scope != null && _children.TryGetValue(scope, out var children) ? children.ToArray() : Array.Empty<IScope>();

    /// <summary>Traverses descendants depth-first in direct-child insertion order, returning original entities.</summary>
    public IEnumerable<IScope> DescendantsOf(IScope? scope)
    {
        if (scope == null) yield break;
        // Snapshot each level: a script may reparent scopes while an iterator effect is running.
        foreach (var child in ChildrenOf(scope))
        {
            yield return child;
            foreach (var descendant in DescendantsOf(child)) yield return descendant;
        }
    }

    /// <summary>Assigns the parent only when the scope has no different parent already.</summary>
    public bool TrySetParent(IScope child, IScope parent, out string error)
    {
        if (_parents.TryGetValue(child, out var existing) && !ReferenceEquals(existing, parent))
        {
            error = $"Scope '{child.ScopeKey}' already has parent '{existing.ScopeKey}'";
            return false;
        }
        return TryReparent(child, parent, out error);
    }

    /// <summary>Moves a scope to a new parent, or to null to detach it. All changes are validated before mutation.</summary>
    public bool TryReparent(IScope child, IScope? parent, out string error)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (parent != null && ReferenceEquals(child, parent))
        {
            error = $"Scope '{child.ScopeKey}' cannot be its own parent";
            return false;
        }
        if (parent != null)
        {
            for (IScope? cursor = parent; cursor != null; cursor = ParentOf(cursor))
            {
                if (ReferenceEquals(cursor, child))
                {
                    error = $"Reparenting '{child.ScopeKey}' under '{parent.ScopeKey}' would create a cycle";
                    return false;
                }
            }
        }

        Register(child);
        if (parent != null) Register(parent);
        if (_parents.TryGetValue(child, out var oldParent))
        {
            if (ReferenceEquals(oldParent, parent)) { error = ""; return true; }
            _children[oldParent].Remove(child);
            _parents.Remove(child);
        }
        if (parent != null)
        {
            _parents.Add(child, parent);
            _children[parent].Add(child);
        }
        error = "";
        return true;
    }
}
