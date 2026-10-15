using System.Globalization;
using Burger.Core.Diplomacy;
using Burger.Core.Events;
using Burger.Core.Model;
using Burger.Core.Save;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Multiplayer;

/// <summary>
/// What a human still has to answer (events and proposals). The normal save leaves these out, which is fine for one player but
/// makes a reloaded client drift from the host: the AI and the event timeout both look at them. Written into the multiplayer save section.
/// Named scopes of an event (<c>saved_scope</c> values) are not kept: a restored event only has its root.
/// </summary>
internal static class PendingState
{
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static void Write(Game g, Node n)
    {
        foreach (var pe in g.Events.PlayerQueue)
        {
            if (pe.Context.Root == null) continue;
            var e = n.AddBlock("event");
            e.Add("id", pe.Def.Id);
            e.Add("scope", SaveGame.ScopeRef(pe.Context.Root), true);
            e.Add("fired", pe.FiredOn.ToScript());
            e.Add("hour", I(pe.FiredOn.Hour));
        }
        foreach (var p in g.PendingProposals)
        {
            var b = n.AddBlock("proposal");
            b.Add("kind", p.Kind.ToString());
            b.Add("from", p.From.Tag); b.Add("to", p.To.Tag);
            b.Add("date", p.Date.ToScript()); b.Add("hour", I(p.Date.Hour));
            b.Add("title", Net.Esc(p.Title), true); b.Add("text", Net.Esc(p.Text), true);
            if (p.Action != null) b.Add("action", p.Action.Key);
            if (p.TargetCharacter != null) b.Add("character", p.TargetCharacter.Key);
            if (p.War != null) b.Add("war", Net.Esc(p.War.Name), true);
            if (p.Peace is { } o)
            {
                b.Add("winner", o.FromIsWinner ? "yes" : "no");
                if (o.NoEnd) b.Add("no_end", "yes");
                foreach (var t in o.Terms)
                {
                    var tb = b.AddBlock("term");
                    tb.Add("type", t.Type.ToString());
                    if (t.State != null) tb.Add("state", t.State.Key);
                    if (t.Beneficiary != null) tb.Add("to", t.Beneficiary.Tag);
                    tb.Add("amount", F(t.Amount));
                    tb.Add("subject_type", Net.Esc(t.SubjectType), true); tb.Add("culture", Net.Esc(t.Culture), true); tb.Add("ideology", Net.Esc(t.Ideology), true);
                    if (t.AsSubject) tb.Add("as_subject", "yes");
                }
            }
        }
    }

    public static void Read(Game g, Node n)
    {
        var w = g.World;
        g.Events.PlayerQueue.Clear();
        g.PendingProposals.Clear();
        foreach (var e in n.GetAll("event"))
        {
            var def = g.Events.Get(e.GetString("id") ?? "");
            g.Script.TryResolveScope(e.GetString("scope") ?? "", g.Context(null), out var scope);
            if (def == null || scope == null) continue;
            var ctx = g.Context(scope);
            g.Events.PlayerQueue.Add(new PendingEvent
            {
                Def = def, Context = ctx, FiredOn = GameDate.Parse(e.GetString("fired") ?? "", w.Date).AddHours(e.GetInt("hour")),
                Title = g.Events.FormatText(def.Title, ctx), Desc = g.Events.FormatText(def.Desc, ctx),
            });
        }
        foreach (var b in n.GetAll("proposal"))
        {
            var from = w.GetCountry(b.GetString("from")); var to = w.GetCountry(b.GetString("to"));
            if (from == null || to == null || !Enum.TryParse<ProposalKind>(b.GetString("kind"), out var kind)) continue;
            var p = new Proposal
            {
                Kind = kind, From = from, To = to,
                Date = GameDate.Parse(b.GetString("date") ?? "", w.Date).AddHours(b.GetInt("hour")),
                Title = Net.Unesc(b.GetString("title") ?? ""), Text = Net.Unesc(b.GetString("text") ?? ""),
            };
            string? action = b.GetString("action");
            if (action != null) p.Action = g.Data.DiplomaticActions.GetValueOrDefault(action) ?? g.Data.CharacterInteractions.GetValueOrDefault(action);
            if (b.GetString("character") is { } ck) p.TargetCharacter = w.GetCharacter(ck);
            if (b.GetString("war") is { } wn) p.War = w.Wars.FirstOrDefault(x => x.Name == Net.Unesc(wn));
            if (kind == ProposalKind.Peace && p.War != null)
            {
                var o = new PeaceOffer { War = p.War, From = from, To = to, FromIsWinner = b.GetString("winner") != "no", NoEnd = b.GetString("no_end") == "yes" };
                foreach (var tb in b.GetAll("term"))
                {
                    if (!Enum.TryParse<PeaceTermType>(tb.GetString("type"), out var tt)) continue;
                    o.Terms.Add(new PeaceTerm
                    {
                        Type = tt, State = w.GetState(tb.GetString("state")), Beneficiary = w.GetCountry(tb.GetString("to")),
                        Amount = tb.GetDouble("amount"), SubjectType = Net.Unesc(tb.GetString("subject_type") ?? ""),
                        Culture = Net.Unesc(tb.GetString("culture") ?? ""), Ideology = Net.Unesc(tb.GetString("ideology") ?? ""), AsSubject = tb.GetString("as_subject") == "yes",
                    });
                }
                p.Peace = o;
            }
            if (kind == ProposalKind.Peace && p.Peace == null) continue;
            g.PendingProposals.Add(p);
        }
    }
}
