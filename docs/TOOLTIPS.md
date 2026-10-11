# Tooltip icons and state colors

Every non-empty tooltip title and body row receives a **subject-specific image asset** and a semantic text/icon tint. The renderer classifies existing plain-text tooltips automatically, so direct tooltip calls and button tooltips are covered without a callsite migration. Use explicit tags whenever a label is ambiguous; they override inference.

## Row syntax

Prefix a title or body row with `[icon:<subject>]`, `[state:<state>]`, or both:

```text
[icon:battle] Battle of the Narrows
[icon:army][state:info] Attacker: Northland (3 divisions)
[icon:war][state:warning] Losses: 125 attacker / 98 defender
[state:value] Terrain: hills
```

Supported states are `neutral`, `good`, `bad`, `warning`, `info`, and `value`. State controls the row text and icon tint; explicit `neutral` forces the neutral color, while unmarked rows receive an inferred state. The older `[+]`, `[-]`, `[!]`, `[i]`, and `[=]` prefixes still map to good, bad, warning, info, and value.

Unmarked rows are classified from their text and tooltip title. Numeric rows default to `value`; positive outcomes to `good`; adverse outcomes to `bad` or `warning`; explanatory text to `info`. Explicit tags override these defaults. Unknown text uses the theme's `information` image rather than a procedural glyph.

## Theme configuration

Map subject IDs to transparent image assets relative to the game folder:

```text
tooltip_icons = {
    battle = "gfx/interface/ui/menu_military.png"
    army = "gfx/interface/ui/menu_military.png"
    navy = "gfx/interface/ui/menu_navy.png"
    production = "gfx/interface/ui/menu_production.png"
}
```

All four themed projects (SampleGame, Earth1930, GameTest, and Test2) include mappings for military stats, diplomacy, population, economy, characters, research, and terrain. Category and stat aliases may share a shipped image by default, but each subject key is independently configurable. Mods may add arbitrary subject IDs and image paths. Textures are cached by the existing asset loader, aspect-fitted in the tooltip's icon column, and tinted by the row state.

Optional state colors are defined under `colors` in `interface/theme.txt`:

```text
colors = {
    tooltip_neutral = { 236 230 214 }
    tooltip_good = { 126 214 120 }
    tooltip_bad = { 236 104 92 }
    tooltip_warning = { 240 190 60 }
    tooltip_info = { 142 196 226 }
    tooltip_value = { 245 207 112 }
}
```

Omit any key to use the engine default. Runtime code can override colors with `Game.Interface.Theme.SetColor("tooltip_warning", 255, 160, 40)` and change icon mappings with `Game.Interface.Theme.SetTooltipIcon("terrain", "gfx/interface/icons/terrain.png")`.
