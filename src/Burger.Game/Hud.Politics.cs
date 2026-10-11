using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Politics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The politics menu: government and parliament, laws, cabinet and political movements (politics features).</summary>
public sealed partial class Hud
{
    public int PoliticsTab;
    private string? _cabinetPick;

    // Preserve parliament seat hover through the small gaps between rendered dots.
    // This is scoped to a country and is cleared as soon as the pointer leaves the parliament.
    private string? _parliamentHoverCountryTag;
    private string? _parliamentHoverPartyKey;
    private string? _politicsSelectedPartyCountry;
    private string? _politicsSelectedPartyKey;
    private string? _politicsSelectedUnitKey;


    /// <summary>"Manpower +50%", "Political Power Gain +0.25" ... for modifier tooltips.</summary>
    public static string ModifierText(string key, double v)
    {
        bool points = key.EndsWith("_drift", StringComparison.OrdinalIgnoreCase) || key is "political_power_gain" or "stability_target" or "opinion" or "relations";
        string val = points ? (v >= 0 ? "+" : "") + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            : (v >= 0 ? "+" : "") + (v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
        string name = key.EndsWith("_drift", StringComparison.OrdinalIgnoreCase)
            ? $"{Localisation.GetOrPretty(key[..^6])} popularity per month"
            : Localisation.GetOrPretty(key);
        return $"{name} {val}";
    }

    private static string ModifierLines(Dictionary<string, double> m)
        => m.Count == 0 ? "No effects" : string.Join("\n", m.Select(kv => ModifierText(kv.Key, kv.Value)));
    /// <summary>Draws gfx/interface/politics/&lt;kind&gt;/&lt;key&gt;.png (kind: parties, factions or branches) when the game ships one.</summary>
    private static bool OrgIcon(string kind, string key, Rectangle dst)
    {
        if (Assets.Texture($"gfx/interface/politics/{kind}/{key}.png") is not { } tex) return false;
        Gui.Image(tex, dst);
        return true;
    }

    private void PoliticsSection(string text, string icon, float x, float y, float w)
    {
        float offset = Gui.Icon(icon, new Rectangle(x, y + 1 * S, 18 * S, 18 * S)) ? 23 * S : 0;
        Gui.Section(Gui.Fit(text, Math.Max(1, w - offset), 17, FontKind.Header), x + offset, y, w - offset);
    }

    private void DrawPoliticsPanel()
    {
        var player = G.World.Player;
        var P = G.Politics;
        if (player == null || !P.Enabled) { CloseLeft(); return; }
        // Every tab sizes to its content; LeftRect caps it to the available screen height.
        var r = LeftRect(PoliticsPanelHeight(player));
        r.Width = 470 * S;
        Gui.Panel(r, "Politics");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 6 * S, w = r.Width - 24 * S;
        var tabs = new List<(string Name, string Label, int Id, string Icon)>
        {
            ("Overview", "Home", 0, "politics_overview"), ("Parties", "Orgs", 4, "politics_organization"),
        };
        if (P.LawsEnabled) tabs.Add(("Laws", "Laws", 1, "politics_law"));
        if (P.CabinetEnabled) tabs.Add(("Cabinet", "Cab", 2, "cabinet"));
        if (P.CivilWarsEnabled) tabs.Add(("Movements", "Move", 3, "movement"));
        tabs.Add(("Effects", "FX", 6, "effects"));
        float tabGap = 4 * S, tabW = (w - (tabs.Count - 1) * tabGap) / tabs.Count, tabH = 32 * S;
        for (int i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var tr = new Rectangle(x + i * (tabW + tabGap), y, tabW, tabH);
            bool hasIcon = Gui.HasIcon(tab.Icon);
            if (Gui.Button(tr, "", true, null, PoliticsTab == tab.Id, 10)) PoliticsTab = tab.Id;
            float textX = tr.X + 3 * S;
            float textW = tr.Width - 6 * S;
            if (hasIcon)
            {
                float iconSize = Math.Min(19 * S, tabH - 6 * S);
                Gui.Icon(tab.Icon, new Rectangle(tr.X + 4 * S, tr.Y + (tr.Height - iconSize) / 2, iconSize, iconSize));
                textX += iconSize + 5 * S;
                textW -= iconSize + 5 * S;
            }
            Gui.LabelCentered(Gui.Fit(tab.Label, textW, 14, FontKind.Bold), new Rectangle(textX, tr.Y, textW, tr.Height), 14,
                PoliticsTab == tab.Id ? Color.White : Gui.Text, FontKind.Bold);
            if (Gui.Hover(tr)) Gui.Tooltip(tab.Name);
        }
        y += 37 * S;
        switch (PoliticsTab)
        {
            case 1: DrawLawsTab(player, r, x, y, w); break;
            case 2: DrawCabinetTab(player, r, x, y, w); break;
            case 3: DrawMovementsTab(player, r, x, y, w); break;
            case 4: DrawPartiesTab(player, r, x, y, w); break;
            case 6: DrawEffectsTab(player, r, x, y, w); break;
            default: DrawGovernmentTab(player, r, x, y, w); break;
        }
    }

    private const string PoliticsMovementTooltip = "Excluded parties can form movements as support grows. Low stability speeds them up. At 100%, they revolt; without armies, this can become a coup. A shut-out majority may also trigger civil war.";
    private float PoliticsPanelHeight(Country c)
    {
        float w = (470 - 24) * S;
        float body = PoliticsTab switch
        {
            0 => PoliticsOverviewContentHeight(c, w),
            1 => 29 * S + Math.Max(24 * S, G.Politics.LawGroups.Where(g => G.Politics.IsGroupVisible(c, g)).Sum(g => LawGroupHeight(c, g, w))) + 12 * S,
            2 => CabinetPanelBodyHeight(c),
            3 => MovementsPanelBodyHeight(c, w),
            4 => PartiesPanelBodyHeight(c),
            6 => EffectsPanelBodyHeight(c),
            _ => 220 * S,
        };
        return Gui.HeaderHeight + 43 * S + body;
    }
    private float PartiesPanelBodyHeight(Country c)
    {
        int count = G.Politics.PartyKeysOf(c).Count();
        if (count == 0) return 45 * S;
        float list = Math.Max(90 * S, Math.Min(count, 4) * 45 * S);
        return 29 * S + 30 * S + list + 8 * S + 212 * S + 8 * S;
    }
    private float CabinetPanelBodyHeight(Country c)
    {
        var P = G.Politics;
        float rows = P.Positions.Count * 74 * S;
        if (_cabinetPick is { } pick && P.Positions.FirstOrDefault(p => p.Key == pick) is { } position)
            rows += P.Candidates(c, position).Take(5).Count() * 36 * S + 8 * S;
        return 29 * S + rows + 12 * S;
    }
    private float MovementsPanelBodyHeight(Country c, float w)
    {
        int count = G.Politics.MovementsOf(c).Count();
        return 29 * S + (count == 0 ? 26 * S : count * 122 * S) + 12 * S;
    }
    private float EffectsPanelBodyHeight(Country c)
    {
        var modifiers = G.Politics.ModifierSources(c).Where(m => Math.Abs(m.Value) > 1e-6)
            .GroupBy(m => m.Source, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Source: g.Key, Values: g.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => (Key: v.Key, Value: v.Sum(t => t.Value)))
                .Where(v => Math.Abs(v.Value) > 1e-6).ToList()))
            .Where(g => g.Values.Count > 0).ToList();
        int totalCount = modifiers.SelectMany(m => m.Values).Select(m => m.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int sourceCount = modifiers.Count;
        int visible = Math.Min(6, totalCount);
        float net = totalCount == 0 ? 24 * S : visible * 34 * S + (totalCount > visible ? 26 * S : 6 * S);
        float sources = sourceCount == 0 ? 24 * S : sourceCount * 46 * S;
        return 57 * S + 24 * S + net + 24 * S + sources + 15 * S;
    }
    private string PoliticsSourceIcon(string source)
    {
        if (source.Contains("core", StringComparison.OrdinalIgnoreCase) || source.Contains("foreign land", StringComparison.OrdinalIgnoreCase)) return "core_territory";
        if (source.Contains("volunteer", StringComparison.OrdinalIgnoreCase) || source.Contains("conscript", StringComparison.OrdinalIgnoreCase) || source.Contains("manpower", StringComparison.OrdinalIgnoreCase)) return "manpower";
        if (source.Contains("press", StringComparison.OrdinalIgnoreCase) || source.Contains("law", StringComparison.OrdinalIgnoreCase)) return "law";
        if (source.Contains("government", StringComparison.OrdinalIgnoreCase) || source.Contains("party", StringComparison.OrdinalIgnoreCase) || source.Contains("ideology", StringComparison.OrdinalIgnoreCase) || source.Contains("election", StringComparison.OrdinalIgnoreCase)) return "politics";
        if (source.Contains("prosperity", StringComparison.OrdinalIgnoreCase) || source.Contains("economy", StringComparison.OrdinalIgnoreCase) || source.Contains("production", StringComparison.OrdinalIgnoreCase)) return "production";
        return "information";
    }
    private string PoliticsModifierIcon(string key)
    {
        if (key.Contains("manpower", StringComparison.OrdinalIgnoreCase)) return "manpower";
        if (key.Contains("stability", StringComparison.OrdinalIgnoreCase)) return "legitimacy";
        if (key.EndsWith("_drift", StringComparison.OrdinalIgnoreCase) || key.Contains("popularity", StringComparison.OrdinalIgnoreCase)) return "politics";
        if (key.Contains("opinion", StringComparison.OrdinalIgnoreCase) || key.Contains("relation", StringComparison.OrdinalIgnoreCase)) return "diplomacy";
        return "information";
    }
    // ------------------------------------------------------------------------------------- overview
    // Overview layout. The panel height and the drawn rows both come from these numbers, so they cannot drift apart.
    private const int OverviewVisibleParties = 3;
    private const int OverviewMaxNotes = 2;
    private const float OverviewPartyRowHeight = 27;
    private const float OverviewNoteRowHeight = 20;

    /// <summary>
    /// One line of political attention for the Overview ("a movement is growing", "election soon").
    /// New political systems (factions, regional branches, pending laws) add a note here instead of adding tabs
    /// or buttons; <see cref="TargetTab"/> makes the line a shortcut to the tab with the detail.
    /// </summary>
    private readonly record struct PoliticsNote(string Text, Color Tone, string Tooltip, int TargetTab);

    private List<PoliticsNote> CollectPoliticsNotes(Country c)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var notes = new List<PoliticsNote>();

        foreach (var m in P.MovementsOf(c).Where(m => m.Progress >= 25).OrderByDescending(m => m.Progress))
        {
            string adjective = P.Ideology(m.Ideology)?.Adjective ?? m.Ideology;
            notes.Add(new PoliticsNote($"{adjective} movement at {m.Progress:0}%",
                m.Progress >= 60 ? Gui.Bad : Gui.Gold,
                $"{P.DemandText(m)}\nAt 100% they revolt. Click to open Movements.", 3));
        }

        double stability = G.Rules.Politics.StabilityOf(G, c);
        if (stability < LowStabilityNote)
            notes.Add(new PoliticsNote($"Unstable: stability {stability:0}",
                Gui.Bad, "Low stability feeds extremist support and movements. Click to open Effects.", 6));

        if (P.HoldsElections(c) && p.NextElection is { } next)
        {
            int days = next.TotalDays - G.World.Date.TotalDays;
            if (days >= 0 && days <= ElectionNoteDays)
                notes.Add(new PoliticsNote(days == 0 ? "Election today" : $"Election in {days} days",
                    Gui.Gold, $"Next election: {next.ToDisplay()}", -1));
        }

        int bannedWithSupport = p.PartyPopularity.Count(kv => kv.Value >= BannedSupportNote && P.IsPartyBanned(c, kv.Key));
        if (bannedWithSupport > 0)
            notes.Add(new PoliticsNote(bannedWithSupport == 1 ? "A banned party has real support" : $"{bannedWithSupport} banned parties have real support",
                Gui.Text, "Banned parties with a large following are where movements start. Click to open Parties.", 4));

        return notes;
    }

    private const double LowStabilityNote = 35;
    private const int ElectionNoteDays = 120;
    private const double BannedSupportNote = 0.10;

    private float PoliticsOverviewContentHeight(Country c, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        int partyCount = P.PartyKeysOf(c).Union(p.PartyPopularity.Keys, StringComparer.OrdinalIgnoreCase).Count();
        int visibleParties = Math.Min(OverviewVisibleParties, partyCount);
        int noteCount = Math.Min(OverviewMaxNotes, CollectPoliticsNotes(c).Count);
        float disclosure = partyCount > OverviewVisibleParties ? OverviewDisclosureHeight * S : 0;
        float listHeight = partyCount == 0 ? OverviewPartyRowHeight * S
            : visibleParties * OverviewPartyRowHeight * S + disclosure;
        float blockHeight = Math.Max(listHeight, partyCount > 0 ? OverviewIdentityHeight * S : 0);
        float notesHeight = noteCount > 0 ? noteCount * OverviewNoteRowHeight * S + 6 * S : 0;
        return OverviewPulseHeight * S + 8 * S
            + 22 * S + OverviewChamberHeight(w) + 3 * S + 22 * S + 6 * S
            + notesHeight + 22 * S + blockHeight + 6 * S;
    }

    private const float OverviewPulseHeight = 46;
    private const float OverviewIdentityHeight = 125;
    private const float OverviewDisclosureHeight = 24;
    private const float OverviewColumnGap = 10;
    // A smaller chamber preserves the seat visualization without making Overview taller than the available panel.
    private float OverviewChamberHeight(float w) => Math.Min(w * 0.32f, 145 * S);

    // Party key the pointer is over in the party list; read by the chamber on the next frame to light that party's seats.
    private string? _overviewListHoverKey;

    private void DrawGovernmentTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var partyOrder = P.PartyKeysOf(c)
            .Union(p.PartyPopularity.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(key => p.PartyPopularity.GetValueOrDefault(key))
            .ThenByDescending(key => p.PartySeats.GetValueOrDefault(key))
            .ThenBy(key => P.PartyDisplayName(c, key), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The selected party drives the identity card; before any pick it is the ruling party.
        string selectedKey = string.Equals(_politicsSelectedPartyCountry, c.Tag, StringComparison.OrdinalIgnoreCase)
            && _politicsSelectedPartyKey != null && partyOrder.Contains(_politicsSelectedPartyKey, StringComparer.OrdinalIgnoreCase)
            ? _politicsSelectedPartyKey : p.RulingOrganization;

        int totalSeats = p.PartySeats.Values.Sum();
        int visibleParties = Math.Min(OverviewVisibleParties, partyOrder.Count);
        var notes = CollectPoliticsNotes(c);
        int visibleNotes = Math.Min(OverviewMaxNotes, notes.Count);
        string? listHover = _overviewListHoverKey;
        _overviewListHoverKey = null;

        float disclosureH = partyOrder.Count > OverviewVisibleParties ? OverviewDisclosureHeight * S : 0;
        float naturalListH = partyOrder.Count == 0 ? OverviewPartyRowHeight * S
            : visibleParties * OverviewPartyRowHeight * S + disclosureH;
        float blockH = Math.Max(naturalListH, partyOrder.Count > 0 ? OverviewIdentityHeight * S : 0);
        float notesH = visibleNotes > 0 ? visibleNotes * OverviewNoteRowHeight * S + 6 * S : 0;

        // Pulse: three figures a player checks every few months.
        double stability = G.Rules.Politics.StabilityOf(G, c);
        double power = P.PowerOf(c);
        double governmentSupport = p.GovernmentOrganizations.Where(key => !PoliticsSystem.IsMinorityParty(key))
            .Sum(key => p.PartyPopularity.GetValueOrDefault(key));
        Color stabilityTone = stability >= 60 ? Gui.Good : stability >= LowStabilityNote ? Gui.Gold : Gui.Bad;
        var pulse = new (string Label, string Value, Color Tone, float Fill, string Tip)[]
        {
            ("STABILITY", $"{stability:0}", stabilityTone, (float)(stability / 100),
                $"Stability {stability:0}/100\nLow stability feeds extremists and political movements."),
            (P.PowerName.ToUpperInvariant(), double.IsFinite(power) ? power.ToString("0") : "-", Gui.Gold, -1,
                double.IsFinite(power) ? $"{power:0.#} {P.PowerName}\nSpent on laws, cabinet changes and suppressing movements." : "No political power resource is configured."),
            ("GOVT SUPPORT", $"{governmentSupport:P0}", governmentSupport >= 0.5 ? Gui.Good : Gui.Text, (float)governmentSupport,
                $"Parties in government hold {governmentSupport:P1} of the popular support."),
        };
        float gap = 4 * S;
        float pulseH = OverviewPulseHeight * S;
        float tileW = (w - 2 * gap) / pulse.Length;
        for (int i = 0; i < pulse.Length; i++)
        {
            var tile = new Rectangle(x + i * (tileW + gap), y, tileW, pulseH);
            Gui.Card(tile, new Color(29, 31, 37, 255));
            Gui.LabelCentered(pulse[i].Value, new Rectangle(tile.X, tile.Y + 1 * S, tile.Width, 21 * S), 16, pulse[i].Tone, FontKind.Bold);
            Gui.LabelCentered(Gui.Fit(pulse[i].Label, tile.Width - 8 * S, 14, FontKind.Bold), new Rectangle(tile.X, tile.Y + 20 * S, tile.Width, 18 * S), 14, Gui.TextDim, FontKind.Bold);
            if (pulse[i].Fill >= 0)
                Gui.Bar(new Rectangle(tile.X + 8 * S, tile.Bottom() - 6 * S, tile.Width - 16 * S, 3 * S), pulse[i].Fill, pulse[i].Tone);
            if (Gui.Hover(tile)) Gui.Tooltip(pulse[i].Tip);
        }
        y += pulseH + 8 * S;

        Gui.Section("Parliament", x, y, w);
        y += 22 * S;
        // If the window is height-limited, flex the chart first instead of making the Overview scroll.
        float reserve = 3 * S + 28 * S + notesH + 22 * S + blockH + 6 * S;
        float chamberH = Math.Min(OverviewChamberHeight(w), Math.Max(60 * S, r.Bottom() - y - reserve));
        DrawParliament(c, new Rectangle(x, y, w, chamberH), selectedKey, listHover);
        y += chamberH + 3 * S;
        string election = P.HoldsElections(c) && p.NextElection is { } nextElection
            ? $"Next election: {nextElection.ToDisplay()}" : "Elections not scheduled";
        if (p.LastElection is { } lastElection) election += $" · last {lastElection.ToDisplay()}";
        Gui.LabelCentered(Gui.Fit(election, w, 14), new Rectangle(x, y, w, 20 * S), 14, Gui.TextDim);
        y += 22 * S + 6 * S;

        // At most two attention lines; any further detail remains in their tooltips or destination tab.
        if (visibleNotes > 0)
        {
            foreach (var note in notes.Take(visibleNotes))
            {
                var row = new Rectangle(x, y, w - 4 * S, (OverviewNoteRowHeight - 2) * S);
                string tip = note.Tooltip;
                if (note.TargetTab >= 0 && Gui.Hover(row)) Raylib.DrawRectangleRec(row, new Color(255, 255, 255, 14));
                if (Gui.Hover(row))
                {
                    if (notes.Count > visibleNotes) tip += $"\n\n{notes.Count - visibleNotes} more political matters not shown.";
                    Gui.Tooltip(tip);
                }
                if (note.TargetTab >= 0 && Gui.Clicked(row)) PoliticsTab = note.TargetTab;
                Raylib.DrawCircleV(new Vector2(x + 8 * S, y + 10 * S), 4 * S, note.Tone);
                Gui.Label(Gui.Fit(note.Text, w - 28 * S, 14), x + 18 * S, y + 2 * S, 14, note.Tone);
                y += OverviewNoteRowHeight * S;
            }
            y += 6 * S;
        }

        // Flex the two columns together; the complete party list lives in Organizations, never in an Overview scrollbar.
        Gui.Section("Parties", x, y, w);
        y += 22 * S;
        float columnW = (w - OverviewColumnGap * S) / 2;
        float rightX = x + columnW + OverviewColumnGap * S;
        float listTop = y;
        float listY = y + (blockH - naturalListH) / 2;
        string? hoverKey = null;
        bool hasDisclosure = partyOrder.Count > OverviewVisibleParties;
        float rowH = OverviewPartyRowHeight * S;
        for (int i = 0; i < visibleParties; i++)
        {
            string key = partyOrder[i];
            double support = p.PartyPopularity.GetValueOrDefault(key);
            int seats = p.PartySeats.GetValueOrDefault(key);
            string name = P.PartyDisplayName(c, key);
            bool banned = P.IsPartyBanned(c, key);
            bool rulingParty = string.Equals(p.RulingOrganization, key, StringComparison.OrdinalIgnoreCase);
            bool governing = p.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase);
            string status = banned ? "Banned" : rulingParty ? "Leads the government" : governing ? "In government" : "Opposition";
            var ideology = P.Ideology(P.IdeologyKeyOfParty(key));
            var details = new List<string> { name, status, $"Support {support:P1}", $"Seats {seats}/{totalSeats}" };
            if (ideology != null) details.Add($"Ideology: {ideology.Name}");
            if (P.LeaderOf(G.World.GetGenericScope(key)) is { } partyLeader) details.Add($"Leader: {partyLeader.DisplayName}");
            details.Add("Click to select; click the party card to open Organizations.");

            var row = new Rectangle(x, listY, columnW, rowH - 2 * S);
            if (Gui.Hover(row)) hoverKey = key;
            if (Gui.Button(row, "", true, string.Join("\n", details),
                    string.Equals(selectedKey, key, StringComparison.OrdinalIgnoreCase), 12))
            {
                _politicsSelectedPartyCountry = c.Tag;
                if (!string.Equals(_politicsSelectedPartyKey, key, StringComparison.OrdinalIgnoreCase))
                    _politicsSelectedUnitKey = null;
                _politicsSelectedPartyKey = key;
            }
            if (rulingParty) Raylib.DrawRectangleRec(new Rectangle(row.X + 1, row.Y + 3 * S, 3 * S, row.Height - 6 * S), Gui.Gold);
            else if (governing) Raylib.DrawRectangleRec(new Rectangle(row.X + 1, row.Y + 3 * S, 3 * S, row.Height - 6 * S), Gui.Darker(Gui.Gold, 0.45f));
            float contentTop = row.Y + (row.Height - 25 * S) / 2;
            Raylib.DrawRectangleRec(new Rectangle(row.X + 9 * S, contentTop + 3 * S, 8 * S, 11 * S), C(P.PartyColor(key)));
            string figures = seats > 0 ? $"{seats} · {support:P0}" : $"{support:P0}";
            float figuresW = Gui.Measure(figures, 14).X;
            Gui.Label(Gui.Fit(name, row.Width - figuresW - 40 * S, 14, FontKind.Bold), row.X + 22 * S, contentTop, 14,
                banned ? Gui.TextDim : Gui.Text, FontKind.Bold);
            Gui.Label(figures, row.X + row.Width - 8 * S - figuresW, contentTop + 1 * S, 14, Gui.Gold);
            Gui.Bar(new Rectangle(row.X + 22 * S, contentTop + 17 * S, row.Width - 30 * S, 4 * S),
                (float)Math.Clamp(support, 0, 1), C(P.PartyColor(key)));
            listY += rowH;
        }
        _overviewListHoverKey = hoverKey;
        if (partyOrder.Count == 0)
            Gui.Label("No party support data.", x + 2 * S, listY + 4 * S, 14, Gui.TextDim);
        if (hasDisclosure && Gui.Button(new Rectangle(x, listY, columnW, (OverviewDisclosureHeight - 2) * S),
                $"All parties · {partyOrder.Count} →", true, "Open the complete party register in Organizations.", false, 14))
        {
            _politicsSelectedPartyCountry = c.Tag;
            PoliticsTab = 4;
        }
        if (partyOrder.Count > 0)
            DrawPartyIdentity(c, selectedKey, new Rectangle(rightX, listTop, columnW, blockH - 2 * S));
    }

    /// <summary>
    /// The selected party at a glance: its flag, name, role, leader and support. Everything is looked up from the key on every
    /// frame, so selecting another party in the list changes the card (and its flag) immediately.
    /// </summary>
    private void DrawPartyIdentity(Country c, string key, Rectangle card)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var ideology = P.Ideology(P.IdeologyKeyOfParty(key));
        var scope = G.World.GetGenericScope(key);
        var leader = P.LeaderOf(scope);
        bool banned = P.IsPartyBanned(c, key);
        bool rulingParty = string.Equals(p.RulingOrganization, key, StringComparison.OrdinalIgnoreCase);
        bool governing = p.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase);
        double support = p.PartyPopularity.GetValueOrDefault(key);
        int seats = p.PartySeats.GetValueOrDefault(key);
        string name = P.PartyDisplayName(c, key);
        var partyColor = C(P.PartyColor(key));

        Gui.Card(card, new Color(34, 36, 42, 255));
        Raylib.DrawRectangleRec(new Rectangle(card.X, card.Y + 8 * S, 4 * S, card.Height - 16 * S), partyColor);
        float left = card.X + 14 * S;
        float inner = card.Width - 14 * S - 10 * S;

        var flagRect = new Rectangle(left, card.Y + 10 * S, 64 * S, 43 * S);
        Gui.Image(Assets.PartyFlag(key, P.PartyFlagPath(key), P.PartyColor(key)), flagRect);
        Raylib.DrawRectangleLinesEx(flagRect, 1, new Color(0, 0, 0, 200));
        if (leader != null && Assets.Texture(leader.Portrait) is { } portrait)
        {
            var frame = new Rectangle(card.X + card.Width - 10 * S - 34 * S, card.Y + 8 * S, 34 * S, 44 * S);
            Gui.Image(portrait, frame);
            Raylib.DrawRectangleLinesEx(frame, 1, new Color(0, 0, 0, 200));
        }

        float y = flagRect.Y + flagRect.Height + 8 * S;
        float bottom = card.Y + card.Height - 6 * S;
        // Lines that no longer fit the card are dropped instead of overflowing it.
        void Line(string text, float size, Color color, FontKind font = FontKind.Body)
        {
            float lineH = Gui.Measure("Ag", size, font).Y + 2 * S;
            if (y + lineH > bottom) return;
            Gui.Label(Gui.Fit(text, inner, size, font), left, y, size, color, font);
            y += lineH;
        }

        float nameH = Gui.Measure("Ag", 15, FontKind.Bold).Y + 2 * S;
        float nameX = left;
        if (OrgIcon("parties", key, new Rectangle(left, y - 1 * S, nameH, nameH))) nameX += nameH + 4 * S;
        Gui.Label(Gui.Fit(name, inner - (nameX - left), 15, FontKind.Bold), nameX, y, 15, Gui.Gold, FontKind.Bold);
        y += nameH;

        string ideologyLine = ideology?.Name ?? "Unaligned";
        if (rulingParty && !string.IsNullOrWhiteSpace(c.Government)) ideologyLine += " · " + Localisation.GetOrPretty(c.Government);
        Line(ideologyLine, 14, Gui.TextDim);
        Line(seats > 0 ? $"{support:P0} support · {seats} seats" : $"{support:P0} support", 14, Gui.Text);

        if (Gui.Hover(card))
        {
            var lines = new List<string> { name };
            lines.Add("Status: " + (banned ? "Banned" : rulingParty ? "Leads the government" : governing ? "In government" : "Opposition"));
            lines.Add($"Support: {support:P1} · Seats: {seats}");
            if (leader != null) lines.Add("Leader: " + leader.DisplayName);
            if (rulingParty && p.GovernmentOrganizations.Count > 1)
                lines.Add("Coalition: " + string.Join(", ", p.GovernmentOrganizations.Where(g => !string.Equals(g, key, StringComparison.OrdinalIgnoreCase))
                    .Select(g => P.PartyDisplayName(c, g))));
            if (!string.IsNullOrWhiteSpace(ideology?.Desc)) lines.Add(ideology!.Desc);
            lines.Add("Click to open Parties.");
            Gui.Tooltip(string.Join("\n", lines));
            if (Gui.Clicked(card))
            {
                _politicsSelectedPartyCountry = c.Tag;
                _politicsSelectedPartyKey = key;
                PoliticsTab = 4;
            }
        }
    }

    /// <summary>
    /// A half-circle parliament: each dot belongs to an individual organization, including parties that share an ideology.
    /// <paramref name="selectedKey"/> gets a ring on every seat; <paramref name="listHoverKey"/> (the party list row under the
    /// pointer) lights its seats and dims the rest, as does hovering a seat itself.
    /// </summary>
    private void DrawParliament(Country c, Rectangle area, string? selectedKey = null, string? listHoverKey = null)
    {
        var P = G.Politics;
        var p = P.Of(c);
        if (!string.Equals(_parliamentHoverCountryTag, c.Tag, StringComparison.OrdinalIgnoreCase))
        {
            _parliamentHoverCountryTag = c.Tag;
            _parliamentHoverPartyKey = null;
        }
        int n = p.PartySeats.Values.Sum();
        if (n <= 0)
        {
            _parliamentHoverPartyKey = null;
            Gui.Label("No parliament", area.X, area.Y, 14, Gui.TextDim);
            return;
        }
        // Seat centres stay one edge margin inside the area on every side, so the dots (which extend a radius beyond
        // their centres) are never clipped by the panel. The first version measured to the centres only.
        float edge = 11 * S;
        float outer = Math.Min(area.Width / 2 - edge, area.Height - 2 * edge);
        var center = new Vector2(area.X + area.Width / 2, area.Y + area.Height - edge);
        // More rows for bigger chambers so seats keep a similar size and the arch stays full.
        int rows = Math.Clamp((int)Math.Round(Math.Sqrt(n / 2.6)), 3, 14);
        float inner = outer * 0.42f;
        var radii = Enumerable.Range(0, rows).Select(i => inner + (outer - inner) * (rows == 1 ? 1 : i / (float)(rows - 1))).ToArray();
        float sumR = radii.Sum();
        var counts = radii.Select(rr => (int)Math.Floor(n * rr / sumR)).ToArray();
        counts[^1] += n - counts.Sum();
        var seats = new List<(float Angle, Vector2 Pos)>();
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < counts[i]; k++)
            {
                float a = counts[i] == 1 ? MathF.PI / 2 : MathF.PI * (1 - k / (float)(counts[i] - 1));
                seats.Add((a, center + new Vector2(MathF.Cos(a), -MathF.Sin(a)) * radii[i]));
            }
        seats.Sort((s1, s2) => s2.Angle.CompareTo(s1.Angle));
        float dot = Math.Max(1.5f, Math.Min((outer - inner) / rows * 0.42f, outer * MathF.PI / Math.Max(1, counts[^1]) * 0.42f));
        dot = Math.Min(dot, edge - 1.5f * S);

        var seatOwners = new List<(Vector2 Pos, string PartyKey)>();
        var order = p.PartySeats.Keys.OrderByDescending(k => p.PartySeats.GetValueOrDefault(k)).ToList();
        int si = 0;
        foreach (var key in order)
        {
            int cnt = p.PartySeats.GetValueOrDefault(key);
            for (int k = 0; k < cnt && si < seats.Count; k++, si++)
                // Keep the exact organization attached to each rendered seat so hover never
                // falls back to the country's ruling party or an ideology-wide aggregate.
                seatOwners.Add((seats[si].Pos, key));
        }

        // The parliament is composed of tiny seat dots; test the closest rendered seat rather
        // than showing one shared tooltip for the entire half-circle. A little extra hit radius
        // makes dense parliaments practical to inspect without changing the rendered dot size.
        float hitRadius = Math.Max(dot + 2 * S, 5 * S);
        var hoveredSeat = seatOwners
            .Select(seat => (seat.PartyKey, DistanceSquared: Vector2.DistanceSquared(Gui.Mouse, seat.Pos)))
            .Where(seat => seat.DistanceSquared <= hitRadius * hitRadius)
            .OrderBy(seat => seat.DistanceSquared)
            .FirstOrDefault();

        bool pointerOverParliament = Gui.Hover(area);
        if (!pointerOverParliament)
        {
            // Do not leak a previously hovered party into unrelated UI or a later hover.
            _parliamentHoverPartyKey = null;
        }
        else if (!string.IsNullOrEmpty(hoveredSeat.PartyKey)
                 && p.PartySeats.ContainsKey(hoveredSeat.PartyKey))
        {
            // Update only on a real seat hit. If the pointer crosses background pixels between
            // dots, keep the last party instead of falling through to the ruling-party tooltip.
            _parliamentHoverPartyKey = hoveredSeat.PartyKey;
        }

        string? activeParty = pointerOverParliament ? _parliamentHoverPartyKey : null;
        string? emphasis = activeParty ?? listHoverKey;

        // Seats: a faint shadow under the party colour; the selected party gets a thin light ring.
        foreach (var (pos, key) in seatOwners)
        {
            var col = C(P.PartyColor(key));
            bool dimmed = emphasis != null && !string.Equals(emphasis, key, StringComparison.OrdinalIgnoreCase);
            if (dimmed) col = new Color(col.R, col.G, col.B, (byte)70);
            if (!dimmed) Raylib.DrawCircleV(pos + new Vector2(0, dot * 0.12f), dot, new Color(0, 0, 0, 90));
            Raylib.DrawCircleV(pos, dot, col);
            if (selectedKey != null && !dimmed && string.Equals(selectedKey, key, StringComparison.OrdinalIgnoreCase))
                Raylib.DrawRing(pos, dot + 0.5f * S, dot + 1.3f * S, 0, 360, 36, new Color(255, 244, 205, 150));
        }

        // The hollow of the arch carries one figure: government seats, tinted by whether that is a majority. The wording is in the tooltip.
        int majority = n / 2 + 1;
        int governmentSeats = p.PartySeats.Where(kv => !PoliticsSystem.IsMinorityParty(kv.Key)
                && p.GovernmentOrganizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        bool showHollow = inner > 64 * S;
        if (showHollow)
        {
            Color tone = governmentSeats >= majority ? Gui.Good : Gui.Bad;
            Gui.LabelCentered(governmentSeats.ToString(), new Rectangle(center.X - inner, center.Y - 44 * S, inner * 2, 30 * S), 24, tone, FontKind.Header);
            Gui.LabelCentered($"{majority} needed", new Rectangle(center.X - inner, center.Y - 21 * S, inner * 2, 19 * S), 14, Gui.TextDim);
        }
        bool overHollow = showHollow && pointerOverParliament && Vector2.Distance(Gui.Mouse, center) < inner;

        if (!string.IsNullOrEmpty(activeParty) && p.PartySeats.ContainsKey(activeParty))
        {
            string key = activeParty;
            int partySeats = p.PartySeats.GetValueOrDefault(key);
            double seatShare = partySeats / (double)Math.Max(1, n);
            var details = new List<string>
            {
                P.PartyDisplayName(c, key),
                $"Parliamentary seats: {partySeats} of {n} ({seatShare:P1})"
            };
            if (p.PartyPopularity.TryGetValue(key, out double support))
                details.Add($"Party support: {support:P1}");

            var ideology = P.Ideology(P.IdeologyKeyOfParty(key));
            if (ideology != null) details.Add($"Ideology: {ideology.Name}");
            if (P.LeaderOf(G.World.GetGenericScope(key)) is { } leader)
                details.Add($"Leader: {leader.DisplayName}");
            if (P.IsPartyBanned(c, key)) details.Add("Status: banned by current law");
            else if (p.RulingOrganization.Equals(key, StringComparison.OrdinalIgnoreCase)) details.Add("Status: leads the government");
            else if (p.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase)) details.Add("Status: coalition partner in government");
            else details.Add("Status: opposition");

            Gui.Tooltip(string.Join("\n", details));
        }
        else if (overHollow)
        {
            Gui.Tooltip($"Government seats: {governmentSeats} of {n}\n{majority} are needed to govern without partners.");
        }
        else if (pointerOverParliament)
        {
            Gui.Tooltip(string.Join("\n", p.PartySeats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value)
                .Select(kv => $"{P.PartyDisplayName(c, kv.Key)}: {kv.Value}")) +
                $"\n\n{n} seats · hover a seat for party details." +
                (p.PartySeats.Keys.Any(Burger.Core.Politics.PoliticsSystem.IsMinorityParty)
                    ? "\nMinority parties cannot govern; national parties accelerate independence."
                    : ""));
        }
    }

    // ------------------------------------------------------------------------------------- parties
    private void DrawPartiesTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        if (!string.Equals(_politicsSelectedPartyCountry, c.Tag, StringComparison.OrdinalIgnoreCase))
        {
            _politicsSelectedPartyCountry = c.Tag;
            _politicsSelectedPartyKey = p.RulingOrganization;
            _politicsSelectedUnitKey = null;
        }

        var keys = P.PartyKeysOf(c)
            .OrderByDescending(key => p.PartySeats.GetValueOrDefault(key))
            .ThenByDescending(key => p.PartyPopularity.GetValueOrDefault(key))
            .ThenBy(key => P.PartyDisplayName(c, key), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
        {
            Gui.Label("No political organizations are defined for this country.", x, y, 14, Gui.TextDim);
            return;
        }
        if (string.IsNullOrEmpty(_politicsSelectedPartyKey) || !keys.Contains(_politicsSelectedPartyKey, StringComparer.OrdinalIgnoreCase))
            _politicsSelectedPartyKey = keys.Contains(p.RulingOrganization, StringComparer.OrdinalIgnoreCase) ? p.RulingOrganization : keys[0];

        int seats = p.PartySeats.Values.Sum();
        int majority = seats / 2 + 1;
        int governmentSeats = p.PartySeats.Where(kv => !PoliticsSystem.IsMinorityParty(kv.Key)
                && p.GovernmentOrganizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);
        int named = P.PartiesOf(c).Count();
        string election = P.HoldsElections(c) && p.NextElection is { } next ? $"Next election: {next.ToDisplay()}" : "Elections suspended or not held";
        var registerSummary = new Rectangle(x, y, w, 24 * S);
        Gui.Label(Gui.Fit($"{named} parties · {keys.Count} entries · {seats} seats", w - 26 * S, 15, FontKind.Bold), x, y + 3 * S, 15, Gui.Gold, FontKind.Bold);
        var summaryHelp = new Rectangle(x + w - 20 * S, y + 1 * S, 18 * S, 18 * S);
        if (!Gui.Icon("information", summaryHelp)) Gui.LabelCentered("i", summaryHelp, 14, Gui.Gold, FontKind.Bold);
        string summaryTip = $"Seats: {seats} · majority: {majority} · government: {governmentSeats}\n{election}\nHover a party for ideology, support, seat share and status.";
        if (Gui.Hover(registerSummary)) Gui.Tooltip(summaryTip);
        y += 29 * S;

        float detailsHeight = 212 * S;
        var detailsArea = new Rectangle(x, r.Y + r.Height - detailsHeight - 8 * S, w, detailsHeight);
        var listArea = new Rectangle(x, y, w, Math.Max(90 * S, detailsArea.Y - y - 8 * S));
        PoliticsSection("Party register", "politics_organization", x, y, w);
        y += 30 * S;
        listArea.Y = y;
        listArea.Height = Math.Max(90 * S, detailsArea.Y - y - 6 * S);
        float rowHeight = 42 * S;
        float rowY = Gui.BeginScroll($"politics-parties-{c.Tag}", listArea, keys.Count * (rowHeight + 3 * S));
        foreach (var key in keys)
        {
            double support = p.PartyPopularity.GetValueOrDefault(key);
            int partySeats = p.PartySeats.GetValueOrDefault(key);
            double seatShare = seats > 0 ? partySeats / (double)seats : 0;
            bool governing = p.GovernmentOrganizations.Contains(key, StringComparer.OrdinalIgnoreCase);
            bool ruling = string.Equals(p.RulingOrganization, key, StringComparison.OrdinalIgnoreCase);
            bool banned = P.IsPartyBanned(c, key);
            string role = banned ? "BANNED" : ruling ? "RULING" : governing ? "COALITION" : "OPPOSITION";
            var ideology = P.Ideology(P.IdeologyKeyOfParty(key));
            string label = P.PartyDisplayName(c, key);
            var partyLeader = P.LeaderOf(G.World.GetGenericScope(key));
            string tip = $"{label}\nSupport: {support:P1}\nSeats: {partySeats}/{seats} ({seatShare:P1})\nIdeology: {ideology?.Name ?? "Unspecified"}\nStatus: {role}" +
                (partyLeader == null ? "" : $"\nLeader: {partyLeader.DisplayName}") + "\nClick to open organization details.";
            if (Gui.Button(new Rectangle(x, rowY, w - 5 * S, rowHeight), "", true, tip,
                    string.Equals(_politicsSelectedPartyKey, key, StringComparison.OrdinalIgnoreCase), 12))
            {
                if (!string.Equals(_politicsSelectedPartyKey, key, StringComparison.OrdinalIgnoreCase))
                    _politicsSelectedUnitKey = null;
                _politicsSelectedPartyKey = key;
            }
            Raylib.DrawRectangleRec(new Rectangle(x + 7 * S, rowY + 6 * S, 4 * S, rowHeight - 12 * S), C(P.PartyColor(key)));
            float textX = x + 16 * S;
            if (OrgIcon("parties", key, new Rectangle(x + 15 * S, rowY + 8 * S, 26 * S, 26 * S))
                || Gui.Icon("politics_organization", new Rectangle(x + 15 * S, rowY + 12 * S, 18 * S, 18 * S)))
                textX = x + 46 * S;
            string supportLabel = $"{support:P0}";
            float supportW = Gui.Measure(supportLabel, 14).X;
            Gui.Label(Gui.Fit(label, w - (textX - x) - supportW - 24 * S, 15, FontKind.Bold), textX, rowY + 11 * S, 15, banned ? Gui.TextDim : Gui.Text, FontKind.Bold);
            Gui.Label(supportLabel, x + w - supportW - 12 * S, rowY + 11 * S, 14, governing ? Gui.Good : Gui.Gold);
            rowY += rowHeight + 3 * S;
        }
        Gui.EndScroll();

        var selectedKey = _politicsSelectedPartyKey!;
        var selectedScope = G.World.GetGenericScope(selectedKey);
        var selectedIdeology = P.Ideology(P.IdeologyKeyOfParty(selectedKey));
        int selectedSeats = p.PartySeats.GetValueOrDefault(selectedKey);
        double selectedSupport = p.PartyPopularity.GetValueOrDefault(selectedKey);
        double selectedSeatShare = seats > 0 ? selectedSeats / (double)seats : 0;
        bool selectedRuling = string.Equals(p.RulingOrganization, selectedKey, StringComparison.OrdinalIgnoreCase);
        bool selectedGovernment = p.GovernmentOrganizations.Contains(selectedKey, StringComparer.OrdinalIgnoreCase);
        bool selectedBanned = P.IsPartyBanned(c, selectedKey);

        var units = selectedScope != null && PoliticsSystem.IsParty(selectedScope)
            ? P.PartyUnitsOf(selectedScope, PoliticsSystem.PartyFactionKind)
                .Concat(P.PartyUnitsOf(selectedScope, PoliticsSystem.PartyBranchKind))
                .OrderBy(unit => PoliticsSystem.IsPartyBranch(unit) ? 1 : 0)
                .ThenBy(unit => unit.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<GenericScope>();
        var selectedUnit = units.FirstOrDefault(unit => string.Equals(unit.Key, _politicsSelectedUnitKey, StringComparison.OrdinalIgnoreCase));
        if (_politicsSelectedUnitKey != null && selectedUnit == null)
            _politicsSelectedUnitKey = null;

        PoliticsSection("Organization details", "politics_organization", x, detailsArea.Y, w);
        float dy = detailsArea.Y + 30 * S;
        if (selectedUnit != null)
        {
            // Factions and branches are inspectable entities, not just text in a tooltip.
            if (Gui.Button(new Rectangle(x, dy, 78 * S, 27 * S), "← Back", true,
                    "Return to " + P.PartyDisplayName(c, selectedKey), false, 14))
                _politicsSelectedUnitKey = null;
            dy += 31 * S;
            bool isBranch = PoliticsSystem.IsPartyBranch(selectedUnit);
            string unitIcon = isBranch ? "branch" : "faction";
            float unitNameX = x;
            bool hasUnitIcon = OrgIcon(isBranch ? "branches" : "factions", selectedUnit.Key, new Rectangle(x, dy - 2 * S, 26 * S, 26 * S))
                || Gui.Icon(unitIcon, new Rectangle(x, dy, 20 * S, 20 * S));
            if (hasUnitIcon) unitNameX += 31 * S;
            var branchState = isBranch ? P.StateOfBranch(selectedUnit) : null;
            string unitTip = $"{selectedUnit.DisplayName}\n{(isBranch ? "Regional branch" : "Faction of " + P.PartyDisplayName(c, selectedKey))}" +
                $"\nLeader: {P.LeaderOf(selectedUnit)?.DisplayName ?? "not recorded"}" +
                (isBranch ? $"\nState: {branchState?.DisplayName ?? "not assigned"}" : "");
            var unitNameRect = new Rectangle(unitNameX, dy, w - (unitNameX - x), 24 * S);
            Gui.Label(Gui.Fit(selectedUnit.DisplayName, unitNameRect.Width, 18, FontKind.Bold), unitNameX, dy, 18, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(unitNameRect)) Gui.Tooltip(unitTip);
            dy += 29 * S;
            if (isBranch)
            {
                bool canOpenState = branchState?.Locations.FirstOrDefault() is not null;
                if (Gui.Button(new Rectangle(x, dy, 78 * S, 27 * S), "Map", canOpenState,
                        branchState?.DisplayName == null ? "No state is assigned" : "Select " + branchState.DisplayName + " on the map", false, 14)
                    && branchState?.Locations.FirstOrDefault() is { } location)
                {
                    Ui.SelectedLocation = location;
                    Map.Selected = location;
                    Map.Dirty = true;
                    Ui.Left = LeftPanel.Location;
                }
            }
        }
        else
        {
            float partyNameX = x;
            if (OrgIcon("parties", selectedKey, new Rectangle(x, dy - 2 * S, 26 * S, 26 * S))) partyNameX += 31 * S;
            Gui.Label(Gui.Fit(P.PartyDisplayName(c, selectedKey), w - (partyNameX - x), 17, FontKind.Bold), partyNameX, dy, 17, Gui.Gold, FontKind.Bold);
            dy += 21 * S;
            var leader = P.LeaderOf(selectedScope);
            string first = $"{selectedIdeology?.Name ?? "Unaligned"} · {selectedSupport:P0} · {selectedSeats} seats";
            var summary = new Rectangle(x, dy, w, 19 * S);
            Gui.Label(Gui.Fit(first, w - 24 * S, 14), x, dy, 14);
            var summaryInfo = new Rectangle(x + w - 19 * S, dy, 18 * S, 18 * S);
            if (!Gui.Icon("information", summaryInfo)) Gui.LabelCentered("i", summaryInfo, 14, Gui.Gold, FontKind.Bold);
            string status = selectedBanned ? "Banned" : selectedRuling ? "Government leader" : selectedGovernment ? "Coalition partner" : "Opposition";
            string partyDetailsTip = $"Status: {status}\nLeader: {leader?.DisplayName ?? "Leader unknown"}\nSeats: {selectedSeats}/{seats} ({selectedSeatShare:P1})\nSupport: {selectedSupport:P1}";
            if (Gui.Hover(summary)) Gui.Tooltip(partyDetailsTip);
            dy += 22 * S;
            PoliticsSection($"Factions & branches · {units.Count}", "faction", x, dy, w);
            dy += 28 * S;
            if (units.Count == 0)
            {
                Gui.Label("No factions or branches recorded.", x, dy, 14, Gui.TextDim);
            }
            else
            {
                var unitArea = new Rectangle(x, dy, w, Math.Max(38 * S, detailsArea.Bottom() - dy - 5 * S));
                const float unitRowHeightBase = 40;
                float unitRowHeight = unitRowHeightBase * S;
                float unitY = Gui.BeginScroll($"politics-units-{c.Tag}-{selectedKey}", unitArea, units.Count * (unitRowHeight + 3 * S));
                foreach (var unit in units)
                {
                    bool isBranch = PoliticsSystem.IsPartyBranch(unit);
                    var state = isBranch ? P.StateOfBranch(unit) : null;
                    string type = isBranch ? "Branch" : "Faction";
                    string tip = $"{unit.DisplayName}\n{type}\nLeader: {P.LeaderOf(unit)?.DisplayName ?? "not recorded"}" +
                        (isBranch ? $"\nState: {state?.DisplayName ?? "not assigned"}" : "");
                    var unitRow = new Rectangle(x, unitY, w - 4 * S, unitRowHeight);
                    if (Gui.Button(unitRow, "", true, tip,
                            string.Equals(_politicsSelectedUnitKey, unit.Key, StringComparison.OrdinalIgnoreCase), 14))
                        _politicsSelectedUnitKey = unit.Key;
                    string unitIcon = isBranch ? "branch" : "faction";
                    float unitTextX = x + 5 * S;
                    if (OrgIcon(isBranch ? "branches" : "factions", unit.Key, new Rectangle(unitTextX, unitY + 6 * S, 26 * S, 26 * S))
                        || Gui.Icon(unitIcon, new Rectangle(unitTextX, unitY + 10 * S, 18 * S, 18 * S)))
                        unitTextX += 31 * S;
                    Gui.Label(Gui.Fit(unit.DisplayName, w - (unitTextX - x) - 8 * S, 14, FontKind.Bold), unitTextX, unitY + 11 * S, 14, Gui.Text, FontKind.Bold);
                    unitY += unitRowHeight + 3 * S;
                }
                Gui.EndScroll();
            }
        }
    }

    // ------------------------------------------------------------------------------------- effects
    private void DrawEffectsTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        double stability = G.Rules.Politics.StabilityOf(G, c);
        double power = P.PowerOf(c);
        var sources = P.ModifierSources(c).Where(s => Math.Abs(s.Value) > 1e-6)
            .GroupBy(s => s.Source, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Source: g.Key, Values: g.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => (Key: v.Key, Value: v.Sum(t => t.Value)))
                .Where(v => Math.Abs(v.Value) > 1e-6).OrderBy(v => v.Key).ToList()))
            .Where(g => g.Values.Count > 0)
            .OrderBy(g => g.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totals = sources.SelectMany(s => s.Values).GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Value: g.Sum(v => v.Value)))
            .Where(v => Math.Abs(v.Value) > 1e-6)
            .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        float statusH = 48 * S, gap = 6 * S, cellW = (w - gap * 2) / 3;
        var status = new (string Label, string Value, string Icon, Color Tone, string Tip)[]
        {
            (P.PowerName, double.IsFinite(power) ? power.ToString("0.#") : "—", "politics", Gui.Gold, "Political power available to enact laws and manage politics."),
            ("Stability", $"{stability:0.#}/100", "legitimacy", stability >= 60 ? Gui.Good : stability >= LowStabilityNote ? Gui.Gold : Gui.Bad, "Low stability feeds extremist support and political movements."),
            ("Active effects", totals.Count.ToString(), "information", Gui.Text, $"{sources.Count} modifier source(s) · {p.PartyPopularity.Count} tracked party entries."),
        };
        for (int i = 0; i < status.Length; i++)
        {
            var cell = new Rectangle(x + i * (cellW + gap), y, cellW, statusH);
            Gui.Card(cell, new Color(29, 31, 37, 255));
            bool hasIcon = Gui.Icon(status[i].Icon, new Rectangle(cell.X + 6 * S, cell.Y + 13 * S, 20 * S, 20 * S));
            float labelX = cell.X + (hasIcon ? 32 : 6) * S;
            float labelW = cellW - (hasIcon ? 38 : 12) * S;
            Gui.Label(Gui.Fit(status[i].Label, labelW, 14, FontKind.Bold), labelX, cell.Y + 4 * S, 14, Gui.TextDim, FontKind.Bold);
            Gui.Label(Gui.Fit(status[i].Value, labelW, 17, FontKind.Bold), labelX, cell.Y + 23 * S, 17, status[i].Tone, FontKind.Bold);
            if (Gui.Hover(cell)) Gui.Tooltip(status[i].Tip);
        }
        y += statusH + 9 * S;
        PoliticsSection("Net effects", "effects", x, y, w);
        y += 24 * S;
        if (totals.Count == 0)
        {
            Gui.Label("No active political modifiers.", x + 2 * S, y, 14, Gui.TextDim);
            y += 24 * S;
        }
        else
        {
            const int maxVisible = 6;
            int visible = Math.Min(maxVisible, totals.Count);
            float cellH = 34 * S;
            for (int i = 0; i < visible; i++)
            {
                var item = totals[i];
                var cell = new Rectangle(x, y + i * cellH, w - 3 * S, cellH - 3 * S);
                Gui.Card(cell, item.Value >= 0 ? new Color(30, 48, 37, 255) : new Color(50, 34, 34, 255));
                string text = ModifierText(item.Key, item.Value);
                bool hasIcon = Gui.Icon(PoliticsModifierIcon(item.Key), new Rectangle(cell.X + 6 * S, cell.Y + 7 * S, 20 * S, 20 * S));
                float textX = cell.X + (hasIcon ? 34 : 8) * S;
                Gui.Label(Gui.Fit(text, cell.Width - (hasIcon ? 42 : 16) * S, 14), textX, cell.Y + 7 * S, 14, item.Value >= 0 ? Gui.Good : Gui.Bad);
                if (Gui.Hover(cell)) Gui.Tooltip($"Net: {text}\nModifier key: {item.Key}");
            }
            y += visible * cellH;
            if (totals.Count > visible)
            {
                var more = new Rectangle(x, y, w, 24 * S);
                string hidden = string.Join("\n", totals.Skip(visible).Select(v => ModifierText(v.Key, v.Value)));
                Gui.Label($"{totals.Count - visible} more effects · hover for list", x + 3 * S, y + 2 * S, 14, Gui.TextDim);
                if (Gui.Hover(more)) Gui.Tooltip("Additional net effects\n" + hidden);
                y += 26 * S;
            }
            else y += 6 * S;
        }
        PoliticsSection($"Sources · {sources.Count}", "effects", x, y, w);
        y += 24 * S;
        if (sources.Count == 0)
        {
            Gui.Label("No effect sources are currently active.", x, y, 14, Gui.TextDim);
            return;
        }
        var area = new Rectangle(x, y, w, Math.Max(76 * S, r.Bottom() - y - 10 * S));
        const float rowHeightBase = 42;
        float rowHeight = rowHeightBase * S;
        float rowY = Gui.BeginScroll($"politics-effects-{c.Tag}", area, sources.Count * 46 * S);
        foreach (var source in sources)
        {
            var row = new Rectangle(x, rowY, w - 5 * S, rowHeight);
            Gui.Card(row, new Color(29, 31, 37, 255));
            bool hasIcon = Gui.Icon(PoliticsSourceIcon(source.Source), new Rectangle(row.X + 6 * S, row.Y + 10 * S, 22 * S, 22 * S));
            float textX = row.X + (hasIcon ? 36 : 8) * S;
            float textW = row.Width - (textX - row.X) - 30 * S;
            Gui.Label(Gui.Fit(source.Source, textW, 15, FontKind.Bold), textX, row.Y + 12 * S, 15, Gui.Gold, FontKind.Bold);
            var info = new Rectangle(row.X + row.Width - 22 * S, row.Y + 11 * S, 18 * S, 18 * S);
            if (!Gui.Icon("information", info)) Gui.LabelCentered("i", info, 14, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(row)) Gui.Tooltip(source.Source + "\n" + string.Join("\n", source.Values.Select(v => ModifierText(v.Key, v.Value))));
            rowY += rowHeight + 4 * S;
        }
        Gui.EndScroll();
    }
    // ------------------------------------------------------------------------------------- laws
    private void DrawLawsTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        double power = P.PowerOf(c);
        string powerLabel = double.IsFinite(power) ? $"{power:0.#} {P.PowerName}" : $"{P.PowerName}: not configured";
        Gui.Label(Gui.Fit(powerLabel, w - 28 * S, 15, FontKind.Bold), x, y, 15, Gui.Gold, FontKind.Bold);
        var help = new Rectangle(x + w - 22 * S, y - 1 * S, 20 * S, 20 * S);
        if (!Gui.Icon("information", help)) Gui.LabelCentered("i", help, 14, Gui.Gold, FontKind.Bold);
        if (Gui.Hover(help)) Gui.Tooltip("Hover a law to see its effects, cost and requirements. Select an available law to enact it.");
        y += 29 * S;
        var groups = P.LawGroups.Where(g => P.IsGroupVisible(c, g)).ToList();
        float rowsH = groups.Sum(g => LawGroupHeight(c, g, w));
        float top = Gui.BeginScroll($"politics-laws-{c.Tag}", new Rectangle(x, y, w, r.Bottom() - y - 9 * S), rowsH);
        float ly = top;
        foreach (var g in groups)
        {
            var current = P.LawOf(c, g);
            int cd = P.LawCooldownLeft(c, g);
            var visible = g.Laws.Where(l => l == current || P.IsLawVisible(c, l)).ToList();
            string groupTip = string.IsNullOrWhiteSpace(g.Desc) ? g.Name : $"{g.Name}\n{g.Desc}";
            float cooldownW = cd > 0 ? Math.Min(w * 0.34f, Gui.Measure($"{cd}d cooldown", 14).X + 8 * S) : 0;
            float sectionW = w - cooldownW - (cooldownW > 0 ? 8 * S : 0);
            PoliticsSection(g.Name, "politics_law", x, ly, sectionW);
            if (Gui.Hover(new Rectangle(x, ly, w - 4 * S, 24 * S))) Gui.Tooltip(groupTip);
            if (cd > 0) Gui.Label($"{cd}d cooldown", x + w - cooldownW, ly + 5 * S, 14, Gui.Gold);
            ly += 28 * S;
            float bx = x, bh = 35 * S;
            foreach (var law in visible)
            {
                var icon = string.IsNullOrWhiteSpace(law.Icon) ? null : Assets.Texture(law.Icon);
                bool hasIcon = icon.HasValue;
                float bw = hasIcon ? 42 * S : Math.Max(110 * S, Gui.Measure(law.Name, 15).X + 20 * S);
                if (bx > x && bx + bw > x + w - 6 * S) { bx = x; ly += bh + 5 * S; }
                bool isCurrent = law == current;
                var reason = isCurrent ? null : P.CannotEnact(c, law);
                string tip = law.Name + (string.IsNullOrWhiteSpace(law.Desc) ? "" : $"\n{law.Desc}") +
                             $"\n\n{ModifierLines(law.Modifier)}" +
                             (law.Bans.Count > 0 ? $"\nBans: {string.Join(", ", law.Bans.Select(b => P.Ideology(b)?.Name ?? b))}" : "") +
                             (law.SingleParty ? "\nOnly the ruling party is allowed; elections suspended" : law.SuspendsElections ? "\nElections suspended" : "");
                if (!isCurrent)
                {
                    tip += $"\n\nCost: {law.Cost:0} {P.PowerName}";
                    if (law.Allow != null)
                        foreach (var (text, ok) in G.Script.ExplainTrigger(law.Allow, G.Context(c))) tip += $"\n{(ok ? "[x]" : "[ ]")} {text}";
                    if (reason != null) tip += $"\n\nUnavailable: {reason}";
                }
                var button = new Rectangle(bx, ly, bw, bh);
                if (Gui.Button(button, hasIcon ? "" : law.Name, isCurrent || reason == null, tip, isCurrent, 15) && !isCurrent)
                {
                    var err = P.Enact(c, law);
                    if (err != null) Ui.SetStatus(err);
                }
                if (hasIcon)
                {
                    float size = Math.Min(24 * S, bh - 6 * S);
                    Gui.Image(icon!.Value, new Rectangle(button.X + (button.Width - size) / 2, button.Y + (button.Height - size) / 2, size, size));
                }
                bx += bw + 6 * S;
            }
            ly += bh + 10 * S;
        }
        if (groups.Count == 0) Gui.Label("No law groups are available.", x + 2 * S, ly + 2 * S, 14, Gui.TextDim);
        Gui.EndScroll();
    }
    private float LawGroupHeight(Country c, LawGroup g, float w)
    {
        var P = G.Politics;
        float y = 28 * S, bx = 0, bh = 35 * S;
        foreach (var law in g.Laws.Where(l => l == P.LawOf(c, g) || P.IsLawVisible(c, l)))
        {
            bool hasIcon = !string.IsNullOrWhiteSpace(law.Icon) && Assets.Texture(law.Icon).HasValue;
            float bw = hasIcon ? 42 * S : Math.Max(110 * S, Gui.Measure(law.Name, 15).X + 20 * S);
            if (bx > 0 && bx + bw > w - 6 * S) { bx = 0; y += bh + 5 * S; }
            bx += bw + 6 * S;
        }
        return y + bh + 10 * S;
    }
    // ------------------------------------------------------------------------------------- cabinet
    private void DrawCabinetTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        double power = P.PowerOf(c);
        Gui.Label($"{P.PowerName}: {(double.IsFinite(power) ? power.ToString("0.#") : "not configured")}", x, y, 15, Gui.Gold, FontKind.Bold);
        y += 29 * S;
        float contentH = P.Positions.Count * 74 * S;
        if (_cabinetPick is { } pickForHeight && P.Positions.FirstOrDefault(p => p.Key == pickForHeight) is { } selectedPosition)
            contentH += P.Candidates(c, selectedPosition).Take(5).Count() * 36 * S + 8 * S;
        var area = new Rectangle(x, y, w, Math.Max(70 * S, r.Bottom() - y - 9 * S));
        float cy = Gui.BeginScroll($"politics-cabinet-{c.Tag}", area, contentH);
        foreach (var pos in P.Positions)
        {
            var box = new Rectangle(x, cy, w, 68 * S);
            Card(box, new Color(36, 38, 44, 255), _cabinetPick == pos.Key);
            bool hasIcon = Gui.Icon("cabinet", new Rectangle(box.X + 8 * S, box.Y + 11 * S, 20 * S, 20 * S));
            float titleX = box.X + (hasIcon ? 34 : 8) * S;
            Gui.Label(Gui.Fit(pos.Name, box.Width - 140 * S, 17, FontKind.Bold), titleX, box.Y + 6 * S, 17, Gui.Gold, FontKind.Bold);
            var minister = P.MinisterOf(c, pos);
            string who = minister == null ? "Vacant" : $"{minister.DisplayName} ({P.Ideology(P.MinisterParty(minister))?.Name}), {Localisation.GetOrPretty(pos.Stat)} {minister.GetStat(pos.Stat):0}";
            Gui.Label(Gui.Fit(who, box.Width - 122 * S, 15), box.X + 8 * S, box.Y + 36 * S, 15, minister == null ? Gui.TextDim : Gui.Text);
            var effects = new Dictionary<string, double>(pos.Modifier);
            if (minister != null) foreach (var kv in pos.PerPoint) effects[kv.Key] = effects.GetValueOrDefault(kv.Key) + kv.Value * minister.GetStat(pos.Stat);
            if (Gui.Hover(box))
                Gui.Tooltip($"{pos.Name}\nGood ministers have high {Localisation.GetOrPretty(pos.Stat)}.\nPer point: {string.Join(", ", pos.PerPoint.Select(kv => ModifierText(kv.Key, kv.Value)))}" +
                            (minister != null ? $"\n\nNow: {string.Join(", ", effects.Select(kv => ModifierText(kv.Key, kv.Value)))}" : ""));
            if (Gui.Button(new Rectangle(box.X + box.Width - 108 * S, box.Y + 18 * S, 100 * S, 32 * S), minister == null ? "Appoint" : "Replace", true, $"Choose a minister ({pos.Cost:0} {P.PowerName})", _cabinetPick == pos.Key, 15))
                _cabinetPick = _cabinetPick == pos.Key ? null : pos.Key;
            cy += 74 * S;
            if (_cabinetPick == pos.Key)
            {
                foreach (var candidate in P.Candidates(c, pos).Take(5))
                {
                    var row = new Rectangle(x + 12 * S, cy, w - 12 * S, 32 * S);
                    string text = $"{candidate.DisplayName} - {P.Ideology(P.MinisterParty(candidate))?.Name}, {Localisation.GetOrPretty(pos.Stat)} {candidate.GetStat(pos.Stat):0}, age {candidate.Age(G.World.Date)}";
                    var reason = P.CannotAppoint(c, pos, candidate);
                    if (Gui.Button(row, Gui.Fit(text, row.Width - 12 * S, 14), reason == null, reason ?? $"Appoint for {pos.Cost:0} {P.PowerName}", false, 14))
                    {
                        var err = P.Appoint(c, pos, candidate);
                        if (err != null) Ui.SetStatus(err); else _cabinetPick = null;
                    }
                    cy += 36 * S;
                }
                cy += 8 * S;
            }
        }
        Gui.EndScroll();
    }
    // ------------------------------------------------------------------------------------- movements
    private void DrawMovementsTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var list = P.MovementsOf(c).ToList();
        float contentH = 29 * S + (list.Count == 0 ? 26 * S : list.Count * 122 * S);
        var area = new Rectangle(x, y, w, Math.Max(70 * S, r.Bottom() - y - 9 * S));
        float cy = Gui.BeginScroll($"politics-movements-{c.Tag}", area, contentH);
        var help = new Rectangle(x + w - 22 * S, cy + 2 * S, 20 * S, 20 * S);
        if (!Gui.Icon("information", help)) Gui.LabelCentered("i", help, 14, Gui.Gold, FontKind.Bold);
        if (Gui.Hover(help)) Gui.Tooltip(PoliticsMovementTooltip);
        cy += 29 * S;
        if (list.Count == 0)
        {
            Gui.Label("No political movements.", x, cy, 15, Gui.Good);
            Gui.EndScroll();
            return;
        }
        foreach (var movement in list)
        {
            var ideology = P.Ideology(movement.Ideology);
            var box = new Rectangle(x, cy, w, 114 * S);
            Card(box, new Color(44, 36, 36, 255));
            Raylib.DrawRectangleRec(new Rectangle(box.X + 8 * S, box.Y + 10 * S, 16 * S, 16 * S), C(ideology?.Color ?? Rgb.Gray));
            Gui.Label(Gui.Fit($"{ideology?.Adjective} movement", box.Width - 42 * S, 18, FontKind.Bold), box.X + 31 * S, box.Y + 5 * S, 18, Gui.Gold, FontKind.Bold);
            Gui.Label(Gui.Fit(P.DemandText(movement), box.Width - 16 * S, 15), box.X + 8 * S, box.Y + 32 * S, 15);
            var bar = new Rectangle(box.X + 8 * S, box.Y + 58 * S, box.Width - 16 * S, 12 * S);
            Gui.Bar(bar, (float)(movement.Progress / 100), new Color(220, 90, 60, 255));
            if (Gui.Hover(bar)) Gui.Tooltip($"{movement.Progress:0}% - at 100% they revolt. Support: {P.PopularityOf(c, movement.Ideology):P0}");
            float by = box.Y + 78 * S;
            float buttonGap = 10 * S, giveW = (w - buttonGap) * 0.42f, suppressW = w - buttonGap - giveW;
            if (Gui.Button(new Rectangle(box.X + 8 * S, by, giveW - 8 * S, 30 * S), "Give in", true, P.DemandText(movement), false, 15)) P.GiveIn(movement);
            var reason = P.CannotSuppress(movement);
            if (Gui.Button(new Rectangle(box.X + giveW + buttonGap, by, suppressW - 8 * S, 30 * S), $"Suppress ({P.SuppressCost:0})", reason == null,
                    reason ?? $"Set the movement back. Costs {P.SuppressCost:0} {P.PowerName} and some stability; martyrs make the ideology a little more popular.", false, 15))
            {
                var err = P.Suppress(movement);
                if (err != null) Ui.SetStatus(err);
            }
            cy += 122 * S;
        }
        Gui.EndScroll();
    }
    /// <summary>Government and popularity of another country (in the country view).</summary>
    private float DrawCountryPolitics(Country c, float x, float y, float w)
    {
        var P = G.Politics;
        if (!P.Enabled) return y;
        var p = P.Of(c);
        var ruling = P.Ideology(p.Ruling);
        Gui.Section("Politics", x, y, w);
        y += 26 * S;
        Raylib.DrawRectangleRec(new Rectangle(x, y + 3 * S, 12 * S, 12 * S), C(ruling?.Color ?? Rgb.Gray));
        Gui.Label(Gui.Fit($"{P.PartyName(c, p.RulingOrganization)} ({ruling?.Name})" + (p.GovernmentOrganizations.Count > 1 ? " + coalition" : ""), w - 20 * S, 14), x + 18 * S, y, 14);
        y += 20 * S;
        {
            float pr = 26 * S;
            var ordered = P.PartyKeysOf(c).OrderByDescending(key => p.PartyPopularity.GetValueOrDefault(key)).ToList();
            Pie(new Vector2(x + pr, y + pr), pr, ordered.Select(key => (P.PartyLabel(key), p.PartyPopularity.GetValueOrDefault(key), P.PartyColor(key))).ToList(), "Party support");
            float ly = y;
            foreach (var key in ordered.Take(4))
            {
                Raylib.DrawRectangleRec(new Rectangle(x + 2 * pr + 10 * S, ly + 3 * S, 9 * S, 9 * S), C(P.PartyColor(key)));
                Gui.Label(Gui.Fit($"{P.PartyDisplayName(c, key)} {p.PartyPopularity.GetValueOrDefault(key):P0}", w - 2 * pr - 26 * S, 14), x + 2 * pr + 24 * S, ly, 14);
                ly += 16 * S;
            }
            y += Math.Max(2 * pr, ly - y) + 4 * S;
            Gui.Label(P.HoldsElections(c) && p.NextElection is { } ne ? $"Next election: {ne.ToDisplay()}" : "No elections", x, y, 14, Gui.TextDim);
            y += 20 * S;
        }
        double op = P.IdeologyOpinion(G.World.Player ?? c, c);
        if (G.World.Player != null && G.World.Player != c && Math.Abs(op) > 0.01)
        {
            Gui.Label($"Ideology: {(op >= 0 ? "+" : "")}{op:0} opinion of them", x, y, 14, op >= 0 ? Gui.Good : Gui.Bad);
            y += 18 * S;
        }
        return y + 6 * S;
    }
}
