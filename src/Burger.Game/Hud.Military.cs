using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private Rectangle RightRect(float height)
    {
        float w = Gui.RightPanelWidth * S;
        return new Rectangle(Raylib.GetScreenWidth() - w - 8 * S, TopH + 8 * S, w, Math.Min(height, Raylib.GetScreenHeight() - TopH - 170 * S));
    }

    private void DrawMilitaryPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = FitRect("army");
        Gui.Panel(r, "");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;

        var armies = G.World.ArmiesOf(player).ToList();
        var unassigned = G.World.Divisions.Where(d => d.Owner == player && d.Army == null).ToList();
        var enemies = G.World.WarsOf(player).SelectMany(war => war.EnemiesOf(player)).Distinct().Where(e => e.Exists).ToList();
        // fronts can also be held at peace against any neighbour
        foreach (var nb in Burger.Core.Scripting.Builtins.NeighborCountries(G, player).OrderBy(n => n.Name))
            if (!enemies.Contains(nb)) enemies.Add(nb);

        Gui.Section($"Armies ({armies.Count})", x, y, w - 150 * S);
        if (Gui.Button(new Rectangle(r.X + r.Width - 140 * S, y - 2, 128 * S, 24 * S), "+ New army", true, "Create an empty army (select divisions and use 'To army' to fill it)", false, 14))
            Ui.SelectedArmy = G.Warfare.CreateArmy(player);
        y += 26 * S;

        bool plans = G.Features.Has(Burger.Core.Modding.FeatureSet.BattlePlans);
        float armyBase = 96 * S, armyPlan = plans ? 26 * S : 0, armyAdd = Ui.SelectedDivisions.Count > 0 ? 24 * S : 0;
        float HeightOf(Army a) => armyBase + (Ui.SelectedArmy == a ? armyPlan : 0) + armyAdd;
        float contentH = armies.Sum(a => HeightOf(a) + 6 * S) + (unassigned.Count > 0 ? 40 * S : 0);
        float listH = Math.Max(60 * S, Math.Min(contentH, r.Y + r.Height - y - 50 * S));
        FitEnd("army", r, y + contentH + 8 * S + 30 * S);
        float top = Gui.BeginScroll("armies", new Rectangle(x, y, w, listH), contentH);
        float ay = top;
        foreach (var army in armies)
        {
            float armyH = HeightOf(army);
            var box = new Rectangle(x, ay, w - 8 * S, armyH);
            bool sel = Ui.SelectedArmy == army;
            Card(box, sel ? new Color(70, 58, 34, 255) : new Color(36, 38, 44, 255), sel);
            bool attacking = army.Mode == ArmyMode.Offensive && army.FrontEnemy != null;
            Stripe(box, attacking ? Gui.Bad : army.FrontEnemy != null ? Gui.Gold : new Color(90, 96, 108, 255));
            Gui.Label(army.Name, x + 10 * S, ay + 4, 16, Gui.Gold, FontKind.Bold);
            bool chars = G.Features.Has(Burger.Core.Modding.FeatureSet.Characters);
            if (chars || army.General != null)
            {
                string general = army.General != null ? $"{army.General.DisplayName}  {army.General.GetStat(G.Data.Defines.GeneralStat):0}" : "No general";
                Gui.Label(Gui.Fit(general, w * 0.7f, 14), x + 10 * S, ay + 24 * S, 14, army.General != null ? Gui.Text : Gui.Bad);
                if (Gui.Hover(new Rectangle(x + 10 * S, ay + 24 * S, w * 0.6f, 18 * S)))
                    Gui.Tooltip(army.General != null ? $"General: {army.General.DisplayName}\n{Localisation.GetOrPretty(G.Data.Defines.GeneralStat)}: {army.General.GetStat(G.Data.Defines.GeneralStat):0}" : "This army has no general. Click 'General' to choose one.");
            }
            double str = army.Divisions.Count == 0 ? 0 : army.Divisions.Average(d => d.StrengthFraction);
            string stat = $"{army.Divisions.Count} div. · {str:P0}";
            Gui.Label(stat, x + w - 16 * S - Gui.Measure(stat, 14).X, ay + 4, 14, str < 0.5 ? Gui.Bad : Gui.TextDim);
            Gui.Bar(new Rectangle(x + w - 16 * S - 90 * S, ay + 24 * S, 90 * S, 7 * S), (float)str, str < 0.5 ? Gui.Bad : Gui.Good);
            if (Gui.Hover(new Rectangle(x + w - 160 * S, ay + 2, 150 * S, 30 * S))) Gui.Tooltip($"{army.Divisions.Count} divisions\nAverage strength {str:P0}");
            // orders line: a flag for the front, a pictogram for what the army is doing
            float fy = ay + 44 * S;
            if (army.Mode == ArmyMode.Fallback && army.Fallback.Count > 0)
                Gui.Label($"Fallback line ({army.Fallback.Count} locations)", x + 10 * S, fy, 14, Gui.TextDim);
            else if (army.FrontEnemy != null)
            {
                float fx0 = x + 10 * S + FlagChip(army.FrontEnemy, x + 10 * S, fy, 15 * S, attacking ? Gui.Bad : null, "Front against this country");
                Gui.Label(attacking ? "Attack" + (army.Objective != null ? " > " + army.Objective.DisplayName : "") : "Hold the front", fx0, fy, 14, attacking ? Gui.Bad : Gui.TextDim);
            }
            else if (army.MoveTarget != null) Gui.Label("Moving to " + army.MoveTarget.DisplayName, x + 10 * S, fy, 14, Gui.TextDim);
            else Gui.Label("No orders", x + 10 * S, fy, 14, Gui.TextDim);
            if (army.Divisions.Count > 0)
            {
                double sup = army.Divisions.Average(d => d.Supply);
                string where = $"{army.Divisions[0].Location.DisplayName} · supply {sup:P0}";
                Gui.Label(Gui.Fit(where, w * 0.45f, 13), x + w - 40 * S - Math.Min(w * 0.45f, Gui.Measure(where, 13).X), fy + 1 * S, 13, sup < 0.5 ? Gui.Bad : Gui.TextDim);
            }

            // one row of three buttons; clicking the name selects the army, x disbands it
            var head = new Rectangle(x, ay, w - 60 * S, 22 * S);
            if (Gui.Hover(head)) Gui.Tooltip("Select this army (right-click the map to move it)");
            if (Gui.Clicked(head))
            {
                Ui.SelectedArmy = army;
                Ui.SelectedDivisions.Clear();
                Ui.SelectedDivisions.AddRange(army.Divisions);
                if (army.Divisions.Count > 0) Cam.LookAt(Map.LocationPos(army.Divisions[0].Location));
            }
            if (Gui.ConfirmButton("disband-" + army.Name, new Rectangle(x + w - 8 * S - 26 * S, ay + 40 * S, 22 * S, 20 * S), "x", true, "Disband the army (divisions stay). Click twice.", 13))
            {
                if (Ui.SelectedArmy == army) Ui.SelectedArmy = null;
                G.Warfare.DisbandArmy(army);
                Gui.EndScroll();
                return;
            }
            float bh = 22 * S, gapB = 4 * S, bw3 = (w - 8 * S - 20 * S - 2 * gapB) / 3, bx = x + 10 * S, by = ay + 66 * S;
            if (Gui.Button(new Rectangle(bx, by, bw3, bh), "General", chars, chars ? "Choose the general leading this army" : "Generals need the characters feature", Ui.ChoosingGeneralFor == army, 13))
                Ui.ChoosingGeneralFor = Ui.ChoosingGeneralFor == army ? null : army;
            bx += bw3 + gapB;
            bool picking = Ui.PlanArmy == army && Ui.Tool == PlanTool.PickFront;
            string frontLabel = picking ? "Pick on map" : army.FrontEnemy == null ? "Front" : army.FrontEnemy.Tag;
            if (Gui.Button(new Rectangle(bx, by, bw3, bh), frontLabel, enemies.Count > 0,
                    enemies.Count > 0 ? "Click, then click the country on the map this army holds a front against (an enemy or a neighbour at peace).\nClick your own land to remove the front." : "You have no neighbours or enemies", picking, 13))
                TogglePlanTool(army, PlanTool.PickFront);
            bx += bw3 + gapB;
            string modeLabel = army.Mode switch { ArmyMode.Offensive => "Attack", ArmyMode.Fallback => "Fallback", _ => "Hold" };
            bool canFallback = plans && army.Fallback.Count > 0;
            if (Gui.Button(new Rectangle(bx, by, bw3, bh), modeLabel, army.FrontEnemy != null || canFallback,
                    "Click to change:\nHold: defend the front line\nAttack: divisions attack along the front" + (plans ? " (towards the arrow's objective if drawn)\nFallback: hold the fallback line" : " (the weakest enemy locations)"),
                    army.Mode == ArmyMode.Offensive, 13))
            {
                var next = army.Mode switch
                {
                    ArmyMode.Hold => army.FrontEnemy != null ? ArmyMode.Offensive : ArmyMode.Fallback,
                    ArmyMode.Offensive => canFallback ? ArmyMode.Fallback : ArmyMode.Hold,
                    _ => ArmyMode.Hold,
                };
                G.Warfare.SetMode(army, next);
            }
            float ry = ay + 92 * S;
            if (plans && sel)
            {
                // battle plan tools only for the selected army: four equal buttons
                float pw = (w - 8 * S - 20 * S - 3 * gapB) / 4, px = x + 10 * S;
                bool planning = Ui.PlanArmy == army;
                if (Gui.Button(new Rectangle(px, ry, pw, bh), army.FrontLimit.Count > 0 ? $"Front ({army.FrontLimit.Count})" : "Front line", army.FrontEnemy != null,
                        army.FrontEnemy != null ? "Click border locations on the map to limit the front to that part of the border (click again to remove)" : "Pick an enemy with 'Front' first", planning && Ui.Tool == PlanTool.Front, 12))
                    TogglePlanTool(army, PlanTool.Front);
                px += pw + gapB;
                if (Gui.Button(new Rectangle(px, ry, pw, bh), army.Objective != null ? "Arrow: set" : "Arrow", army.FrontEnemy != null,
                        army.Objective != null ? $"Offensive objective: {army.Objective.DisplayName}\nClick to draw a new arrow" : "Click an enemy location as the offensive objective; set the army to Attack to execute", planning && Ui.Tool == PlanTool.Arrow, 12))
                    TogglePlanTool(army, PlanTool.Arrow);
                px += pw + gapB;
                if (Gui.Button(new Rectangle(px, ry, pw, bh), army.Fallback.Count > 0 ? $"Retreat ({army.Fallback.Count})" : "Retreat", true,
                        "Click your own locations to draw a fallback line (click again to remove); set the army to Fallback to hold it", planning && Ui.Tool == PlanTool.Fallback, 12))
                    TogglePlanTool(army, PlanTool.Fallback);
                px += pw + gapB;
                if (Gui.Button(new Rectangle(px, ry, pw, bh), "Clear", army.FrontLimit.Count > 0 || army.Objective != null || army.Fallback.Count > 0, "Remove the drawn front, arrow and fallback line", false, 12))
                {
                    army.FrontLimit.Clear();
                    army.Objective = null;
                    army.Fallback.Clear();
                    if (Ui.PlanArmy == army) Ui.Tool = PlanTool.None;
                    G.Warfare.SetMode(army, army.Mode == ArmyMode.Fallback ? ArmyMode.Hold : army.Mode);
                }
                ry += bh + 4 * S;
            }
            if (Ui.SelectedDivisions.Count > 0 && Gui.Button(new Rectangle(x + 6, ry, 190 * S, 20 * S), $"Add {Ui.SelectedDivisions.Count} selected division(s)", true, null, false, 12))
                foreach (var d in Ui.SelectedDivisions) G.Warfare.AssignToArmy(d, army);
            ay += armyH + 6 * S;
        }
        if (unassigned.Count > 0)
        {
            if (Gui.Button(new Rectangle(x, ay + 4, w - 8 * S, 28 * S), $"{unassigned.Count} division(s) without army - select them", true, null, false, 14))
            {
                Ui.SelectedDivisions.Clear();
                Ui.SelectedDivisions.AddRange(unassigned);
                Ui.SelectedArmy = null;
            }
        }
        Gui.EndScroll();
        y += listH + 8 * S;

        // new divisions are made in the recruit menu only
        string trainLabel = player.Recruitment.Count > 0 ? $"Recruit & train ({player.Recruitment.Count} in training)" : "Recruit & train...";
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), trainLabel, true, "Open the recruit menu (R)", false, 15)) { Ui.Left = LeftPanel.Recruit; return; }

        if (Ui.ChoosingGeneralFor != null) DrawGeneralChooser(Ui.ChoosingGeneralFor, r);
    }

    /// <summary>
    /// The country a click on <paramref name="loc"/> gives an army a front against: an enemy (also one holding our land) or a
    /// neighbour; the army's own country means "no front"; null when the location belongs to neither.
    /// </summary>
    public Country? FrontTargetOf(Army army, Location loc)
    {
        var me = army.Owner;
        if (loc.Controller is { } ctl && ctl != me && G.World.AtWar(me, ctl)) return ctl;
        var owner = loc.Owner;
        if (owner == null) return null;
        if (owner == me) return me;
        if (G.World.AtWar(me, owner)) return owner;
        return Burger.Core.Scripting.Builtins.NeighborCountries(G, me).Contains(owner) ? owner : null;
    }

    private void TogglePlanTool(Army army, PlanTool tool)
    {
        if (Ui.PlanArmy == army && Ui.Tool == tool) { Ui.Tool = PlanTool.None; Ui.PlanArmy = null; Ui.SetStatus("Done drawing"); return; }
        Ui.PlanArmy = army;
        Ui.Tool = tool;
        Ui.SelectedArmy = army;
        Ui.SetStatus(tool switch
        {
            PlanTool.Front => "Click border locations to draw the front (click again to remove). Right-click or Esc when done.",
            PlanTool.Arrow => "Click the objective of the offensive. Right-click or Esc to cancel.",
            PlanTool.PickFront => $"Click the country {army.Name} should hold a front against (your own land: no front). Right-click or Esc to cancel.",
            _ => "Click your locations to draw the fallback line (click again to remove). Right-click or Esc when done.",
        });
    }

    /// <summary>Map click while a battle plan tool is active.</summary>
    private void PlanMapClick(Army army, Location loc)
    {
        if (!G.World.Armies.Contains(army)) { Ui.PlanArmy = null; Ui.Tool = PlanTool.None; return; }
        switch (Ui.Tool)
        {
            case PlanTool.Front:
                if (!loc.IsLand) return;
                G.Warfare.ToggleFrontLocation(army, loc);
                Ui.SetStatus(army.FrontLimit.Contains(loc) ? $"{loc.DisplayName} added to the front ({army.FrontPairs.Count} border sections)" : $"{loc.DisplayName} removed from the front");
                break;
            case PlanTool.Arrow:
                if (!loc.IsLand) return;
                army.Objective = loc;
                Ui.Tool = PlanTool.None;
                Ui.PlanArmy = null;
                Ui.SetStatus(army.Mode == ArmyMode.Offensive ? $"{army.Name} attacks towards {loc.DisplayName}" : $"Objective: {loc.DisplayName}. Set the army to Attack to execute the plan.");
                break;
            case PlanTool.PickFront:
                {
                    if (!loc.IsLand) return;
                    var target = FrontTargetOf(army, loc);
                    if (target == null) { Ui.SetStatus($"{loc.Owner?.Name ?? loc.DisplayName} is not an enemy or a neighbour"); return; }
                    Ui.Tool = PlanTool.None;
                    Ui.PlanArmy = null;
                    if (target == army.Owner)
                    {
                        G.Warfare.SetFront(army, null, ArmyMode.Hold);
                        Ui.SetStatus($"{army.Name} has no front");
                    }
                    else
                    {
                        G.Warfare.SetFront(army, target, army.Mode == ArmyMode.Fallback ? ArmyMode.Hold : army.Mode);
                        Ui.SetStatus($"{army.Name} holds a front against {target.Name}");
                    }
                    break;
                }
            case PlanTool.Fallback:
                if (!loc.IsLand || loc.Controller != army.Owner && (loc.Controller == null || G.World.AtWar(army.Owner, loc.Controller))) { Ui.SetStatus("The fallback line must be on land you hold"); return; }
                G.Warfare.ToggleFallbackLocation(army, loc);
                break;
        }
    }

    private void DrawGeneralChooser(Army army, Rectangle anchor)
    {
        var player = G.World.Player!;
        string stat = G.Data.Defines.GeneralStat;
        var now = G.World.Date;
        var candidates = G.World.CharactersOf(player)
            .Where(c => c.Age(now) >= G.Data.Defines.AdultAge)
            .OrderByDescending(c => c.IsGeneral).ThenByDescending(c => c.GetStat(stat)).ToList();
        float w = 330 * S, h = Math.Min(460 * S, 70 * S + candidates.Count * 30 * S);
        var r = new Rectangle(anchor.X + anchor.Width + 8 * S, anchor.Y, w, h);
        Gui.Panel(r, $"General for {army.Name}");
        if (Gui.CloseButton(r)) { Ui.ChoosingGeneralFor = null; return; }
        float y = Gui.BeginScroll("generals", new Rectangle(r.X + 8, r.Y + 34 * S, w - 16, h - 42 * S), candidates.Count * 30 * S + 32 * S);
        if (Gui.Button(new Rectangle(r.X + 8, y, w - 30, 26 * S), "(no general)", true, null, army.General == null, 14))
        {
            G.Warfare.SetGeneral(army, null);
            Ui.ChoosingGeneralFor = null;
        }
        y += 30 * S;
        foreach (var c in candidates)
        {
            var busy = G.World.Armies.FirstOrDefault(a => a.General == c && a != army);
            string label = $"{c.DisplayName}  {Localisation.GetOrPretty(stat)} {c.GetStat(stat):0}{(c.IsRuler ? " (ruler)" : "")}{(busy != null ? " *" : "")}";
            string tip = $"Age {c.Age(now)}\nTraits: {string.Join(", ", c.Traits.Select(t => Localisation.Get(t.Name)))}" +
                         (busy != null ? $"\nCurrently leads {busy.Name}" : "");
            if (Gui.Button(new Rectangle(r.X + 8, y, w - 30, 26 * S), label, true, tip, army.General == c, 14))
            {
                G.Warfare.SetGeneral(army, c);
                Ui.ChoosingGeneralFor = null;
            }
            y += 30 * S;
        }
        Gui.EndScroll();
    }
}
