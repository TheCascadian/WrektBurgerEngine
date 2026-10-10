# Scopes: a practical guide

This guide explains how to use the engine's generic scopes in simple terms. A **scope** is an object scripts can work with: a country, character, state, location, war, or a generic scope created by your game data or a script.

## The quick version

1. Define a generic scope in `game/common/scopes/`.
2. Give it a unique `id` (the block name), a `kind`, optional data, and optionally a `parent`.
3. Use `generic:<id>` to find it from a script.
4. Use `scope_child` for its immediate children and `scope_descendant` for everything below it.

A parent is just an organizational link. It does **not** mean the parent owns, employs, commands, or has permission over the child.

## 1. Make a scope in game data

Create a file such as `game/common/scopes/organizations.txt`:

```text
# This child can refer to a parent declared later in the file.
north_office = {
    kind = office
    name = "Northern Office"
    parent = generic:agency

    data = {
        region = north
        rank = 2
    }
    vars = {
        budget = 50
    }
    flags = { active }
}

agency = {
    kind = organization
    name = "Example Agency"
    parent = country:GER
}
```

What the fields mean:

| Field | Meaning |
| --- | --- |
| Block name, e.g. `north_office` | The scope's stable ID. Use it in references as `generic:north_office`. IDs must be unique (case-insensitively). |
| `kind` | A label you choose, such as `office`, `team`, or `department`. Use it to distinguish kinds of generic scopes in scripts. |
| `name` | Optional display name. |
| `parent` | Optional organizational parent. It may refer to a generic scope declared later, or to an existing country, state, location, or character. |
| `data` | Optional text attributes. Numeric values are also copied into the scope's numeric variable store. |
| `vars` | Optional numeric script variables. |
| `flags` | Optional script flags, like the flags on other engine scopes. |

All generic scopes are created before their parent references are resolved, so forward references work. Unknown parents, duplicate parent declarations, and cycles are reported with source-file context.

## 2. Use a scope in a script

### Find it by ID

Use the `generic:` prefix to switch to one specific generic scope:

```text
generic:north_office = {
    set_variable = { name = budget value = 75 }
    set_flag = reviewed
}
```

You can also use `scope_id:north_office`. Named scopes saved with `save_scope_as` still use the existing `scope:<name>` syntax; that is different from a generic scope's ID.

### Read its kind or data

These are triggers and can be used in a trigger/limit block:

```text
scope_kind = office
scope_data = { region = north }
has_scope_data = rank
has_flag = active
```

`scope_kind` checks the custom kind for a generic scope. `scope_type` checks the engine's built-in scope type (for generic scopes that type is `Generic`).

Numeric data can be used as a variable. For example, `rank = 2` in the `data` block is also available as `var:rank`:

```text
check_variable = { name = rank value >= 2 }
```

To change a numeric variable during play:

```text
set_variable = { name = budget value = 75 }
change_variable = { name = budget add = 10 }
```

Text attributes are read-only in script triggers in this implementation; use `scope_data` to compare them. The usual C# scope API can also inspect or update the generic scope's `Attributes` dictionary.

## 3. Organize and find child scopes

The hierarchy has three useful directions:

- **Parent:** the one direct parent, reached through `parent` or `scope_parent`.
- **Child:** an immediate child, iterated with `scope_child`.
- **Descendant:** any scope below the current one, at any depth, iterated with `scope_descendant`.

For example, while the current scope is an agency:

```text
# Check whether any scope at any depth is an active northern office.
any_scope_descendant = {
    scope_kind = office
    scope_data = { region = north }
    has_flag = active
}
```

Common iterator forms:

```text
# Trigger: at least one immediate child is an office.
any_scope_child = { scope_kind = office }

# Trigger: every descendant must have a value of at least 1.
all_scope_descendant = { check_variable = { name = level value >= 1 } }

# Effect: run an effect on every descendant.
every_scope_descendant = {
    limit = { scope_kind = office }
    set_flag = checked
}

# Effect: choose one matching descendant at random.
random_scope_descendant = {
    limit = { scope_kind = office }
    set_flag = selected
}
```

`scope_descendant` traverses depth-first in stable child insertion order. It returns the existing scope entities; it does not clone their data.

You can also navigate upward from a child:

```text
parent = {
    # This block now runs on the direct parent.
    set_flag = has_child
}
```

Or test a relationship explicitly:

```text
is_scope_child_of = generic:agency
is_scope_descendant_of = country:GER
```

## 4. Create and move scopes during play

Use `create_scope` as an effect to create a new generic scope. Its ID must not already exist:

```text
create_scope = {
    id = temporary_office
    kind = office
    name = "Temporary Office"
    parent = generic:agency
    data = { region = west }
    vars = { budget = 20 }
    flags = { active }
}
```

To move the current scope under a different parent:

```text
set_scope_parent = { parent = generic:another_agency }
```

To detach the current scope from its parent:

```text
set_scope_parent = none
# Equivalent explicit effect:
clear_scope_parent = yes
```

The engine rejects unknown parent references, self-parenting, and changes that would create a cycle. A failed reparent leaves the previous link unchanged. Moving a scope changes only the organizational hierarchy; it does not transfer ownership or modify other relationships.

## 5. Keep these concepts separate

Think of the systems as separate questions:

- **Hierarchy:** “Where is this scope organized?” (`parent`, `scope_child`, `scope_descendant`)
- **Relationships:** “Who owns, employs, supplies, allies with, or is related to it?” Use the engine's existing relationship links and systems for those facts.
- **Authority:** “Who may read, modify, or command it?” Hierarchy does not grant these permissions. Do not treat being a parent as authorization.

The existing `child`, `is_child_of`, and `is_parent_of` keywords retain their pre-existing character-family meaning. Use `scope_child` and the `is_scope_*` triggers for generic hierarchy instead.

## 6. Saves and quick troubleshooting

- Generic scope variables, flags, text attributes, dynamic scopes, and hierarchy links are included in new saves.
- Older saves without generic scope sections remain loadable; data-defined parent links are rebuilt from the game files.
- If a scope is missing, check that its ID is spelled correctly and that its definition is in `game/common/scopes/`.
- If a parent reference fails, make sure the target already exists at resolution time. Generic scopes may forward-reference one another; a data-defined scope cannot refer to a runtime entity that has not been created yet.
- If a cycle is reported, follow the `parent` links until you find a chain that leads back to its starting scope.

For a runnable fixture and scope self-tests, use:

```sh
burger validate Test --selftest --days 0
```
