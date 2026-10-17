using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The character sheet: a window with everything about one person - who they are, where they live, their abilities and their family.</summary>
public sealed partial class Hud
{
    private Character? _sheet;
    private readonly Stack<Character> _sheetBack = new();
    private float _sheetH = 700;

    /// <summary>Opens the sheet of a character (clicking a relative inside the sheet remembers where you came from).</summary>
    private void OpenSheet(Character ch)
    {
        if (_sheet != null && _sheet != ch) _sheetBack.Push(_sheet);
        _sheet = ch;
        Ui.SelectedCharacter = ch;
        Gui.ResetScroll("char_sheet");
    }

    private void CloseSheet()
    {
        _sheet = null;
        _sheetBack.Clear();
    }

    private void DrawCharacterSheet()
    {
        if (_sheet == null) return;
        var ch = _sheet;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float w = 520 * S;
        var r = new Rectangle(sw - w - 10 * S, TopH + 8 * S, w, sh - TopH - 20 * S);
        Gui.Panel(r, "Character");
        Gui.Block(r);
        if (Gui.CloseButton(r)) { CloseSheet(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 6 * S, cw = r.Width - 24 * S;
        if (_sheetBack.Count > 0 && Gui.Button(new Rectangle(x, y, 80 * S, 24 * S), "< Back", true, "Back to " + _sheetBack.Peek().DisplayName, false, 13))
        {
            _sheet = _sheetBack.Pop();
            Gui.ResetScroll("char_sheet");
            return;
        }
        if (_sheetBack.Count > 0) y += 30 * S;

        var area = new Rectangle(x - 2 * S, y, cw + 4 * S, r.Bottom() - y - 8 * S);
        float top = Gui.BeginScroll("char_sheet", area, _sheetH);
        float end = DrawSheetBody(ch, x, top, cw - 10 * S);
        _sheetH = end - top + 12 * S;
        Gui.EndScroll();
    }

    private float DrawSheetBody(Character ch, float x, float y, float w)
    {
        var now = G.World.Date;
        var country = ch.Country;
        var home = country ?? G.World.Player;
        var roles = home != null ? RolesOf(ch, home) : new List<CharRole>();
        int age = ch.Age(now);
        var dyn = G.Dynasties;

        // ---- identity
        float pw = 116 * S, ph = 144 * S;
        var pr = new Rectangle(x, y, pw, ph);
        DrawPortrait(ch, pr, true, false);
        PortraitFrame(new Rectangle(pr.X - 1, pr.Y - 1, pr.Width + 2, pr.Height + 2), new Color(0, 0, 0, 220), 2);
        PortraitFrame(pr, roles.Count > 0 ? roles[0].Tint : Gui.PanelBorder, 2);
        SickMark(ch, pr);
        DrawIntrigueOverlay(ch, pr);
        float hx = x + pw + 12 * S, hw = w - pw - 12 * S, hy = y;
        Gui.Label(Gui.Fit(ch.DisplayName, hw, 21, FontKind.Header), hx, hy, 21, Gui.Gold, FontKind.Header);
        hy += 30 * S;
        Gui.Label($"{(ch.Female ? "Woman" : "Man")} · {(ch.Alive ? $"age {age}" : $"died aged {age}")}", hx, hy, 14, Gui.Text);
        hy += 20 * S;
        Gui.Label(Gui.Fit($"Born {ch.BirthDate.ToDisplay()}", hw, 13), hx, hy, 13, Gui.TextDim);
        hy += 18 * S;
        if (!ch.Alive)
        {
            Gui.Label(Gui.Fit($"Died {ch.DeathDate?.ToDisplay()} ({ch.DeathReason})", hw, 13), hx, hy, 13, Gui.Bad);
            hy += 18 * S;
        }
        if (ch.Family.Length > 0) { Gui.Label($"House {Localisation.Get(ch.Family)}", hx, hy, 14, Gui.Text, FontKind.Bold); hy += 20 * S; }
        if (dyn.Enabled)
        {
            string claim = dyn.ClaimLabel(ch);
            Gui.Label(claim, hx, hy, 13, dyn.Claim(ch) < 1 ? Gui.Gold : Gui.TextDim);
            if (ch.Family.Length > 0 && ch.Alive) { string pr2 = $"  ·  house prestige {dyn.StateOf(ch.Family).Prestige:0}"; Gui.Label(pr2, hx + Gui.Measure(claim, 13).X, hy, 13, Gui.TextDim); }
            hy += 18 * S;
        }
        float cx = hx;
        foreach (var role in roles)
        {
            float cwid = ChipWidth(role.Text);
            if (cx + cwid > hx + hw && cx > hx) { cx = hx; hy += 22 * S; }
            cx += Chip(role.Text, cx, hy, role.Tint, role.Tip) + 4 * S;
        }
        y += Math.Max(ph, hy - y + 24 * S) + 8 * S;

        // ---- where they live
        Gui.Section("Where they live", x, y, w);
        y += 26 * S;
        if (country != null)
        {
            Gui.Flag(country, new Rectangle(x, y, 30 * S, 20 * S));
            Gui.Label(Gui.Fit(country.Name, w - 40 * S, 15, FontKind.Bold), x + 38 * S, y, 15, Gui.Text, FontKind.Bold);
        }
        else Gui.Label("In exile, belonging to no country", x, y, 15, Gui.TextDim);
        y += 24 * S;
        var (place, loc) = ResidenceOf(ch);
        if (place.Length > 0)
        {
            Gui.Label(Gui.Fit(place, w - 130 * S, 14), x, y + 2 * S, 14, Gui.Text);
            if (loc != null && Gui.Button(new Rectangle(x + w - 118 * S, y, 118 * S, 24 * S), "Show on map", true, "Move the camera to where they live", false, 13)) Cam.LookAt(Map.LocationPos(loc), 30);
            y += 28 * S;
        }
        var pop = G.Population;
        var bits = new List<string>();
        if (ch.Culture != null) bits.Add(pop.Culture(ch.Culture)?.Name ?? ch.Culture);
        if (ch.Religion != null) bits.Add(pop.Religion(ch.Religion)?.Name ?? ch.Religion);
        if (ch.PopType != null && pop.PopTypeByKey.TryGetValue(ch.PopType, out var pt)) bits.Add(pt.Name);
        if (bits.Count > 0) { Gui.Label(Gui.Fit(string.Join(" · ", bits), w, 13), x, y, 13, Gui.TextDim); y += 20 * S; }
        y += 4 * S;

        // ---- abilities
        Gui.Section("Abilities", x, y, w);
        y += 26 * S;
        var stats = G.Data.Defines.CharacterStats.ToList();
        if (stats.Count > 0)
        {
            float gap = 6 * S;
            int perRow = Math.Min(stats.Count, 4);
            float tw = (w - gap * (perRow - 1)) / perRow, th = 54 * S;
            for (int i = 0; i < stats.Count; i++)
                DrawStatTile(ch, stats[i], new Rectangle(x + i % perRow * (tw + gap), y + i / perRow * (th + gap), tw, th));
            y += (stats.Count + perRow - 1) / perRow * (th + gap);
        }
        Gui.Label($"Loyalty {ch.Loyalty:0}", x, y, 13, Gui.TextDim);
        Gui.Bar(new Rectangle(x + 90 * S, y + 6 * S, w * 0.32f, 6 * S), (float)(ch.Loyalty / 100), new Color(214, 170, 70, 255));
        Gui.Label($"Health {ch.Health:0}", x + w * 0.5f, y, 13, Gui.TextDim);
        Gui.Bar(new Rectangle(x + w * 0.5f + 80 * S, y + 6 * S, w * 0.32f - 30 * S, 6 * S), (float)(ch.Health / 100), ch.Health < 40 ? Gui.TooltipBad : new Color(104, 190, 108, 255));
        y += 24 * S;
        if (dyn.Enabled && dyn.IsSick(ch) && dyn.DiseaseOf(ch) is { } ill)
        {
            Gui.Label($"Ill with {ill.Name.ToLowerInvariant()} until {new GameDate(dyn.SickUntil(ch)).ToDisplay()}; it may kill.", x, y, 13, Gui.Bad);
            y += 20 * S;
        }
        if (ch.Traits.Count > 0)
        {
            float tx = x;
            foreach (var t in ch.Traits)
            {
                string name = Localisation.Get(t.Name);
                float cwid = ChipWidth(name, 13);
                if (tx + cwid > x + w && tx > x) { tx = x; y += 24 * S; }
                string tip = $"{name}\n{Localisation.Get(t.Description)}" + (t.Modifiers.Count > 0 ? "\n" + string.Join("\n", t.Modifiers.Select(m => $"{Localisation.GetOrPretty(m.Key)}: {m.Value:+0.##;-0.##}")) : "");
                tx += Chip(name, tx, y, new Color(214, 200, 170, 255), tip, 13) + 4 * S;
            }
            y += 30 * S;
        }

        y = DrawTitlesSection(ch, x, y, w);
        y = DrawMarriageSection(ch, x, y, w);
        y = DrawIntrigueSection(ch, x, y, w);

        // ---- family
        Gui.Section("Family", x, y, w);
        y += 26 * S;
        float Rel(string label, Character who, string extra = "")
        {
            Gui.Label(label, x, y - 1 * S, 11, Gui.TextDim, FontKind.Bold);
            return CharRow(who, x, y + 10 * S, w, extra) + 10 * S;
        }
        if (ch.Father != null) y += Rel("FATHER", ch.Father);
        if (ch.Mother != null) y += Rel("MOTHER", ch.Mother);
        if (ch.Spouse != null) y += Rel(ch.Female ? "HUSBAND" : "WIFE", ch.Spouse);
        if (dyn.Enabled)
        {
            foreach (var con in dyn.Concubines(ch)) y += Rel("CONCUBINE", con);
            if (dyn.IsConcubine(ch) && (int)ch.Vars.Get("concubine_of") is var rid && G.World.Characters.FirstOrDefault(c => c.Id == rid) is { } lord) y += Rel("CONCUBINE OF", lord);
        }
        var siblings = new[] { ch.Father, ch.Mother }.Where(p => p != null).SelectMany(p => p!.Children).Where(k => k != ch).Distinct().OrderByDescending(k => k.Age(now)).ToList();
        foreach (var sib in siblings.Take(8)) y += Rel(sib.Female ? "SISTER" : "BROTHER", sib, dyn.Enabled && dyn.Claim(sib) < 1 ? dyn.ClaimLabel(sib) : "");
        foreach (var kid in ch.Children.OrderBy(k => k.BirthDate.TotalDays).Take(12)) y += Rel(kid.Female ? "DAUGHTER" : "SON", kid, dyn.Enabled && dyn.Claim(kid) < 1 ? dyn.ClaimLabel(kid) : "");
        if (ch.Father == null && ch.Mother == null && ch.Spouse == null && ch.Children.Count == 0 && siblings.Count == 0)
        {
            Gui.Label("No known family.", x, y, 13, Gui.TextDim);
            y += 22 * S;
        }
        return y + DrawRememberedActs(ch, "Remembered acts", x, y, w);
    }

    /// <summary>Where a character lives: the capital for a ruler, regent or heir, the army's place for a general, else the home state or the country's capital.</summary>
    private (string Text, Location? Loc) ResidenceOf(Character ch)
    {
        var country = ch.Country;
        if (!ch.Alive) return ("", null);
        if (G.World.Armies.FirstOrDefault(a => a.General == ch) is { } army && army.Divisions.FirstOrDefault()?.Location is { } al)
            return ($"Commanding {army.Name} at {al.DisplayName}", al);
        State? home = ch.HomeState != null ? G.World.GetState(ch.HomeState) : null;
        if (country != null && (ch.IsRuler || ch.IsHeir || country.Regent == ch || home == null))
        {
            var cap = country.Capital;
            if (cap != null) return ($"At court in {(cap.State?.DisplayName ?? cap.DisplayName)}", cap);
        }
        if (home != null)
        {
            var l = home.Locations.FirstOrDefault(x => x.IsLand);
            return ($"Lives in {home.DisplayName}", l);
        }
        return ("", null);
    }
}
