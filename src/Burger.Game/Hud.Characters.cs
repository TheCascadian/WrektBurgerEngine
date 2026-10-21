using Burger.Core.Memories;
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The characters panel: a portrait roster on top, a dossier of the selected character below.</summary>
public sealed partial class Hud
{
    private bool _showForeignCharacters;
    /// <summary>Height the dossier needed last frame; sizes the panel and its scroll area.</summary>
    private float _charDetailH = 360;
    private readonly Dictionary<int, (long Day, string? Plain, string? Path)> _portraitPaths = new();

    private readonly record struct CharRole(string Text, Color Tint, string Tip);

    private static readonly Color RulerTint = new(232, 196, 90, 255), HeirTint = new(196, 164, 110, 255),
        GeneralTint = new(206, 92, 78, 255), MinisterTint = new(96, 152, 214, 255);

    // ---------------------------------------------------------------------------------------------- portraits
    /// <summary>The portrait file for a character now. Rules (see <c>portraits</c> in a character definition) are re-checked once a day.</summary>
    private string? PortraitPathOf(Character ch)
    {
        long day = G.World.Date.TotalDays;
        if (_portraitPaths.TryGetValue(ch.Id, out var e) && e.Day == day && e.Plain == ch.Portrait) return e.Path;
        var path = G.Characters.PortraitFor(ch);
        _portraitPaths[ch.Id] = (day, ch.Portrait, path);
        return path;
    }

    /// <summary>
    /// Draws a character's portrait (still image, gif, or video) cropped to fill <paramref name="dst"/>; characters without
    /// usable artwork get a bust in their country's colour. Returns the running clip when the portrait is animated.
    /// <paramref name="live"/> plays animation (and sound when <paramref name="sound"/>); otherwise animated portraits show a still frame.
    /// </summary>
    private Media.Clip? DrawPortrait(Character ch, Rectangle dst, bool live, bool sound)
    {
        string? path = PortraitPathOf(ch);
        Media.Clip? clip = null;
        Texture2D? tex = null;
        if (Media.IsAnimated(path))
        {
            clip = Media.Get(path);
            if (clip != null) tex = live ? clip.Use(sound) : clip.Still();
        }
        else tex = Assets.Texture(path);
        if (tex is { } t)
        {
            float da = dst.Width / dst.Height, ta = t.Width / (float)t.Height;
            var src = ta > da ? new Rectangle((t.Width - t.Height * da) / 2, 0, t.Height * da, t.Height) : new Rectangle(0, 0, t.Width, t.Width / da);
            Raylib.DrawTexturePro(t, src, dst, Vector2.Zero, 0, Color.White);
        }
        else DrawBust(ch, dst);
        return clip;
    }

    /// <summary>Fallback portrait: a head-and-shoulders silhouette on a backdrop in the colour of the character's country.</summary>
    private void DrawBust(Character ch, Rectangle r)
    {
        var baseColor = ch.Country != null ? C(ch.Country.Color) : new Color(90, 90, 100, 255);
        Gui.Gradient(r, Gui.Darker(baseColor, 0.35f), Gui.Darker(baseColor, 0.7f));
        var body = new Color(214, 204, 186, 150);
        float cx = r.X + r.Width / 2;
        float headR = r.Width * 0.2f;
        float shoulderTop = r.Y + r.Height * 0.66f;
        Raylib.DrawRectangleRec(new Rectangle(cx - r.Width * 0.36f, shoulderTop + r.Height * 0.08f, r.Width * 0.72f, r.Height * 0.26f), body);
        Raylib.DrawEllipse((int)cx, (int)(shoulderTop + r.Height * 0.08f), r.Width * 0.36f, r.Height * 0.08f, body);
        Raylib.DrawCircleV(new Vector2(cx, r.Y + r.Height * 0.38f), headR, body);
        if (ch.Female) Raylib.DrawEllipse((int)cx, (int)(r.Y + r.Height * 0.4f), headR * 1.25f, headR * 1.45f, new Color(60, 52, 48, 120));
        Raylib.DrawCircleV(new Vector2(cx, r.Y + r.Height * 0.38f), headR, body);
    }

    private void PortraitFrame(Rectangle r, Color color, float thickness = 1)
        => Raylib.DrawRectangleLinesEx(r, thickness, color);

    // ---------------------------------------------------------------------------------------------- roles
    private List<CharRole> RolesOf(Character ch, Country country)
    {
        var roles = new List<CharRole>();
        if (ch.IsRuler) roles.Add(new("Ruler", RulerTint, $"Rules {country.Name}"));
        if (ch.IsHeir) roles.Add(new("Heir", HeirTint, $"Heir to the throne of {country.Name}"));
        if (G.Politics.Enabled)
            foreach (var party in G.Politics.PartiesOf(country))
                if (G.Politics.LeaderOf(party) == ch)
                    roles.Add(new("Leader · " + G.Politics.PartyDisplayName(country, party.Key), C(G.Politics.PartyColor(party.Key)), "Leads " + G.Politics.PartyDisplayName(country, party.Key)));
        foreach (var army in G.World.Armies.Where(a => a.General == ch))
            roles.Add(new("General", GeneralTint, "Commands " + army.Name));
        if (G.Politics.CabinetEnabled)
            foreach (var pos in G.Politics.Positions)
                if (G.Politics.MinisterOf(country, pos) == ch)
                    roles.Add(new(Localisation.Get(pos.Name), MinisterTint, $"Cabinet minister: {Localisation.Get(pos.Name)}\n{Localisation.Get(pos.Desc)}"));
        if (G.Succession.Enabled && country.Regent == ch) roles.Add(new("Regent", RulerTint, $"Rules {country.Name} for {country.Ruler?.DisplayName}, who is too young"));
        if (G.Dynasties.Enabled)
        {
            var dyn = G.Dynasties;
            if (dyn.IsConcubine(ch)) roles.Add(new("Concubine", HeirTint, "Kept at court; no claim to the throne"));
            else if (dyn.Claim(ch) < 1) roles.Add(new(dyn.IsBastard(ch) ? "Bastard" : "Concubine's child", HeirTint, "Half the claim to the throne of a lawful child"));
            if (dyn.IsSick(ch) && dyn.DiseaseOf(ch) is { } ill) roles.Add(new("Ill · " + ill.Name, new Color(205, 70, 70, 255), $"{ill.Name} until {new GameDate(dyn.SickUntil(ch)).ToDisplay()}; it may kill"));
        }
        return roles;
    }

    /// <summary>The state worth flagging at a glance (health, loyalty, age), each with an explanation for its tooltip.</summary>
    private List<(string Text, Color Tint, string Tip)> ConditionsOf(Character ch, GameDate now)
    {
        var list = new List<(string, Color, string)>();
        if (ch.Health < 40) list.Add(("Frail", Gui.TooltipBad, $"Health is {ch.Health:0}/100. Risk of dying is high."));
        else if (ch.Health < 70) list.Add(("Unwell", Gui.TooltipWarning, $"Health is {ch.Health:0}/100."));
        if (ch.Loyalty < 25) list.Add(("Disloyal", Gui.TooltipBad, $"Loyalty is {ch.Loyalty:0}/100. Likely to cause trouble."));
        else if (ch.Loyalty >= 80) list.Add(("Devoted", Gui.TooltipGood, $"Loyalty is {ch.Loyalty:0}/100."));
        if (ch.Age(now) >= 70) list.Add(("Elderly", Gui.TooltipWarning, $"Aged {ch.Age(now)}."));
        return list;
    }

    // ---------------------------------------------------------------------------------------------- small widgets
    private float ChipWidth(string text, float size = 12) => Gui.Measure(text, size, FontKind.Bold).X + 14 * S;

    /// <summary>A tinted tag; returns its width.</summary>
    private float Chip(string text, float x, float y, Color tint, string? tip = null, float size = 12)
    {
        float w = ChipWidth(text, size);
        var r = new Rectangle(x, y, w, 19 * S);
        Raylib.DrawRectangleRec(r, new Color(tint.R, tint.G, tint.B, (byte)46));
        Raylib.DrawRectangleLinesEx(r, 1, new Color(tint.R, tint.G, tint.B, (byte)150));
        Gui.LabelCentered(text, r, size, Gui.Lighter(tint, 0.35f), FontKind.Bold);
        if (tip != null && Gui.Hover(r)) Gui.Tooltip(tip);
        return w;
    }

    private void MeterRow(string label, double value, Color fill, float x, float y, float w, string tip)
    {
        var row = new Rectangle(x, y, w, 18 * S);
        Gui.Label(label, x, y, 13, Gui.TextDim);
        float bx = x + 56 * S, bw = w - 56 * S - 34 * S;
        Gui.Bar(new Rectangle(bx, y + 4 * S, bw, 10 * S), (float)(value / 100.0), fill);
        string v = $"{value:0}";
        Gui.Label(v, x + w - Gui.Measure(v, 13).X, y, 13, Gui.Text);
        if (Gui.Hover(row)) Gui.Tooltip(tip);
    }

    private static void DrawSpeaker(Rectangle r, bool on, Color col)
    {
        float cx = r.X + r.Width * 0.38f, cy = r.Y + r.Height / 2, u = r.Width * 0.16f;
        Raylib.DrawRectangleRec(new Rectangle(cx - u * 1.6f, cy - u * 0.8f, u * 1.2f, u * 1.6f), col);
        Raylib.DrawTriangle(new Vector2(cx - u * 0.4f, cy - u * 0.8f), new Vector2(cx - u * 0.4f, cy + u * 0.8f), new Vector2(cx + u * 1.5f, cy + u * 2.1f), col);
        Raylib.DrawTriangle(new Vector2(cx - u * 0.4f, cy - u * 0.8f), new Vector2(cx + u * 1.5f, cy + u * 2.1f), new Vector2(cx + u * 1.5f, cy - u * 2.1f), col);
        if (on)
        {
            Raylib.DrawLineEx(new Vector2(cx + u * 2.4f, cy - u), new Vector2(cx + u * 2.4f, cy + u), 1.6f, col);
            Raylib.DrawLineEx(new Vector2(cx + u * 3.3f, cy - u * 1.7f), new Vector2(cx + u * 3.3f, cy + u * 1.7f), 1.6f, col);
        }
        else
        {
            Raylib.DrawLineEx(new Vector2(cx + u * 2.2f, cy - u), new Vector2(cx + u * 4f, cy + u), 2f, new Color(230, 90, 80, 255));
            Raylib.DrawLineEx(new Vector2(cx + u * 2.2f, cy + u), new Vector2(cx + u * 4f, cy - u), 2f, new Color(230, 90, 80, 255));
        }
    }

    /// <summary>
    /// The characters worth listing: backbench parliamentarians are left out unless they hold a role (party leader, minister,
    /// general, ruler, heir) or belong to the ruling family.
    /// </summary>
    private List<Character> NotableCharacters(Country country, GameDate now)
    {
        HashSet<Character>? seated = null;
        if (G.Politics.Enabled)
            seated = G.Politics.ParliamentOf(country).Seats.Where(s => s.Character != null).Select(s => s.Character!).ToHashSet();
        bool Notable(Character c)
        {
            if (seated == null || !seated.Contains(c)) return true;
            if (RolesOf(c, country).Count > 0) return true;
            foreach (var head in new[] { country.Ruler, country.Heir })
                if (head != null && (head.Spouse == c || head.Father == c || head.Mother == c || head.Children.Contains(c))) return true;
            return false;
        }
        return G.World.CharactersOf(country).Where(Notable)
            .OrderByDescending(c => c.IsRuler).ThenByDescending(c => c.IsHeir)
            .ThenByDescending(c => G.World.Armies.Any(a => a.General == c))
            .ThenBy(c => c.Family).ThenByDescending(c => c.Age(now)).ToList();
    }

    // ---------------------------------------------------------------------------------------------- panel
    private void DrawCharactersPanel()
    {
        var player = G.World.Player;
        var country = player == null ? Ui.SelectedCountry : _showForeignCharacters && Ui.SelectedCountry != null ? Ui.SelectedCountry : player;
        if (country == null) { Ui.Left = LeftPanel.None; return; }
        var now = G.World.Date;
        var chars = NotableCharacters(country, now);

        // Summaries first: the roster takes only the room its rows need, the dossier gets the rest.
        bool foreignButton = player != null && Ui.SelectedCountry != null && Ui.SelectedCountry != player;
        float rowStep = 50 * S;
        float screenRoom = Raylib.GetScreenHeight() - TopH - 20 * S - SubNavH;
        float rosterH = Math.Min(chars.Count * rowStep + 2 * S, Math.Max(150 * S, screenRoom * 0.34f));
        float chrome = 34 * S + (foreignButton ? 28 * S : 0) + rosterH + 22 * S;
        var r = LeftRect(chrome + _charDetailH + 12 * S);
        Gui.Panel(r, SubNavH > 0 ? country.Name : $"Characters - {country.Name}");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 34 * S, w = r.Width - 20 * S;
        if (foreignButton)
        {
            if (Gui.Button(new Rectangle(x, y, w, 24 * S), _showForeignCharacters ? "Show my characters" : $"Show characters of {Ui.SelectedCountry!.Name}", true, null, false, 14))
                _showForeignCharacters = !_showForeignCharacters;
            y += 28 * S;
        }

        if (chars.Count == 0) { Gui.Label("No known characters.", x, y + 4 * S, 15, Gui.TextDim); return; }
        if (Ui.SelectedCharacter == null || !chars.Contains(Ui.SelectedCharacter)) Ui.SelectedCharacter = chars[0];

        // ---- roster
        float top = Gui.BeginScroll("chars", new Rectangle(x, y, w, rosterH), chars.Count * rowStep);
        float ry = top;
        foreach (var c in chars)
        {
            var row = new Rectangle(x, ry, w - 8 * S, rowStep - 4 * S);
            DrawRosterRow(c, row, country, now);
            ry += rowStep;
        }
        Gui.EndScroll();
        y += rosterH + 8 * S;
        Raylib.DrawLine((int)x, (int)y, (int)(x + w), (int)y, Gui.PanelBorder);
        y += 8 * S;

        var ch = Ui.SelectedCharacter!;
        var area = new Rectangle(x, y, w, Math.Max(60 * S, r.Y + r.Height - y - 8 * S));
        float dtop = Gui.BeginScroll("chars-detail", area, _charDetailH);
        float used = DrawCharacterDossier(ch, country, x, dtop, w - 8 * S, now, player);
        Gui.EndScroll();
        _charDetailH = Math.Max(120 * S, used);
    }

    private void DrawRosterRow(Character c, Rectangle row, Country country, GameDate now)
    {
        bool selected = Ui.SelectedCharacter == c;
        var roles = RolesOf(c, country);
        string tip = c.DisplayName + $"\nAge {c.Age(now)} · Loyalty {c.Loyalty:0} · Health {c.Health:0}" +
            (roles.Count > 0 ? "\n" + string.Join(", ", roles.Select(q => q.Text)) : "");
        if (Gui.Button(row, "", true, tip, selected, 14)) { Ui.SelectedCharacter = c; OpenSheet(c); }

        var thumb = new Rectangle(row.X + 4 * S, row.Y + 3 * S, 34 * S, row.Height - 6 * S);
        DrawPortrait(c, thumb, false, false);
        PortraitFrame(thumb, roles.Count > 0 ? new Color(roles[0].Tint.R, roles[0].Tint.G, roles[0].Tint.B, (byte)220) : new Color(0, 0, 0, 200));
        SickMark(c, thumb);

        float tx = thumb.X + thumb.Width + 8 * S;
        float meterW = 54 * S;
        float textW = row.X + row.Width - tx - meterW - 12 * S;
        Gui.Label(Gui.Fit(c.DisplayName, textW, 15, FontKind.Bold), tx, row.Y + 5 * S, 15, selected ? Color.White : Gui.Text, FontKind.Bold);

        // one-row summary: the main role (coloured) and the age
        string sub = roles.Count > 0 ? roles[0].Text : "Citizen";
        Color subColor = roles.Count > 0 ? Gui.Lighter(roles[0].Tint, 0.25f) : Gui.TextDim;
        float sw = Gui.Measure(sub, 13, FontKind.Bold).X;
        Gui.Label(Gui.Fit(sub, textW, 13, FontKind.Bold), tx, row.Y + 25 * S, 13, subColor, FontKind.Bold);
        Gui.Label($"· {c.Age(now)}", tx + Math.Min(sw, textW) + 6 * S, row.Y + 25 * S, 13, Gui.TextDim);

        // loyalty and health at a glance
        float mx = row.X + row.Width - meterW - 6 * S;
        Gui.Bar(new Rectangle(mx, row.Y + 11 * S, meterW, 6 * S), (float)(c.Loyalty / 100), new Color(214, 170, 70, 255));
        Gui.Bar(new Rectangle(mx, row.Y + 24 * S, meterW, 6 * S), (float)(c.Health / 100), c.Health < 40 ? Gui.TooltipBad : new Color(104, 190, 108, 255));
        var meters = new Rectangle(mx, row.Y + 6 * S, meterW, 28 * S);
        if (Gui.Hover(meters)) Gui.Tooltip($"Loyalty {c.Loyalty:0}\nHealth {c.Health:0}");
    }

    /// <summary>Draws the selected character's dossier starting at y and returns the height used.</summary>
    private float DrawCharacterDossier(Character ch, Country country, float x, float y, float w, GameDate now, Country? player)
    {
        float start = y;
        var roles = RolesOf(ch, country);

        // ---- header: portrait (animated when it can be) and identity
        float pw = 108 * S, ph = 136 * S;
        var pr = new Rectangle(x, y, pw, ph);
        var clip = DrawPortrait(ch, pr, true, true);
        var frameTint = roles.Count > 0 ? roles[0].Tint : Gui.PanelBorder;
        PortraitFrame(new Rectangle(pr.X - 1, pr.Y - 1, pr.Width + 2, pr.Height + 2), new Color(0, 0, 0, 220), 2);
        PortraitFrame(pr, frameTint, 2 * Math.Max(1, S * 0.5f));
        SickMark(ch, pr);
        if (clip is { HasAudio: true })
        {
            var sb = new Rectangle(pr.X + pr.Width - 26 * S, pr.Y + pr.Height - 26 * S, 22 * S, 22 * S);
            bool on = Media.AudioEnabled;
            Raylib.DrawRectangleRec(sb, new Color(0, 0, 0, Gui.Hover(sb) ? 210 : 150));
            DrawSpeaker(sb, on, on ? Color.White : new Color(200, 200, 200, 255));
            if (Gui.Hover(sb)) Gui.Tooltip((on ? "Sound on" : "Sound off") + (Media.AudioReady || !on ? "" : " (no audio device)") + "\nClick to toggle");
            if (Gui.Clicked(sb))
            {
                Media.AudioEnabled = !Media.AudioEnabled;
                UserSettings.PortraitSound = Media.AudioEnabled;
                UserSettings.Save();
            }
        }

        float hx = x + pw + 12 * S, hw = w - pw - 12 * S;
        float hy = y;
        Gui.Label(Gui.Fit(ch.DisplayName, hw, 20, FontKind.Header), hx, hy, 20, Gui.Gold, FontKind.Header);
        hy += 28 * S;
        float cx = hx;
        float chipRowH = 22 * S;
        foreach (var role in roles)
        {
            float cw = ChipWidth(role.Text);
            if (cx + cw > hx + hw && cx > hx) { cx = hx; hy += chipRowH; }
            cx += Chip(role.Text, cx, hy, role.Tint, role.Tip) + 4 * S;
        }
        if (roles.Count > 0) hy += chipRowH;
        Gui.Label($"{(ch.Female ? "Female" : "Male")} · age {ch.Age(now)} · born {ch.BirthDate.ToDisplay()}", hx, hy, 13, Gui.TextDim);
        hy += 20 * S;
        MeterRow("Loyalty", ch.Loyalty, new Color(214, 170, 70, 255), hx, hy, hw, $"Loyalty {ch.Loyalty:0}/100\nHow reliably this character serves {country.Name}.");
        hy += 20 * S;
        MeterRow("Health", ch.Health, ch.Health < 40 ? Gui.TooltipBad : new Color(104, 190, 108, 255), hx, hy, hw, $"Health {ch.Health:0}/100\nLow health raises the chance of death.");
        hy += 22 * S;
        y = Math.Max(y + ph, hy) + 10 * S;

        // ---- stats as tiles
        var stats = G.Data.Defines.CharacterStats.ToList();
        if (stats.Count > 0)
        {
            float gap = 6 * S;
            int perRow = Math.Min(stats.Count, 4);
            float tw = (w - gap * (perRow - 1)) / perRow;
            float th = 54 * S;
            for (int i = 0; i < stats.Count; i++)
            {
                int col = i % perRow, rowN = i / perRow;
                var t = new Rectangle(x + col * (tw + gap), y + rowN * (th + gap), tw, th);
                DrawStatTile(ch, stats[i], t);
            }
            y += (stats.Count + perRow - 1) / perRow * (th + gap) + 4 * S;
        }

        // ---- conditions and traits: tags, details in tooltips
        var conditions = ConditionsOf(ch, now);
        if (conditions.Count > 0 || ch.Traits.Count > 0)
        {
            float tx = x;
            foreach (var (text, tint, tip) in conditions)
            {
                float cw = ChipWidth(text, 13);
                if (tx + cw > x + w && tx > x) { tx = x; y += 24 * S; }
                tx += Chip(text, tx, y, tint, tip, 13) + 4 * S;
            }
            foreach (var t in ch.Traits)
            {
                string name = Localisation.Get(t.Name);
                float cw = ChipWidth(name, 13);
                if (tx + cw > x + w && tx > x) { tx = x; y += 24 * S; }
                string tip = $"{name}\n{Localisation.Get(t.Description)}" +
                    (t.Modifiers.Count > 0 ? "\n" + string.Join("\n", t.Modifiers.Select(m => $"{Localisation.GetOrPretty(m.Key)}: {m.Value:+0.##;-0.##}")) : "");
                tx += Chip(name, tx, y, new Color(214, 200, 170, 255), tip, 13) + 4 * S;
            }
            y += 30 * S;
        }

        // ---- relatives: click to open their dossier
        var relatives = new List<(string Tag, Character Who)>();
        if (ch.Spouse != null) relatives.Add(("Spouse", ch.Spouse));
        if (ch.Father != null) relatives.Add(("Father", ch.Father));
        if (ch.Mother != null) relatives.Add(("Mother", ch.Mother));
        foreach (var kid in ch.Children.Take(6)) relatives.Add(("Child", kid));
        if (relatives.Count > 0)
        {
            Gui.Section("Family", x, y, w);
            y += 26 * S;
            float tx = x;
            foreach (var (tag, who) in relatives)
            {
                string label = $"{tag}: {(string.IsNullOrEmpty(who.FirstName) ? who.DisplayName : Localisation.Get(who.FirstName))}";
                float bw = Gui.Measure(label, 13, FontKind.Bold).X + 18 * S;
                if (tx + bw > x + w && tx > x) { tx = x; y += 28 * S; }
                bool inReach = who.Alive && who.Country == country;
                string tip = who.DisplayName + (who.Alive ? $", age {who.Age(now)}" : " (dead)") + (who.Alive && !inReach ? $"\nLives in {who.Country?.Name ?? "exile"}" : "");
                if (Gui.Button(new Rectangle(tx, y, bw, 24 * S), label, who.Alive, tip, false, 13)) { Ui.SelectedCharacter = who; OpenSheet(who); }
                tx += bw + 4 * S;
            }
            y += 32 * S;
        }

        y += DrawModifierList(ch, x, y, w);

        // ---- interactions (data-driven)
        var actions = player == null ? new List<Burger.Core.Model.ActionDef>() : G.Diplomacy.ActionsFor(ActionTarget.Character).Where(a => G.Diplomacy.IsVisible(a, player, ch)).ToList();
        if (actions.Count > 0)
        {
            Gui.Section("Interactions", x, y, w);
            y += 28 * S;
            float gap = 6 * S, bw = (w - gap) / 2;
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var reqs = G.Diplomacy.Requirements(a, player!, ch);
                bool can = reqs.All(q => q.Ok);
                string tip = Localisation.Get(a.Desc);
                if (reqs.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text));
                var effects = G.Script.Describe(a.OnAccept, G.Diplomacy.ActionContext(a, player!, ch));
                if (effects.Count > 0) tip += "\n\nEffects:\n" + string.Join("\n", effects);
                var br = new Rectangle(x + (i % 2) * (bw + gap), y + (i / 2) * 30 * S, bw, 26 * S);
                if (Gui.Button(br, Localisation.Get(a.Name), can, tip, false, 14))
                    Ui.SetStatus($"{Localisation.Get(a.Name)}: {G.Diplomacy.Use(a, player!, ch)}");
            }
            y += (actions.Count + 1) / 2 * 30 * S;
        }
        y += DrawRememberedActs(ch, "Remembered acts", x, y, w);
        y += DrawRememberedActs(country, $"{country.Name} is remembered for", x, y, w);
        return y - start + 6 * S;
    }

    /// <summary>The most weighty acts a country, character, party or pop group is remembered for (the Memory), strongest first; nothing is drawn while it is empty.</summary>
    private float DrawRememberedActs(Subject who, string title, float x, float y, float w)
    {
        return DrawRememberedActs(G.Memory.Of(who).OrderByDescending(G.Memory.Current).Take(5).ToList(), title, x, y, w);
    }

    /// <summary>Colour that marks everything the Memory shows, so remembered acts stand apart from the rest of a panel.</summary>
    private static readonly Color MemoryAccent = new(176, 140, 224, 255);

    /// <summary>Height of a memory block with <paramref name="rows"/> acts, with the gap after it (0 when there are none).</summary>
    private float MemoryBlockHeight(int rows) => rows <= 0 ? 0 : (36 + 22 * rows + 4) * S;

    /// <summary>
    /// A boxed, tinted block with a violet edge: the title, one row per remembered act (good or bad marker, what was done and to whom,
    /// and a bar for how heavily it still counts). Returns the height used, including the gap after it; nothing when there are no acts.
    /// </summary>
    private float DrawRememberedActs(List<MemoryEntry> acts, string title, float x, float y, float w)
    {
        if (acts.Count == 0) return 0;
        float h = MemoryBlockHeight(acts.Count) - 4 * S;
        var box = new Rectangle(x, y, w, h);
        Gui.Card(box, new Color(46, 38, 62, 255));
        Raylib.DrawRectangleRec(new Rectangle(x, y, 4 * S, h), MemoryAccent);
        float tagW = Gui.Measure("MEMORY", 11, FontKind.Bold).X;
        Gui.Label("MEMORY", x + w - tagW - 10 * S, y + 8 * S, 11, MemoryAccent, FontKind.Bold);
        Gui.Label(Gui.Fit(title, w - tagW - 34 * S, 14, FontKind.Bold), x + 14 * S, y + 6 * S, 14, Gui.Text, FontKind.Bold);
        float ry = y + 30 * S;
        foreach (var e in acts)
        {
            var kind = G.Memory.Kind(e.Kind);
            var tone = kind?.Good == true ? Gui.Good : Gui.Bad;
            var row = new Rectangle(x + 8 * S, ry, w - 16 * S, 22 * S);
            Raylib.DrawRectangleRec(new Rectangle(x + 14 * S, ry + 8 * S, 6 * S, 6 * S), tone);
            float barW = 46 * S;
            Gui.Label(Gui.Fit(G.Memory.Describe(e), w - barW - 52 * S, 13), x + 26 * S, ry + 3 * S, 13, tone);
            Gui.Bar(new Rectangle(x + w - barW - 12 * S, ry + 8 * S, barW, 6 * S), (float)Math.Clamp(G.Memory.Current(e) / 5.0, 0.08, 1), tone);
            if (Gui.Hover(row))
                Gui.Tooltip(G.Memory.Describe(e) + (e.Note.Length > 0 ? "\n" + e.Note : "") + "\n" + (e.DecayPerYear > 0 ? "Fades with time." : "Never fades."));
            ry += 22 * S;
        }
        return h + 4 * S;
    }

    private void DrawStatTile(Character ch, string stat, Rectangle t)
    {
        double total = ch.GetStat(stat), baseValue = ch.Stats.GetValueOrDefault(stat);
        string label = Localisation.GetOrPretty(stat);
        Gui.Card(t, new Color(34, 36, 42, 255));
        bool hasIcon = Gui.Icon(stat, new Rectangle(t.X + 5 * S, t.Y + 5 * S, 16 * S, 16 * S));
        Gui.Label(Gui.Fit(label, t.Width - (hasIcon ? 28 : 10) * S, 12, FontKind.Bold), t.X + (hasIcon ? 25 : 6) * S, t.Y + 5 * S, 12, Gui.TextDim, FontKind.Bold);
        string v = $"{total:0}";
        var tone = total >= 8 ? Gui.TooltipGood : total <= 3 ? Gui.TooltipBad : Gui.Gold;
        float vw = Gui.Measure(v, 22, FontKind.Header).X;
        Gui.Label(v, t.X + (t.Width - vw) / 2, t.Y + 19 * S, 22, tone, FontKind.Header);
        Gui.Bar(new Rectangle(t.X + 6 * S, t.Y + t.Height - 9 * S, t.Width - 12 * S, 4 * S), (float)(total / 10.0), tone);
        if (Gui.Hover(t))
        {
            var lines = new List<string> { $"{label}: {total:0.#} of 10", $"Base {baseValue:0.#}" };
            foreach (var tr in ch.Traits)
                if (Math.Abs(tr.Mod(stat)) > 0.001) lines.Add($"{Localisation.Get(tr.Name)}: {tr.Mod(stat):+0.##;-0.##}");
            Gui.Tooltip(string.Join("\n", lines));
        }
    }
}
