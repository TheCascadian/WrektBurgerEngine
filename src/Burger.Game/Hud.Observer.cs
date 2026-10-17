using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Country panel tabs (Overview / People / Factions), flag chips and the observer top bar.</summary>
public sealed partial class Hud
{
    private int _countryTab;
    private string? _countryTabTag;

    private static readonly Color AllyBlue = new(110, 160, 245, 255);
    private static readonly Color SubjectBlue = new(140, 200, 235, 255);

    /// <summary>Small clickable flag with the country name as its tooltip. Returns the width used.</summary>
    private float FlagChip(Country c, float x, float y, float h, Color? frame = null, string? extraTip = null)
    {
        float w = h * 1.5f;
        var rect = new Rectangle(x, y, w, h);
        Gui.Flag(c, rect);
        Raylib.DrawRectangleLinesEx(rect, 1, frame ?? new Color(0, 0, 0, 200));
        bool hover = Gui.Hover(rect);
        if (hover)
        {
            Raylib.DrawRectangleLinesEx(rect, 2, Color.White);
            Gui.Tooltip(c.Name + (extraTip != null ? "\n" + extraTip : "") + "\nClick to open");
        }
        if (Gui.Clicked(rect)) { Ui.SelectedCountry = c; Ui.Left = LeftPanel.Country; }
        return w + 4 * S;
    }

    /// <summary>A row of flag chips wrapping at width w. Returns the height used.</summary>
    private float FlagRow(IEnumerable<Country> list, float x, float y, float w, Color frame, Func<Country, string?>? tip = null)
    {
        float cx = x, ch = 16 * S, row = ch + 4 * S, used = row;
        foreach (var c in list)
        {
            if (cx + ch * 1.5f > x + w && cx > x) { cx = x; used += row; }
            cx += FlagChip(c, cx, y + used - row, ch, frame, tip?.Invoke(c));
        }
        return used;
    }

    /// <summary>A small pictogram (tooltip icon) followed by a value; hover shows the tip. Returns the width used.</summary>
    private float IconStat(string icon, string value, string tip, float x, float y, Color? col = null)
    {
        float s = 16 * S, vw = Gui.Measure(value, 14, FontKind.Bold).X;
        var rect = new Rectangle(x, y, s + 4 * S + vw, s);
        if (!Gui.Icon(icon, new Rectangle(x, y, s, s))) Raylib.DrawRectangleRec(new Rectangle(x + 3 * S, y + 3 * S, s - 6 * S, s - 6 * S), Gui.TextDim);
        Gui.Label(value, x + s + 4 * S, y, 14, col ?? Gui.Text, FontKind.Bold);
        if (Gui.Hover(rect)) Gui.Tooltip(tip);
        return rect.Width + 10 * S;
    }

    private readonly Dictionary<string, float> _fitH = new();

    /// <summary>A left panel only as tall as its content needed on the previous frame.</summary>
    private Rectangle FitRect(string key) => LeftRect(_fitH.GetValueOrDefault(key, 2000));

    /// <summary>Remember how far down the content reached (absolute y) so the panel can shrink to it next frame.</summary>
    private void FitEnd(string key, Rectangle r, float contentBottom) => _fitH[key] = contentBottom - r.Y + 12 * S;

    private readonly Dictionary<string, string> _chipSel = new();

    /// <summary>A wrapping row of filter chips with counts; the choice is remembered under id. Returns the height used.</summary>
    private float FilterChips(string id, IList<(string Name, int Count)> options, float x, float y, float w, out string selected)
    {
        string sel0 = _chipSel.GetValueOrDefault(id, "");
        selected = sel0;
        if (options.Count > 0 && !options.Any(opt => opt.Name == sel0)) selected = options[0].Name;
        float cx = x, row = 24 * S, used = row;
        foreach (var (name, count) in options)
        {
            string label = $"{name} {count}";
            float bw = Gui.Measure(label, 13, FontKind.Bold).X + 16 * S;
            if (cx + bw > x + w && cx > x) { cx = x; used += row; }
            if (Gui.Button(new Rectangle(cx, y + used - row, bw, 22 * S), label, true, null, name == selected, 13)) { _chipSel[id] = name; selected = name; }
            cx += bw + 3 * S;
        }
        return used + 4 * S;
    }

    /// <summary>A small dark tag with a coloured value, e.g. "HP 20". Returns the width used.</summary>
    private float StatTag(string text, float x, float y, Color col)
    {
        float w = Gui.Measure(text, 12, FontKind.Bold).X + 10 * S, h = 17 * S;
        Raylib.DrawRectangleRec(new Rectangle(x, y, w, h), new Color(18, 20, 24, 220));
        Gui.Label(text, x + 5 * S, y + 1 * S, 12, col, FontKind.Bold);
        return w + 3 * S;
    }

    /// <summary>A pictogram followed by text (no hover). Returns the width used.</summary>
    private float IconLabel(string icon, string text, float x, float y, int size, Color? col = null, FontKind font = FontKind.Body)
    {
        float s = (size + 2) * S;
        if (!Gui.Icon(icon, new Rectangle(x, y, s, s))) Raylib.DrawRectangleRec(new Rectangle(x + 3 * S, y + 3 * S, s - 6 * S, s - 6 * S), Gui.TextDim);
        Gui.Label(text, x + s + 4 * S, y, size, col ?? Gui.Text, font);
        return s + 4 * S + Gui.Measure(text, size, font).X;
    }

    /// <summary>A section heading with a pictogram in front of it.</summary>
    private void IconSection(string icon, string text, float x, float y, float w)
    {
        float s = 18 * S;
        Gui.Icon(icon, new Rectangle(x, y, s, s));
        Gui.Section(text, x + s + 4 * S, y, w - s - 4 * S);
    }

    /// <summary>A button showing a pictogram and a short label centred together.</summary>
    private bool IconButton(Rectangle r, string icon, string label, bool enabled, string? tip, bool active, int size = 13)
    {
        bool hit = Gui.Button(r, "", enabled, tip, active, size);
        float iw = Math.Min(r.Height - 6 * S, 16 * S), lw = label.Length > 0 ? Gui.Measure(label, size, FontKind.Bold).X + 4 * S : 0;
        float sx = r.X + (r.Width - lw - iw) / 2;
        Gui.Icon(icon, new Rectangle(sx, r.Y + (r.Height - iw) / 2, iw, iw), enabled ? null : new Color(255, 255, 255, 110));
        if (label.Length > 0) Gui.Label(label, sx + iw + 4 * S, r.Y + (r.Height - Gui.FontSize(size)) / 2, size, enabled ? (active ? Color.White : Gui.Text) : Gui.TextDim, FontKind.Bold);
        return hit;
    }

    /// <summary>A narrow coloured stripe on the left edge of a card that tells its state at a glance.</summary>
    private void Stripe(Rectangle box, Color col) => Raylib.DrawRectangleRec(new Rectangle(box.X, box.Y, 3 * S, box.Height), col);

    private float DrawCountryTabs(Country c, float x, float y, float w)
    {
        if (_countryTabTag != c.Tag) { _countryTabTag = c.Tag; _countryTab = 0; }
        var tabs = new List<(string Label, string Icon, string Tip)> { ("Overview", "information", "Government, economy and relations") };
        tabs.Add(("People", "characters", "Rulers, ministers, generals and everyone else of this country"));
        if (G.Politics.Enabled) tabs.Add(("Factions", "politics", "Parties and their factions as a flowchart"));
        if (ScRelationsOn) tabs.Add(("Standing", "diplomacy", "Power rank, reputation, rivals, influence, leagues and the alliance faction"));
        float bw = (w - (tabs.Count - 1) * 4 * S) / tabs.Count;
        for (int i = 0; i < tabs.Count; i++)
        {
            var r = new Rectangle(x + i * (bw + 4 * S), y, bw, 26 * S);
            if (Gui.Button(r, "", true, tabs[i].Tip, _countryTab == i, 14)) _countryTab = i;
            float lw = Gui.Measure(tabs[i].Label, 14, FontKind.Bold).X, iw = 16 * S;
            float sx = r.X + (bw - lw - iw - 4 * S) / 2;
            Gui.Icon(tabs[i].Icon, new Rectangle(sx, r.Y + 5 * S, iw, iw));
            Gui.Label(tabs[i].Label, sx + iw + 4 * S, r.Y + 4 * S, 14, _countryTab == i ? Color.White : Gui.Text, FontKind.Bold);
        }
        if (_countryTab >= tabs.Count) _countryTab = 0;
        return y + 32 * S;
    }

    // ------------------------------------------------------------------------------------------ people
    private void DrawCountryPeople(Country c, Rectangle panel, float x, float y, float w)
    {
        var now = G.World.Date;
        var chars = NotableCharacters(c, now);
        if (chars.Count == 0) { Gui.Label("No known characters.", x, y, 15, Gui.TextDim); return; }
        if (Ui.SelectedCharacter == null || !chars.Contains(Ui.SelectedCharacter)) Ui.SelectedCharacter = chars[0];

        float rowStep = 50 * S;
        float room = panel.Y + panel.Height - y - 8 * S;
        float rosterH = Math.Min(chars.Count * rowStep + 2 * S, Math.Max(140 * S, room * 0.4f));
        float top = Gui.BeginScroll("obs-chars", new Rectangle(x, y, w, rosterH), chars.Count * rowStep);
        float ry = top;
        foreach (var ch in chars)
        {
            DrawRosterRow(ch, new Rectangle(x, ry, w - 8 * S, rowStep - 4 * S), c, now);
            ry += rowStep;
        }
        Gui.EndScroll();
        y += rosterH + 6 * S;
        Raylib.DrawLine((int)x, (int)y, (int)(x + w), (int)y, Gui.PanelBorder);
        y += 6 * S;
        var area = new Rectangle(x, y, w, Math.Max(60 * S, panel.Y + panel.Height - y - 8 * S));
        float dtop = Gui.BeginScroll("obs-dossier", area, _obsDossierH);
        float used = DrawCharacterDossier(Ui.SelectedCharacter!, c, x, dtop, w - 8 * S, now, G.World.Player);
        Gui.EndScroll();
        _obsDossierH = Math.Max(120 * S, used);
    }
    private float _obsDossierH = 400;

    // ---------------------------------------------------------------------------------------- factions
    private static Color Heat(double v) => v >= 60 ? new Color(104, 190, 108, 255) : v >= 35 ? new Color(214, 170, 70, 255) : new Color(214, 84, 70, 255);

    private void DrawCountryFactions(Country c, Rectangle panel, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var parties = P.PartiesOf(c).OrderByDescending(q => p.PartyPopularity.GetValueOrDefault(q.Key)).ToList();
        if (parties.Count == 0) { Gui.Label("No parties are modelled for this country.", x, y, 14, Gui.TextDim); return; }

        // flowchart content height: root + per party node + faction nodes
        float rootH = 40 * S, partyH = 38 * S, facH = 30 * S, gap = 8 * S;
        float content = rootH + gap + 16 * S;
        foreach (var party in parties) content += partyH + P.FactionsOf(party).Count() * (facH + 4 * S) + gap;

        var area = new Rectangle(x, y, w, panel.Y + panel.Height - y - 8 * S);
        float cy = Gui.BeginScroll("obs-factions", area, content);
        float bw = w - 10 * S;
        var line = new Color(120, 118, 110, 255);

        // root: the government
        var root = new Rectangle(x, cy, bw, rootH);
        Raylib.DrawRectangleRec(root, Gui.PanelInner);
        Raylib.DrawRectangleLinesEx(root, 1, Gui.Gold);
        Gui.Flag(c, new Rectangle(root.X + 5 * S, root.Y + 6 * S, 40 * S, 27 * S));
        double coh = P.GovernmentCohesion(c);
        Gui.Label(Gui.Fit(c.Name + " government", bw - 110 * S, 15, FontKind.Bold), root.X + 52 * S, root.Y + 4 * S, 15, Gui.Text, FontKind.Bold);
        Gui.Label(Gui.Fit(P.PartyName(c, p.RulingOrganization), bw - 110 * S, 13), root.X + 52 * S, root.Y + 22 * S, 13, Gui.TextDim);
        Gui.Bar(new Rectangle(root.X + bw - 52 * S, root.Y + 16 * S, 44 * S, 8 * S), (float)(coh / 100), Heat(coh));
        if (Gui.Hover(root)) Gui.Tooltip($"Government cohesion {coh:0}/100\nHow united the governing parties' factions are. Low cohesion costs stability and law votes.\nGoverning: {string.Join(", ", p.GovernmentOrganizations.Select(k => P.PartyDisplayName(c, k)))}");

        float trunkX = x + 10 * S;
        float yy = cy + rootH;
        float trunkTop = yy;
        yy += gap;
        float lastPartyY = yy;
        foreach (var party in parties)
        {
            bool gov = p.GovernmentOrganizations.Contains(party.Key, StringComparer.OrdinalIgnoreCase);
            double pop = p.PartyPopularity.GetValueOrDefault(party.Key);
            var col = C(P.PartyColor(party.Key));
            var node = new Rectangle(x + 24 * S, yy, bw - 24 * S, partyH);
            lastPartyY = yy + partyH / 2;
            Raylib.DrawLineEx(new Vector2(trunkX, lastPartyY), new Vector2(node.X, lastPartyY), 1.5f, line);
            Raylib.DrawRectangleRec(node, Gui.PanelInner);
            Raylib.DrawRectangleRec(new Rectangle(node.X, node.Y, 4 * S, node.Height), col);
            Raylib.DrawRectangleLinesEx(node, gov ? 2 : 1, gov ? Gui.Gold : Gui.PanelBorder);
            Gui.Image(Assets.PartyFlag(party.Key, P.PartyFlagPath(party.Key), P.PartyColor(party.Key)), new Rectangle(node.X + 9 * S, node.Y + 6 * S, 39 * S, 26 * S));
            float tx = node.X + 54 * S;
            Gui.Label(Gui.Fit(party.DisplayName, node.Width - 54 * S - 70 * S, 14, FontKind.Bold), tx, node.Y + 3 * S, 14, Gui.Text, FontKind.Bold);
            float ix = tx;
            ix += IconStat("population", $"{pop:P0}", "Share of the vote (popular support)", ix, node.Y + 20 * S);
            if (P.PartySeats(party) is { } seats) ix += IconStat("politics", seats.ToString(), "Seats in the legislature", ix, node.Y + 20 * S);
            double pc = P.PartyCohesion(party);
            var cb = new Rectangle(node.X + node.Width - 56 * S, node.Y + 24 * S, 48 * S, 7 * S);
            Gui.Bar(cb, (float)(pc / 100), Heat(pc));
            if (gov) Gui.Label("GOV", node.X + node.Width - 36 * S, node.Y + 4 * S, 11, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(node)) Gui.Tooltip($"{party.DisplayName}\n{(gov ? "In government" : "Opposition")}\nSupport {pop:P1}\nCohesion {pc:0}/100 (bar)");
            yy += partyH;

            var facs = P.FactionsOf(party).OrderByDescending(f => P.FactionInfluence(f)).ToList();
            float fx = node.X + 18 * S, spineX = node.X + 8 * S;
            float spineTop = yy;
            float lastFy = yy;
            foreach (var f in facs)
            {
                yy += 4 * S;
                var fr = new Rectangle(fx, yy, node.Width - 18 * S, facH);
                lastFy = yy + facH / 2;
                Raylib.DrawLineEx(new Vector2(spineX, lastFy), new Vector2(fr.X, lastFy), 1, line);
                double loy = P.FactionLoyalty(f), gr = P.FactionGrievance(f), inf = P.FactionInfluence(f);
                Raylib.DrawRectangleRec(fr, Gui.PanelBg);
                Raylib.DrawRectangleLinesEx(fr, 1, Heat(loy));
                // influence as a filled strip along the bottom
                Raylib.DrawRectangleRec(new Rectangle(fr.X, fr.Y + fr.Height - 3 * S, fr.Width * (float)Math.Clamp(inf, 0, 1), 3 * S), col);
                Gui.Label(Gui.Fit(f.DisplayName, fr.Width - 96 * S, 13), fr.X + 6 * S, fr.Y + 3 * S, 13, Gui.Text);
                float ex = fr.X + fr.Width - 6 * S;
                string pct = $"{inf:P0}";
                ex -= Gui.Measure(pct, 13, FontKind.Bold).X;
                Gui.Label(pct, ex, fr.Y + 3 * S, 13, Gui.Text, FontKind.Bold);
                ex -= 20 * S;
                Gui.Icon(gr >= 25 ? "rebellion" : "legitimacy", new Rectangle(ex, fr.Y + 4 * S, 16 * S, 16 * S), gr >= 25 ? Gui.Bad : Heat(loy));
                if (Gui.Hover(fr))
                {
                    var (target, reasons) = P.LoyaltyOutlook(c, f);
                    Gui.Tooltip($"{f.DisplayName} ({party.DisplayName})\nInfluence {inf:P0} of the party\nLoyalty {loy:0} (heading to {target:0})\nGrievance {gr:0}\n" + string.Join("\n", reasons.Take(5)) +
                        (P.FactionLikes(f).Count > 0 ? "\nLikes: " + string.Join(", ", P.FactionLikes(f)) : "") +
                        (P.FactionDislikes(f).Count > 0 ? "\nDislikes: " + string.Join(", ", P.FactionDislikes(f)) : ""));
                }
                yy += facH;
            }
            if (facs.Count > 0) Raylib.DrawLineEx(new Vector2(spineX, spineTop), new Vector2(spineX, lastFy), 1, line);
            yy += gap;
        }
        Raylib.DrawLineEx(new Vector2(trunkX, trunkTop), new Vector2(trunkX, lastPartyY), 1.5f, line);
        Gui.EndScroll();
    }

    // ---------------------------------------------------------------------------------------- top bar
    private string ObserveUiCommand()
    {
        if (G.World.Player != null) return "observe_ui only works in observer mode (run 'observe' first)";
        ToggleObserveUi();
        return Ui.ObserveUi ? "observe_ui on" : "observe_ui off";
    }

    private void ToggleObserveUi()
    {
        Ui.ObserveUi = !Ui.ObserveUi;
        if (Ui.ObserveUi) Ui.SetStatus("observe_ui on: select a country, then press O for its Politics panel");
        else if (Ui.Left == LeftPanel.Politics) CloseLeft();
    }

    /// <summary>Observer top bar: world summary and flag shortcuts to the biggest countries. Returns the new x.</summary>
    private float DrawObserverTopBar(float x, float limit)
    {
        Gui.LabelShadow("Observer", x, 10 * S, 20, Gui.Gold, FontKind.Header);
        x += Gui.Measure("Observer", 20, FontKind.Header).X + 14 * S;
        var live = G.World.Countries.Where(c => c.Exists).ToList();
        int wars = G.World.Wars.Count;
        float iy = 15 * S;
        x += IconStat("population", live.Count.ToString(), $"{live.Count} countries exist", x, iy);
        x += IconStat("combat", wars.ToString(), $"{wars} war(s) in the world", x, iy, wars > 0 ? Gui.Bad : Gui.TextDim);
        x += 6 * S;
        var ib = new Rectangle(x, 11 * S, 82 * S, 24 * S);
        if (Gui.Button(ib, "Inspect", true, "observe_ui (F7): the Politics panel (O) shows the selected country's live values, read-only. Click a flag or a state to change country.", Ui.ObserveUi, 14))
            ToggleObserveUi();
        x += ib.Width + 4 * S;
        if (Ui.ObserveUi)
        {
            if (G.Politics.Enabled)
            {
                var pb = new Rectangle(x, 11 * S, 78 * S, 24 * S);
                if (Gui.Button(pb, "Politics", true, "Selected country's politics, parliament and population moods (O)", Ui.Left == LeftPanel.Politics, 14)) ToggleLeft(LeftPanel.Politics);
                x += pb.Width + 4 * S;
            }
            if (G.Population.Enabled)
            {
                var ob = new Rectangle(x, 11 * S, 92 * S, 24 * S);
                if (Gui.Button(ob, "Population", true, "Selected country's full population panel: cultures, religions, colonies (U)", Ui.Left == LeftPanel.Population, 14)) ToggleLeft(LeftPanel.Population);
                x += ob.Width + 4 * S;
            }
        }
        x += 4 * S;
        foreach (var c in live.OrderByDescending(c => c.OwnedStates.Count).Take(12))
        {
            if (x + 40 * S > limit) break;
            x += FlagChip(c, x, 11 * S, 24 * S, Ui.SelectedCountry == c ? Gui.Gold : null, $"{c.OwnedStates.Count} states");
        }
        return x;
    }
}
