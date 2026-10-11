# Politics panel icons

Politics now requests just the height its content needs. Tall lists use flexible scroll areas and are capped to the available screen height. Labels in the redesigned tabs use larger type and compact single-row navigation.

The supplied game data already contains matching fallback images in `gfx/interface/tooltips/` (including politics, organization, law, characters, rebellion, province, and modifier-category icons). Those are loaded automatically when present; no new art assets are included or required for the defaults.

To replace the Politics-specific tab and section icons, add any of these keys to the existing `tooltip_icons` block in `game/interface/theme.txt`. These unique keys intentionally let a custom theme override the built-in fallback:

```text
theme = {
    tooltip_icons = {
        politics_overview = "gfx/interface/politics/overview.png"
        politics_organization = "gfx/interface/politics/organization.png"
        politics_law = "gfx/interface/politics/law.png"
        cabinet = "gfx/interface/politics/cabinet.png"
        movement = "gfx/interface/politics/movement.png"
        effects = "gfx/interface/politics/effects.png"
        faction = "gfx/interface/politics/faction.png"
        branch = "gfx/interface/politics/branch.png"
    }
}
```

Paths are relative to the game root. A key can be omitted to keep its bundled fallback. `organization` imagery is used for parties and factions by default; `province` is used for regional branches. Effects rows also select existing semantic icons where the modifier/source name makes a reasonable match (e.g. manpower, stability, laws, territory, or production).

## Optional icon per law

Set `icon` on an individual law to make its choice button icon-only. When a valid icon is supplied, the law name is automatically moved into the hover tooltip, together with its description, effects, cost, eligibility checks, and any blocking reason. If the icon is omitted or cannot be loaded, the choice stays a readable text button.

Add `icon` to a law block in its normal `common/laws/*.txt` file, for example:

```text
conscription = {
    name = "Conscription"
    default = volunteer_only

    volunteer_only = {
        name = "Volunteer Only"
        desc = "A small professional force."
        icon = "gfx/interface/politics/law_volunteer_only.png"
        cost = 100
    }

    extensive_conscription = {
        name = "Extensive Conscription"
        desc = "A larger pool of available recruits."
        icon = "gfx/interface/politics/law_extensive_conscription.png"
        cost = 150
    }
}
```
