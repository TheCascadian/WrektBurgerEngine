using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Simulation;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawWarsPanel()
    {
        var wars = _app.Snapshot.Wars.OrderByDescending(w => w.IsMine).ToList();
        var r = RightRect(2000);
        Gui.Panel(r, "Wars");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        if (wars.Count == 0) { Gui.Label("The world is at peace.", x, y, 16, Gui.TextDim); return; }
        var country = new Dictionary<string, Country?>(StringComparer.OrdinalIgnoreCase);
        Country? Live(string tag)
        {
            if (!country.TryGetValue(tag, out var c)) country[tag] = c = G.World.Countries.FirstOrDefault(v => v.Tag == tag);
            return c;
        }
        float CardH(RenderWar war) => (212 + Math.Clamp(war.Goals.Length, 1, 3) * 16 + 10 + (war.IsMine ? 32 : 0)) * S;
        float top = Gui.BeginScroll("wars", new Rectangle(x, y, w, r.Height - 46 * S), wars.Sum(war => CardH(war) + 8 * S));
        float wy = top;
        foreach (var war in wars)
        {
            float cardH = CardH(war);
            var box = new Rectangle(x, wy, w - 8 * S, cardH);
            Card(box, war.IsMine ? new Color(52, 34, 32, 255) : new Color(34, 36, 42, 255), war.IsMine);
            float ix = x + 10 * S, iw = w - 28 * S;
            var attacker = war.Attackers.First(c => c.IsLeader);
            var defender = war.Defenders.First(c => c.IsLeader);
            Color atkCol = C(attacker.Color), defCol = C(defender.Color);
            string since = war.StartDate.ToDisplay();
            float sinceW = Gui.Measure(since, 13).X;
            Gui.Label(Gui.Fit(war.Name, iw - sinceW - 10 * S, 16, FontKind.Bold), ix, wy + 6 * S, 16, Gui.Gold, FontKind.Bold);
            Gui.Label(since, ix + iw - sinceW, wy + 8 * S, 13, Gui.TextDim);
            Raylib.DrawRectangle((int)ix, (int)(wy + 29 * S), (int)iw, 1, new Color(255, 255, 255, 28));
            float ry = wy + 36 * S;
            foreach (var (label, col, members) in new[] { ("ATTACK", atkCol, war.Attackers), ("DEFEND", defCol, war.Defenders) })
            {
                Raylib.DrawRectangle((int)ix, (int)(ry + 2 * S), (int)(4 * S), (int)(14 * S), col);
                Gui.Label(label, ix + 10 * S, ry + 2 * S, 12, Gui.TextDim, FontKind.Bold);
                float fw = 24 * S, fh = 16 * S, fx = ix + 66 * S, avail = iw - 66 * S;
                int fit = Math.Max(1, (int)(avail / (fw + 4 * S)));
                int shown = Math.Min(fit, members.Length);
                for (int i = 0; i < shown; i++)
                {
                    var member = members[i];
                    var fr = new Rectangle(fx + i * (fw + 4 * S), ry + 1 * S, fw, fh);
                    if (Live(member.Tag) is { } live) Gui.Flag(live, fr);
                    Raylib.DrawRectangleLinesEx(fr, member.IsLeader ? 2 : 1, member.IsLeader ? Gui.Gold : new Color(0, 0, 0, 160));
                    if (Gui.Hover(fr)) Gui.Tooltip($"{member.Name} ({member.Tag})\n{(member.IsLeader ? "War leader" : "Ally")}\nExhaustion: {member.Exhaustion:0.0}");
                }
                if (members.Length > shown) Gui.Label($"+{members.Length - shown}", fx + shown * (fw + 4 * S), ry + 1 * S, 14, Gui.TextDim, FontKind.Bold);
                ry += 20 * S;
            }
            string aTag = war.AttackerLeaderTag, dTag = war.DefenderLeaderTag;
            Gui.Icon("ui_war_score", new Rectangle(ix, wy + 78 * S, 14 * S, 14 * S));
            Gui.Label("WAR SCORE", ix + 18 * S, wy + 80 * S, 11, Gui.TextDim, FontKind.Bold);
            var bar = new Rectangle(ix, wy + 96 * S, iw, 16 * S);
            Raylib.DrawRectangleRec(bar, new Color(18, 19, 22, 255));
            float score = Math.Clamp((float)war.WarScore / 100f, -1f, 1f), mid = bar.X + bar.Width / 2;
            var fill = score >= 0 ? new Rectangle(mid, bar.Y, bar.Width / 2 * score, bar.Height) : new Rectangle(mid + bar.Width / 2 * score, bar.Y, -bar.Width / 2 * score, bar.Height);
            Raylib.DrawRectangleRec(fill, score >= 0 ? atkCol : defCol);
            Raylib.DrawRectangleLinesEx(bar, 1, new Color(255, 255, 255, 40));
            Raylib.DrawLine((int)mid, (int)bar.Y - 2, (int)mid, (int)(bar.Y + bar.Height + 2), Color.White);
            Gui.Label($"{dTag} {Math.Max(0, -war.WarScore):0}", bar.X + 4, bar.Y + 1 * S, 13, Color.White, FontKind.Bold);
            string rightScore = $"{aTag} {Math.Max(0, war.WarScore):0}";
            Gui.Label(rightScore, bar.X + bar.Width - Gui.Measure(rightScore, 13, FontKind.Bold).X - 4, bar.Y + 1 * S, 13, Color.White, FontKind.Bold);
            string verdict = Math.Abs(war.WarScore) < 5 ? "Undecided - neither side is ahead" : war.IsMine ? ($"We are {(war.Attackers.Any(c => c.Tag == _app.Snapshot.PlayerTag) == war.WarScore > 0 ? "winning" : "losing")} by {Math.Abs(war.WarScore):0}") : (war.WarScore > 0 ? $"Attackers ({aTag}) ahead by {war.WarScore:0}" : $"Defenders ({dTag}) ahead by {-war.WarScore:0}");
            Gui.Label(verdict, ix, wy + 116 * S, 14, war.IsMine && war.WarScore < 0 ? Gui.Bad : Gui.Text, FontKind.Bold);
            int still = (_app.Snapshot.Date.TotalDays - war.LastChangeDay) / 30;
            Gui.Icon("ui_exhaustion", new Rectangle(ix, wy + 136 * S, 14 * S, 14 * S));
            Gui.Label("EXHAUSTION", ix + 18 * S, wy + 138 * S, 11, Gui.TextDim, FontKind.Bold);
            float colW = (iw - 12 * S) / 2;
            for (int ci = 0; ci < 2; ci++)
            {
                var member = ci == 0 ? attacker : defender;
                float cx = ix + ci * (colW + 12 * S);
                Gui.Label($"{member.Tag} {member.Exhaustion:0.0}", cx, wy + 152 * S, 13, member.Exhaustion >= war.MaxExhaustion * 0.5 ? Gui.Bad : Gui.Text);
                var eb = new Rectangle(cx, wy + 170 * S, colW, 6 * S);
                Raylib.DrawRectangleRec(eb, new Color(18, 19, 22, 255));
                Raylib.DrawRectangleRec(new Rectangle(eb.X, eb.Y, eb.Width * (float)Math.Clamp(member.Exhaustion / war.MaxExhaustion, 0, 1), eb.Height), member.Exhaustion >= war.MaxExhaustion * 0.5 ? Gui.Bad : ci == 0 ? atkCol : defCol);
            }
            if (still >= 6) Gui.Label($"Front has not moved for {still} months", ix + 18 * S, wy + 180 * S, 12, still >= 12 ? Gui.Bad : Gui.TextDim);
            Gui.Icon("ui_war_goals", new Rectangle(ix, wy + 194 * S, 14 * S, 14 * S));
            Gui.Label("WAR GOALS", ix + 18 * S, wy + 196 * S, 11, Gui.TextDim, FontKind.Bold);
            for (int i = 0; i < Math.Min(war.Goals.Length, 3); i++)
            {
                var goal = war.Goals[i];
                float gy = wy + (212 + i * 16) * S;
                if (i == 2 && war.Goals.Length > 3) { Gui.Label($"+{war.Goals.Length - 2} more war goals", ix, gy, 13, Gui.TextDim); break; }
                if (Live(goal.HolderTag) is { } holder) Gui.Flag(holder, new Rectangle(ix, gy + 1 * S, 20 * S, 13 * S));
                Gui.Label(Gui.Fit($"{Localisation.Get(goal.CasusBelliName)} vs {goal.TargetTag}  ({goal.GoalStateCount} states)", iw - 28 * S, 13), ix + 26 * S, gy, 13, Gui.Text);
            }
            if (war.IsMine && Gui.Button(new Rectangle(ix, wy + cardH - 34 * S, 150 * S, 24 * S), "Negotiate peace", !war.CivilWar, war.CivilWar ? "A civil war ends only when one side is defeated" : $"Send a peace offer to {war.DefenderLeaderName}", false, 14))
            {
                var liveWar = G.World.Wars.FirstOrDefault(v => v.Id == war.Id);
                var enemy = liveWar == null ? null : Live(war.AttackerLeaderTag == _app.Snapshot.PlayerTag ? war.DefenderLeaderTag : war.AttackerLeaderTag);
                if (liveWar != null && enemy != null) OpenPeace(liveWar, enemy);
            }
            wy += cardH + 8 * S;
        }
        Gui.EndScroll();
    }

    private void DrawWarsPanelLegacy()
    {
        var player = G.World.Player;
        var r = RightRect(2000);
        Gui.Panel(r, "Wars");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        var wars = G.World.Wars.OrderByDescending(war => player != null && war.Involves(player)).ToList();
        if (wars.Count == 0) Gui.Label("The world is at peace.", x, y, 16, Gui.TextDim);
        float CardH(War wr) => (212 + Math.Clamp(wr.Goals.Count, 1, 3) * 16 + 10 + (player != null && wr.Involves(player) ? 32 : 0)) * S;
        float top = Gui.BeginScroll("wars", new Rectangle(x, y, w, r.Height - 46 * S), wars.Sum(wr => CardH(wr) + 8 * S));
        float wy = top;
        foreach (var war in wars)
        {
            bool mine = player != null && war.Involves(player);
            float cardH = CardH(war);
            var box = new Rectangle(x, wy, w - 8 * S, cardH);
            Card(box, mine ? new Color(52, 34, 32, 255) : new Color(34, 36, 42, 255), mine);
            float ix = x + 10 * S, iw = w - 28 * S;
            Color atkCol = C(war.AttackerLeader.Color), defCol = C(war.DefenderLeader.Color);
            // header: name left, start date right
            string since = war.StartDate.ToDisplay();
            float sinceW = Gui.Measure(since, 13).X;
            Gui.Label(Gui.Fit(war.Name, iw - sinceW - 10 * S, 16, FontKind.Bold), ix, wy + 6 * S, 16, Gui.Gold, FontKind.Bold);
            Gui.Label(since, ix + iw - sinceW, wy + 8 * S, 13, Gui.TextDim);
            Raylib.DrawRectangle((int)ix, (int)(wy + 29 * S), (int)iw, 1, new Color(255, 255, 255, 28));
            // sides
            float ry = wy + 36 * S;
            foreach (var (label, col, members, leader) in new[] { ("ATTACK", atkCol, war.Attackers, war.AttackerLeader), ("DEFEND", defCol, war.Defenders, war.DefenderLeader) })
            {
                Raylib.DrawRectangle((int)ix, (int)(ry + 2 * S), (int)(4 * S), (int)(14 * S), col);
                Gui.Label(label, ix + 10 * S, ry + 2 * S, 12, Gui.TextDim, FontKind.Bold);
                float fw = 24 * S, fh = 16 * S, fx = ix + 66 * S, avail = iw - 66 * S;
                int fit = Math.Max(1, (int)(avail / (fw + 4 * S)));
                var list = members.OrderByDescending(c => c == leader).ToList();
                bool more = list.Count > fit;
                int shown = more ? fit - 1 : list.Count;
                for (int i = 0; i < shown; i++)
                {
                    var c = list[i];
                    var fr = new Rectangle(fx + i * (fw + 4 * S), ry + 1 * S, fw, fh);
                    Gui.Flag(c, fr);
                    Raylib.DrawRectangleLinesEx(fr, c == leader ? 2 : 1, c == leader ? Gui.Gold : new Color(0, 0, 0, 160));
                    if (Gui.Hover(fr))
                    {
                        var goal = war.Goals.Where(g => g.Holder == c).Select(g => Localisation.Get(g.CasusBelli.Name)).FirstOrDefault();
                        Gui.Tooltip($"{c.Name} ({c.Tag})\n{(c == leader ? "War leader" : "Ally")} - {label.ToLower().Replace("attack", "attacking").Replace("defend", "defending")}" +
                            $"\nExhaustion: {G.Diplomacy.WarExhaustion(c):0.0}" + (goal != null ? $"\nWar goal: {goal}" : "") + (c == player ? "\nThis is you" : ""));
                    }
                }
                if (more)
                {
                    var mr = new Rectangle(fx + shown * (fw + 4 * S), ry, fw + 10 * S, fh);
                    Gui.Label($"+{list.Count - shown}", mr.X + 2, ry + 1 * S, 14, Gui.TextDim, FontKind.Bold);
                    if (Gui.Hover(mr)) Gui.Tooltip(string.Join("\n", list.Skip(shown).Select(c => c.Name)));
                }
                ry += 20 * S;
            }
            // war score bar: centre = 0, attacker side fills right, defender side fills left
            string aTag = war.AttackerLeader.Tag, dTag = war.DefenderLeader.Tag;
            Gui.Icon("ui_war_score", new Rectangle(ix, wy + 78 * S, 14 * S, 14 * S));
            Gui.Label("WAR SCORE", ix + 18 * S, wy + 80 * S, 11, Gui.TextDim, FontKind.Bold);
            var bar = new Rectangle(ix, wy + 96 * S, iw, 16 * S);
            Raylib.DrawRectangleRec(bar, new Color(18, 19, 22, 255));
            float score = Math.Clamp((float)war.WarScore / 100f, -1f, 1f);
            float mid = bar.X + bar.Width / 2;
            var fill = score >= 0 ? new Rectangle(mid, bar.Y, bar.Width / 2 * score, bar.Height) : new Rectangle(mid + bar.Width / 2 * score, bar.Y, -bar.Width / 2 * score, bar.Height);
            Raylib.DrawRectangleRec(fill, score >= 0 ? atkCol : defCol);
            Raylib.DrawRectangleLinesEx(bar, 1, new Color(255, 255, 255, 40));
            Raylib.DrawLine((int)mid, (int)bar.Y - 2, (int)mid, (int)(bar.Y + bar.Height + 2), Color.White);
            // numbers: attacker's end shows its lead, defender's end the other way
            double wsA = war.WarScore;
            Gui.Label($"{dTag} {Math.Max(0, -wsA):0}", bar.X + 4, bar.Y + 1 * S, 13, Color.White, FontKind.Bold);
            string rtxt = $"{aTag} {Math.Max(0, wsA):0}";
            Gui.Label(rtxt, bar.X + bar.Width - Gui.Measure(rtxt, 13, FontKind.Bold).X - 4, bar.Y + 1 * S, 13, Color.White, FontKind.Bold);
            string verdict; Color vcol;
            double mineScore = mine ? war.ScoreFor(player!) : wsA;
            if (Math.Abs(wsA) < 5) { verdict = "Undecided - neither side is ahead"; vcol = Gui.TextDim; }
            else if (mine) { verdict = mineScore > 0 ? $"We are winning by {mineScore:0}" : $"We are losing by {-mineScore:0}"; vcol = mineScore > 0 ? Gui.Good : Gui.Bad; }
            else { verdict = wsA > 0 ? $"Attackers ({aTag}) ahead by {wsA:0}" : $"Defenders ({dTag}) ahead by {-wsA:0}"; vcol = Gui.Text; }
            Gui.Label(verdict, ix, wy + 116 * S, 14, vcol, FontKind.Bold);
            var sr = new Rectangle(ix, wy + 80 * S, iw, 50 * S);
            if (Gui.Hover(sr)) Gui.Tooltip($"War score runs from -100 to +100. At +100 the attacker can demand everything.\nOccupation, battles and holding the war goal decide it.\nBattle score (attacker): {war.BattleScore:0.0} (capped at {G.Data.Defines.WarScoreBattleCap:0})\nWar goal held (attacker): {war.TickingScore:+0;-0;0}");
            // exhaustion: one mini bar per leader
            int still = (G.World.Date.TotalDays - war.LastChangeDay) / 30;
            double maxEx = G.Diplomacy.MaxWarExhaustion;
            Gui.Icon("ui_exhaustion", new Rectangle(ix, wy + 136 * S, 14 * S, 14 * S));
            Gui.Label("EXHAUSTION", ix + 18 * S, wy + 138 * S, 11, Gui.TextDim, FontKind.Bold);
            float colW = (iw - 12 * S) / 2;
            int ci = 0;
            foreach (var (tag, ex, col) in new[] { (aTag, G.Diplomacy.WarExhaustion(war.AttackerLeader), atkCol), (dTag, G.Diplomacy.WarExhaustion(war.DefenderLeader), defCol) })
            {
                float cx = ix + ci++ * (colW + 12 * S);
                Gui.Label($"{tag} {ex:0.0}", cx, wy + 152 * S, 13, ex >= maxEx * 0.5 ? Gui.Bad : Gui.Text);
                var eb = new Rectangle(cx, wy + 170 * S, colW, 6 * S);
                Raylib.DrawRectangleRec(eb, new Color(18, 19, 22, 255));
                Raylib.DrawRectangleRec(new Rectangle(eb.X, eb.Y, eb.Width * (float)Math.Clamp(ex / maxEx, 0, 1), eb.Height), ex >= maxEx * 0.5 ? Gui.Bad : col);
            }
            var er = new Rectangle(ix, wy + 138 * S, iw, 40 * S);
            if (Gui.Hover(er)) Gui.Tooltip($"War exhaustion (0-{maxEx:0}) grows while at war, with enemy occupation, losses in battle and a front that does not move.\nIt lowers stability, manpower and political power, and makes countries accept peace sooner.\nBars turn red past half.");
            if (still >= 6) Gui.Icon("ui_stalemate", new Rectangle(ix, wy + 179 * S, 14 * S, 14 * S));
            if (still >= 6) Gui.Label($"Front has not moved for {still} months", ix + 18 * S, wy + 180 * S, 12, still >= 12 ? Gui.Bad : Gui.TextDim);
            // war goals: one line per goal, holder's flag first
            Gui.Icon("ui_war_goals", new Rectangle(ix, wy + 194 * S, 14 * S, 14 * S));
            Gui.Label("WAR GOALS", ix + 18 * S, wy + 196 * S, 11, Gui.TextDim, FontKind.Bold);
            if (war.Goals.Count == 0) Gui.Label("No war goal set", ix, wy + 212 * S, 13, Gui.TextDim);
            for (int i = 0; i < Math.Min(war.Goals.Count, 3); i++)
            {
                var g = war.Goals[i];
                float gy = wy + (212 + i * 16) * S;
                if (i == 2 && war.Goals.Count > 3) { Gui.Label($"+{war.Goals.Count - 2} more war goals", ix, gy, 13, Gui.TextDim); break; }
                var fr = new Rectangle(ix, gy + 1 * S, 20 * S, 13 * S);
                Gui.Flag(g.Holder, fr);
                Raylib.DrawRectangleLinesEx(fr, 1, new Color(0, 0, 0, 160));
                var line = new Rectangle(ix, gy, iw, 16 * S);
                Gui.Label(Gui.Fit($"{Localisation.Get(g.CasusBelli.Name)} vs {g.Target.Tag}  ({G.Diplomacy.GoalStates(g).Count} states)", iw - 28 * S, 13), ix + 26 * S, gy, 13, Gui.Text);
                if (Gui.Hover(line))
                    Gui.Tooltip($"{g.Holder.Name} - {Localisation.Get(g.CasusBelli.Name)} against {g.Target.Name}\n{G.Diplomacy.GoalStates(g).Count} goal states; matching demands cost {g.CasusBelli.GoalCostMultiplier:P0}" +
                        (g.CasusBelli.TickingWarScore ? "\nHolding them adds war score." : "") + "\nAllies that join add their own war goals.");
            }
            if (mine)
            {
                var enemyLeader = war.EnemyLeaderOf(player!);
                if (Gui.Button(new Rectangle(ix, wy + cardH - 34 * S, 150 * S, 24 * S), "Negotiate peace", !war.Vars.HasFlag("civil_war"),
                        war.Vars.HasFlag("civil_war") ? "A civil war ends only when one side is defeated" : $"Send a peace offer to {enemyLeader.Name}", false, 14))
                    OpenPeace(war, enemyLeader);
            }
            wy += cardH + 8 * S;
        }
        Gui.EndScroll();
    }

    public void OpenPeaceFromOutside(War war, Country enemy) => OpenPeace(war, enemy);

    private void OpenPeace(War war, Country enemy)
    {
        var player = G.World.Player!;
        Ui.Peace = new PeaceDraft { War = war, Enemy = enemy, Concede = war.ScoreFor(player) < 0 };
        Ui.Right = RightPanel.Peace;
        Map.Mode = MapMode.Political;
        Map.Dirty = true;
        Ui.SetStatus("Click enemy states on the map (or in the list) to demand them");
    }

    private void TogglePeaceState(State s)
    {
        var p = Ui.Peace;
        if (p == null) return;
        var loserSide = p.Concede ? p.War.SideOf(G.World.Player!) : p.War.SideOf(p.Enemy);
        if (s.Owner == null || !loserSide.Contains(s.Owner)) { Ui.SetStatus($"{s.DisplayName} is not owned by the {(p.Concede ? "our" : "enemy")} side"); return; }
        if (!p.States.Remove(s)) p.States.Add(s);
        Map.HighlightState = s;
        Map.Dirty = true;
    }

    /// <summary>Who on the winning side receives a state: the member whose war goal it is, else a claimant, else the war leader.</summary>
    private Country StateReceiver(War war, State s, Country winner)
    {
        var side = war.SideOf(winner);
        foreach (var g in war.Goals) if (side.Contains(g.Holder) && G.Diplomacy.GoalStates(g).Contains(s)) return g.Holder;
        return side.FirstOrDefault(c => s.Cores.Contains(c)) ?? side.FirstOrDefault(c => s.Claims.Contains(c)) ?? winner;
    }

    private PeaceOffer BuildOffer(PeaceDraft p)
    {
        var player = G.World.Player!;
        var winner = p.Concede ? p.Enemy : player;
        var loser = p.Concede ? player : p.Enemy;
        var offer = new PeaceOffer { War = p.War, From = player, To = p.Enemy, FromIsWinner = !p.Concede };
        if (p.Annex) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner });
        else
        {
            foreach (var s in p.States) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = StateReceiver(p.War, s, winner) });
            if (p.Subjugate) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = G.Data.SubjectTypes.Keys.First() });
            foreach (var cu in p.Release)
            {
                var st = p.War.SideOf(loser).SelectMany(c => c.OwnedStates).FirstOrDefault(s => G.Population.MajorityCulture(s) == cu && s != s.Owner?.Capital?.State);
                if (st != null) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = st, Culture = cu, Beneficiary = winner });
            }
        }
        if (p.FreeSubjects)
            foreach (var c in p.War.SideOf(winner).Where(c => G.World.Relations.OverlordOf(c) is { } o && p.War.SideOf(loser).Contains(o)))
                offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = c });
        if (p.RegimeChange && G.Politics.Enabled) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = G.Politics.Of(winner).Ruling });
        if (p.Humiliate) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Humiliate, Beneficiary = winner });
        foreach (var s in p.Demilitarize.Where(s => s.Owner != null && !p.States.Contains(s))) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Demilitarize, State = s, Beneficiary = winner });
        if (p.Gold > 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Gold, Amount = p.Gold, Beneficiary = winner });
        if (offer.Terms.Count == 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
        return offer;
    }

    private void DrawPeacePanel()
    {
        var p = Ui.Peace;
        var player = G.World.Player;
        if (p == null || player == null || !G.World.Wars.Contains(p.War)) { Ui.Right = RightPanel.Wars; Ui.Peace = null; return; }
        var r = RightRect(2000);
        Gui.Panel(r, $"Peace with {p.Enemy.Name}");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.Wars; Ui.Peace = null; Map.HighlightState = null; Map.Dirty = true; return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        double score = p.War.ScoreFor(player);
        Gui.Label($"War score for us: {score:0}   exhaustion {G.Diplomacy.WarExhaustion(player):0.0} / {G.Diplomacy.WarExhaustion(p.Enemy):0.0}", x, y, 17, score >= 0 ? Gui.Good : Gui.Bad);
        y += 26 * S;
        if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 26 * S), "We demand", true, "Ask the enemy to give things up", !p.Concede, 14)) { p.Concede = false; p.States.Clear(); }
        if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 26 * S), "We concede", true, "Offer to give things up to end the war", p.Concede, 14)) { p.Concede = true; p.States.Clear(); }
        y += 34 * S;

        var loserSide = p.Concede ? p.War.SideOf(player) : p.War.SideOf(p.Enemy);
        var states = loserSide.SelectMany(c => c.OwnedStates).OrderBy(s => s.Owner!.Tag).ThenBy(s => s.DisplayName).ToList();
        var offer = BuildOffer(p);
        var (winner, loser) = G.Diplomacy.WinnerLoser(offer);
        Gui.Label("States (click here or on the map):", x, y, 15, Gui.TextDim);
        y += 22 * S;
        float listH = r.Height * 0.34f;
        float top = Gui.BeginScroll("peace", new Rectangle(x, y, w, listH), states.Count * 26 * S);
        float sy = top;
        foreach (var s in states)
        {
            var receiver = StateReceiver(p.War, s, winner);
            double cost = G.Rules.Diplomacy.PeaceTermCost(G, p.War, new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = receiver }, winner, s.Owner!);
            double held = s.Locations.Count(l => l.Controller != null && p.War.SideOf(winner).Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count);
            bool goal = G.Diplomacy.GoalMultiplier(p.War, s, receiver) < 1;
            string label = $"{(goal ? "* " : "")}{s.DisplayName} ({s.Owner!.Tag})  cost {cost:0}  occupied {held:P0}" + (receiver != winner ? $"  to {receiver.Tag}" : "");
            if (Gui.Button(new Rectangle(x, sy, w - 10 * S, 23 * S), label, !p.Annex, null, p.States.Contains(s), 13)) TogglePeaceState(s);
            sy += 26 * S;
        }
        Gui.EndScroll();
        y += listH + 8 * S;
        if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 26 * S), "Subjugate", !p.Annex, $"{loser.Name} becomes a subject of {winner.Name}", p.Subjugate, 14)) p.Subjugate = !p.Subjugate;
        if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 26 * S), "Annex", true, $"{winner.Name} takes everything from {loser.Name}", p.Annex, 14)) p.Annex = !p.Annex;
        y += 32 * S;
        // the other demands: free subjects, regime change, humiliation, release peoples
        float bw3 = (w - 8) / 3;
        bool subjects = p.War.SideOf(winner).Any(c => G.World.Relations.OverlordOf(c) is { } o && p.War.SideOf(loser).Contains(o));
        if (Gui.Button(new Rectangle(x, y, bw3, 26 * S), "Free subjects", subjects, subjects ? "Subjects of the losing side that fight with the winners become independent" : "No subjects of the losing side fight with the winners", p.FreeSubjects, 13)) p.FreeSubjects = !p.FreeSubjects;
        bool regime = G.Politics.Enabled && G.Politics.Of(loser).Ruling != G.Politics.Of(winner).Ruling;
        if (Gui.Button(new Rectangle(x + bw3 + 4, y, bw3, 26 * S), "Regime change", regime, regime ? $"Put the {G.Politics.Ideology(G.Politics.Of(winner).Ruling)?.Name} party in power in {loser.Name}" : "Same ideology (or no politics)", p.RegimeChange, 13)) p.RegimeChange = !p.RegimeChange;
        if (Gui.Button(new Rectangle(x + 2 * (bw3 + 4), y, bw3, 26 * S), "Humiliate", true, $"{loser.Name} loses stability and political power to {winner.Name}", p.Humiliate, 13)) p.Humiliate = !p.Humiliate;
        y += 32 * S;
        if (G.Diplomacy.DemilEnabled && !p.Annex)
        {
            // demilitarize the state that is highlighted (click it in the list above or on the map first)
            var hl = Map.HighlightState;
            bool can = hl?.Owner != null && loserSide.Contains(hl.Owner) && !hl.Demilitarized && hl != hl.Owner.Capital?.State;
            bool on = hl != null && p.Demilitarize.Contains(hl);
            string text = hl != null ? (on ? $"Keep {hl.DisplayName} armed" : $"Demilitarize {hl.DisplayName}") : "Demilitarize the selected state";
            if (Gui.Button(new Rectangle(x, y, w, 24 * S), text, can || on, "No armed forces may enter or be raised there; it ends when its owner goes to war, or remilitarizes it (and risks the answer of its neighbours)", on, 13))
            { if (hl != null && !p.Demilitarize.Remove(hl)) p.Demilitarize.Add(hl!); }
            if (p.Demilitarize.Count > 0) Gui.Label($"{p.Demilitarize.Count} state(s) demilitarized", x + w - 150 * S, y + 4 * S, 12, Gui.TextDim);
            y += 30 * S;
        }
        if (G.Population.Enabled && !p.Annex)
        {
            var peoples = p.War.SideOf(loser).SelectMany(c => c.OwnedStates.Where(s => s != c.Capital?.State).Select(s => (c, cu: G.Population.MajorityCulture(s))))
                .Where(t => t.cu != null && !G.Population.IsAccepted(t.c, t.cu!)).Select(t => t.cu!).Distinct().Take(4).ToList();
            if (peoples.Count > 0)
            {
                float bx = x;
                Gui.Label("Release:", x, y + 4 * S, 13, Gui.TextDim);
                bx += 60 * S;
                foreach (var cu in peoples)
                {
                    string name = G.Population.Culture(cu)?.Name ?? cu;
                    float bw = Math.Min((x + w - bx) , Math.Max(70 * S, Gui.Measure(name, 12).X + 14 * S));
                    if (bw < 40 * S) break;
                    bool on = p.Release.Contains(cu);
                    if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), name, true, $"Free the {name} people of {loser.Name} as their own country", on, 12)) { if (!p.Release.Remove(cu)) p.Release.Add(cu); }
                    bx += bw + 4 * S;
                }
                y += 30 * S;
            }
        }
        Gui.Label($"Gold: {p.Gold:0}", x, y + 4, 15);
        if (Gui.Button(new Rectangle(x + 120 * S, y, 50 * S, 24 * S), "-50", p.Gold > 0, null, false, 13)) p.Gold = Math.Max(0, p.Gold - 50);
        if (Gui.Button(new Rectangle(x + 176 * S, y, 50 * S, 24 * S), "+50", true, null, false, 13)) p.Gold += 50;
        y += 34 * S;

        offer = BuildOffer(p);
        double total = G.Diplomacy.OfferCost(offer);
        string? invalid = G.Diplomacy.InvalidReason(offer);
        bool accept = invalid == null && (p.Enemy.IsPlayer || G.Diplomacy.AiWouldAccept(offer));
        Gui.Label($"Total cost: {total:0} war score (war goals allow {G.Diplomacy.MaxDemands(p.War, winner):0})", x, y, 16, total <= Math.Abs(score) || p.Concede ? Gui.Text : Gui.Bad);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S))) Gui.Tooltip("States marked * are war goals of the winning side (cheaper). War exhaustion and a long or stuck war make the AI accept less.");
        y += 22 * S;
        Gui.Label(invalid ?? (accept ? "They will ACCEPT this offer" : "They will REFUSE this offer"), x, y, 15, invalid == null && accept ? Gui.Good : Gui.Bad);
        y += 28 * S;
        if (Gui.Button(new Rectangle(x, y, w, 32 * S), "Send peace offer", invalid == null, string.Join("\n", offer.Terms), false, 17))
        {
            string result = G.Diplomacy.SendPeaceOffer(offer);
            Ui.SetStatus("Peace offer: " + result);
            if (result == "Accepted") { Ui.Peace = null; Ui.Right = RightPanel.Wars; Map.HighlightState = null; Map.Dirty = true; }
        }
    }
}
