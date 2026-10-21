#nullable enable
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Division designer: templates on the left, battalions in the middle (a slot grid you drag battalions into, or a +/- list;
/// both edit the same design), live stats with the change against the saved template on the right.
/// </summary>
public sealed partial class Hud
{
    private DivisionTemplate? _designBase;
    private string _designName = "New Division";
    private readonly List<(UnitType Type, int Count)> _design = new();
    private bool _designGrid = true;

    private UnitType? _dragType;
    private bool _dragFromGrid;
    private Vector2 _dragStart;

    private void LoadDesign(DivisionTemplate t)
    {
        _designBase = t;
        _designName = t.Name;
        _design.Clear();
        _design.AddRange(t.Units);
    }

    private void AddBattalion(UnitType type)
    {
        int i = _design.FindIndex(d => d.Type == type);
        if (i >= 0) _design[i] = (type, _design[i].Count + 1);
        else _design.Add((type, 1));
    }

    private void RemoveBattalion(UnitType type)
    {
        int i = _design.FindIndex(d => d.Type == type);
        if (i < 0) return;
        if (_design[i].Count <= 1) _design.RemoveAt(i);
        else _design[i] = (type, _design[i].Count - 1);
    }

    private static Color UnitColor(UnitType u) => u.Category.ToLowerInvariant() switch
    {
        "infantry" => new Color(120, 150, 100, 255), "mobile" or "cavalry" => new Color(200, 160, 80, 255),
        "armor" or "armour" or "tank" => new Color(200, 100, 80, 255), "support" => new Color(110, 160, 210, 255),
        "artillery" => new Color(190, 120, 60, 255), _ => CategoryColor(u.Category),
    };

    /// <summary>A battalion tile: category stripe, short name, optional count badge.</summary>
    private void UnitTile(Rectangle r, UnitType u, int count, bool enabled)
    {
        bool hover = Gui.Hover(r);
        Card(r, hover && enabled ? new Color(52, 54, 62, 255) : new Color(36, 38, 44, 255), hover && enabled);
        Raylib.DrawRectangleRec(new Rectangle(r.X, r.Y, r.Width, 3 * S), enabled ? UnitColor(u) : Gui.TextDim);
        var icon = new Rectangle(r.X + r.Width / 2 - 9 * S, r.Y + 6 * S, 18 * S, 18 * S);
        Gui.Icon(UnitIcon(u), icon, enabled ? null : new Color(255, 255, 255, 100));
        Gui.LabelCentered(Gui.Fit(u.Name, r.Width - 4 * S, 11, FontKind.Bold), new Rectangle(r.X, r.Y + r.Height - 18 * S, r.Width, 16 * S), 11, enabled ? Gui.Text : Gui.TextDim, FontKind.Bold);
        if (count > 1)
        {
            string c = count.ToString();
            var m = Gui.Measure(c, 12, FontKind.Bold);
            Gui.Label(c, r.X + r.Width - m.X - 4 * S, r.Y + 5 * S, 12, Gui.Gold, FontKind.Bold);
        }
    }

    private static string UnitIcon(UnitType u)
    {
        string c = u.Category.ToLowerInvariant();
        if (c.Contains("air") || c.Contains("fight") || c.Contains("bomb")) return "air";
        if (c.Contains("nav") || c.Contains("ship") || c.Contains("sea") || c.Contains("sub")) return "navy";
        if (c.Contains("art") || c.Contains("support") || c.Contains("gun")) return "artillery";
        return "army";
    }

    private string UnitTip(UnitType u)
    {
        var lines = new List<string> { u.Name, $"Manpower {u.Manpower:0}   Training {u.BuildDays} days" + (G.Economy.GoldCosts ? $"   Gold {u.Cost:0}" : "") };
        if (u.Equipment.Count > 0) lines.Add("Equipment: " + string.Join(", ", u.Equipment.Select(kv => $"{kv.Value:0} {G.Data.Equipment.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}")));
        foreach (var kv in u.Stats.Where(kv => Math.Abs(kv.Value) > 1e-9).Take(8)) lines.Add($"{Localisation.GetOrPretty(kv.Key)}: {kv.Value:0.##}");
        return string.Join("\n", lines);
    }

    private void DrawDesignerPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.DivisionDesigner)) { CloseLeft(); return; }
        var r = FitRect("designer");
        r.Width = Math.Min(790 * S, Raylib.GetScreenWidth() - r.X - 12 * S);
        Gui.Panel(r, "");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y0 = r.Y + Gui.HeaderHeight + 10 * S;
        if (_design.Count == 0 && _designBase == null && G.Designer.TemplatesOf(player).FirstOrDefault() is { } first) LoadDesign(first);
        int max = G.Designer.Rules.MaxBattalions(G, player);
        int total = _design.Sum(u => u.Count);
        bool mouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);

        // ---- templates ------------------------------------------------------------------------
        float lw = 160 * S;
        Gui.Section("Templates", x, y0, lw);
        var templates = G.Designer.TemplatesOf(player).ToList();
        float top = Gui.BeginScroll("designs", new Rectangle(x, y0 + 26 * S, lw, r.Y + r.Height - y0 - 36 * S), templates.Count * 30 * S + 34 * S);
        float ty = top;
        if (Gui.Button(new Rectangle(x, ty, lw - 10 * S, 26 * S), "+ New", true, "Start an empty design", false, 14)) { _designBase = null; _designName = "New Division"; _design.Clear(); }
        ty += 32 * S;
        foreach (var t in templates)
        {
            if (Gui.Button(new Rectangle(x, ty, lw - 10 * S, 26 * S), t.Name, true, (t.Custom ? "Your design" : "From the game files") + $"\n{t.Battalions} battalions", _designBase == t, 13))
                LoadDesign(t);
            ty += 30 * S;
        }
        Gui.EndScroll();

        // ---- columns --------------------------------------------------------------------------
        float cx = x + lw + 10 * S, right = r.X + r.Width - 12 * S;
        float sw = 250 * S;                       // stats column
        float cw = right - sw - 12 * S - cx;      // battalion column
        float sx = cx + cw + 12 * S;
        float y = y0;

        Gui.TextField("designname", new Rectangle(cx, y, cw, 28 * S), ref _designName, 40, "Division name");
        y += 36 * S;
        Gui.Section($"Battalions ({total}/{max})", cx, y, cw - 120 * S);
        float mw = 56 * S;
        if (Gui.Button(new Rectangle(right - sw - 12 * S - 2 * mw - 4 * S, y - 2 * S, mw, 22 * S), "Grid", true, "Drag battalions into the slots (or click them)", _designGrid, 12)) _designGrid = true;
        if (Gui.Button(new Rectangle(right - sw - 12 * S - mw, y - 2 * S, mw, 22 * S), "List", true, "Edit counts with + and -", !_designGrid, 12)) _designGrid = false;
        y += 28 * S;

        Rectangle dropArea;
        if (_designGrid)
        {
            int cols = 5;
            float tile = Math.Min(60 * S, (cw - (cols - 1) * 4 * S) / cols);
            float th = tile * 0.85f;
            int slots = Math.Min(Math.Max(max, total), total + 1 + (cols - (total + 1) % cols) % cols); // filled slots plus the rest of the next row
            int rows = (slots + cols - 1) / cols;
            dropArea = new Rectangle(cx, y, cols * tile + (cols - 1) * 4 * S, rows * (th + 4 * S) - 4 * S);
            var flat = _design.SelectMany(d => Enumerable.Repeat(d.Type, d.Count)).ToList();
            for (int i = 0; i < slots; i++)
            {
                var sr = new Rectangle(cx + (i % cols) * (tile + 4 * S), y + (i / cols) * (th + 4 * S), tile, th);
                if (i >= flat.Count)
                {
                    Raylib.DrawRectangleRec(sr, new Color(14, 15, 18, 200));
                    Raylib.DrawRectangleLinesEx(sr, 1, new Color(70, 72, 80, 255));
                    continue;
                }
                var u = flat[i];
                UnitTile(sr, u, 1, true);
                if (Gui.Hover(sr))
                {
                    Gui.Tooltip(UnitTip(u) + "\n\nDrag out to remove, or right-click.");
                    if (_dragType == null && Raylib.IsMouseButtonPressed(MouseButton.Left)) { _dragType = u; _dragFromGrid = true; _dragStart = Gui.Mouse; }
                    if (Gui.RightClicked(sr)) { RemoveBattalion(u); break; }
                }
            }
            Gui.Block(dropArea);
            y += dropArea.Height + 10 * S;
            if (_dragType != null && !_dragFromGrid && Gui.Hover(dropArea))
                Raylib.DrawRectangleLinesEx(dropArea, 2, total < max ? Gui.Good : Gui.Bad);
        }
        else
        {
            dropArea = new Rectangle(cx, y, cw, Math.Max(24 * S, _design.Count * 28 * S));
            for (int i = 0; i < _design.Count; i++)
            {
                var (type, count) = _design[i];
                Gui.Label(Gui.Fit(type.Name, cw - 120 * S, 15), cx, y + 4 * S, 15);
                if (Gui.Button(new Rectangle(cx + cw - 110 * S, y, 28 * S, 24 * S), "-", true, null, false, 16)) { RemoveBattalion(type); break; }
                Gui.Label(count.ToString(), cx + cw - 72 * S, y + 3 * S, 16, Gui.Text, FontKind.Bold);
                if (Gui.Button(new Rectangle(cx + cw - 40 * S, y, 28 * S, 24 * S), "+", total < max, null, false, 16)) { AddBattalion(type); break; }
                y += 28 * S;
            }
            if (_design.Count == 0) { Gui.Label("Nothing yet. Click a battalion below.", cx, y + 4 * S, 14, Gui.TextDim); y += 28 * S; }
            y += 8 * S;
        }

        // ---- palette --------------------------------------------------------------------------
        Gui.Section("Available", cx, y, cw);
        y += 26 * S;
        var palette = G.Data.UnitTypes.Values.Where(u => G.Designer.Rules.CanUse(G, player, u)).ToList();
        float pw = Math.Min(70 * S, (cw - 4 * 4 * S) / 5), ph = 44 * S;
        float px = cx;
        foreach (var group in palette.GroupBy(u => u.Category))
        {
            Gui.Label(Cap(group.Key), cx, y, 12, Gui.TextDim, FontKind.Bold);
            y += 16 * S;
            px = cx;
            foreach (var u in group)
            {
                if (px + pw > cx + cw + 1) { px = cx; y += ph + 4 * S; }
                var pr = new Rectangle(px, y, pw, ph);
                bool can = total < max;
                UnitTile(pr, u, 0, can);
                if (Gui.Hover(pr))
                {
                    Gui.Tooltip(UnitTip(u) + (can ? "\n\nDrag into the design, or click to add one." : "\n\nThe division is full."));
                    if (can && _dragType == null && Raylib.IsMouseButtonPressed(MouseButton.Left)) { _dragType = u; _dragFromGrid = false; _dragStart = Gui.Mouse; }
                }
                Gui.Block(pr);
                px += pw + 4 * S;
            }
            y += ph + 8 * S;
        }

        // ---- drag and drop --------------------------------------------------------------------
        if (_dragType != null)
        {
            bool moved = Vector2.Distance(_dragStart, Gui.Mouse) > 6 * S;
            if (moved)
            {
                var ghost = new Rectangle(Gui.Mouse.X - 28 * S, Gui.Mouse.Y - 18 * S, 56 * S, 36 * S);
                UnitTile(ghost, _dragType, 0, true);
            }
            if (!mouseDown)
            {
                bool overDrop = Raylib.CheckCollisionPointRec(Gui.Mouse, dropArea);
                if (!_dragFromGrid)
                {
                    // a click (no real drag) adds too, so both ways work in both modes
                    if ((overDrop || !moved) && total < max) AddBattalion(_dragType);
                }
                else if (moved && !overDrop) RemoveBattalion(_dragType);
                _dragType = null;
            }
        }

        // ---- stats ----------------------------------------------------------------------------
        float ey = y0;
        Gui.Section("Division", sx, ey, sw);
        ey += 26 * S;
        var preview = G.Designer.Preview(_designName, _design);
        if (preview.Units.Count > 0)
        {
            var shown = new[] { "max_strength", "max_organisation", "soft_attack", "hard_attack", "defense", "breakthrough", "piercing", "speed", "combat_width", "supply_use" }
                .Where(k => preview.Stats.ContainsKey(k)).ToList();
            foreach (var k in shown)
            {
                double? was = _designBase != null && _designBase.Stats.TryGetValue(k, out double w0) ? w0 : null;
                ey += Gui.StatRow(sx, ey, sw, Localisation.GetOrPretty(k), preview.Stats[k], k, was, k is "supply_use");
            }
            ey += 6 * S;
            Gui.Label($"Manpower {preview.Manpower:0}   Training {preview.BuildDays} days", sx, ey, 14);
            ey += 20 * S;
            if (G.Economy.GoldCosts) { Gui.Label($"Gold {preview.Cost:0}", sx, ey, 14); ey += 20 * S; }
            if (preview.Equipment.Count > 0)
            {
                ey += Gui.Wrapped("Equipment: " + string.Join(", ", preview.Equipment.Select(kv => $"{kv.Value:0} {G.Data.Equipment.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}")), sx, ey, sw, 13, Gui.TextDim);
                if (G.Production.Enabled)
                {
                    double share = G.Production.AvailableShare(player, preview.Equipment);
                    Gui.Label($"In stock: {share:P0}", sx, ey + 2 * S, 13, share >= 1 ? Gui.Good : Gui.Bad);
                }
            }
        }
        else Gui.Label("Add battalions to see the stats.", sx, ey, 14, Gui.TextDim);

        FitEnd("designer", r, Math.Max(y, ey) + 60 * S);

        // ---- actions --------------------------------------------------------------------------
        var reason = G.Designer.CannotSaveReason(player, _designName, _design);
        float by = r.Y + r.Height - 42 * S;
        if (reason != null) Gui.Label(reason, cx, by - 22 * S, 14, Gui.Bad);
        float bw = (right - cx - 3 * 4 * S) / 4;
        if (Gui.Button(new Rectangle(cx, by, bw, 30 * S), "Save new", reason == null, "Save as a new template", false, 14))
        {
            var t = G.Designer.Create(player, _designName, _design);
            if (t != null) { LoadDesign(t); Ui.SetStatus($"Template '{t.Name}' saved"); }
        }
        bool canEdit = _designBase != null && G.Designer.CanEdit(player, _designBase);
        if (Gui.Button(new Rectangle(cx + (bw + 4 * S), by, bw, 30 * S), "Update", reason == null && canEdit,
                canEdit ? "Change this template - its divisions change too" : "Only your own designs can be changed (save a new one)", reason == null && canEdit, 14))
            if (G.Designer.Update(player, _designBase!, _designName, _design)) Ui.SetStatus($"Template '{_designName}' updated");
        if (Gui.ConfirmButton("design-delete", new Rectangle(cx + 2 * (bw + 4 * S), by, bw, 30 * S), "Delete", canEdit, "Delete this template (click twice)"))
        {
            var err = G.Designer.Delete(player, _designBase!);
            Ui.SetStatus(err ?? "Template deleted");
            if (err == null) { _designBase = null; _design.Clear(); }
        }
        if (Gui.Button(new Rectangle(cx + 3 * (bw + 4 * S), by, bw, 30 * S), "Train", _designBase != null && reason == null, "Open the recruit menu with this template", false, 14))
        {
            Ui.Left = LeftPanel.Recruit;
            Ui.RecruitTemplate = _designBase;
        }
    }
}
