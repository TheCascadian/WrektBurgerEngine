using Burger.Core.Model;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Multiplayer: actions go through the session (the sim only changes by commands), plus the player list, chat and country pick.</summary>
public sealed partial class Hud
{
    private LockstepSession? Mp => _app.Mp;
    private string _chatText = "";
    private bool _mpCollapsed;

    /// <summary>True when the action was sent as a command and the caller must not change the sim itself.</summary>
    private bool Send(string type, params string[] args)
    {
        if (Mp == null) return false;
        Mp.Submit(type, args);
        return true;
    }

    private void SetPace(bool? paused, int? speed)
    {
        if (Mp != null)
        {
            // only vote for what differs from the current state, so pressing 5 while running is one speed vote, not a speed vote plus a pause vote
            if (speed is int s && s != Mp.Speed) Mp.SetSpeed(s);
            if (paused is bool p && p != Mp.Paused) Mp.SetPaused(p);
            return;
        }
        if (speed is int sp) G.Clock.Speed = sp;
        if (paused is bool pa) G.Clock.Paused = pa;
    }

    private bool IsMine(Country? c) => Mp == null || (c != null && Mp.Me != null && c.Tag == Mp.Me.Tag);

    /// <summary>Orders one division (or an army's divisions) to move; true when it went out as commands.</summary>
    private bool MpMove(IEnumerable<Division> divisions, Location to, bool queue)
    {
        if (Mp == null) return false;
        foreach (var d in divisions) Mp.Submit(GameCommand.Move, d.Id.ToString(), to.Key, queue ? "q" : "");
        return true;
    }

    private void DrawMultiplayer()
    {
        var mp = Mp;
        if (mp == null) return;
        float s = S;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float w = 300 * s;
        var toggle = new Rectangle(sw - w - 6 * s, sh - 28 * s, w, 22 * s);
        if (Gui.Button(toggle, _mpCollapsed ? $"Multiplayer ({mp.Players.Count(p => p.Active)})  [show]" : "Multiplayer  [hide]", true, null, false, 13)) _mpCollapsed = !_mpCollapsed;
        if (_mpCollapsed) return;

        var chat = _app.MpChat;
        // chat is wrapped at a readable size; show the newest lines that fit in a fixed height
        const float ChatSize = 15;
        float chatW = w - 20 * s, chatMax = 150 * s, chatH = 0;
        int lines = 0;
        for (int i = chat.Count - 1; i >= 0; i--)
        {
            float lh = Gui.WrappedHeight(chat[i], chatW, ChatSize) + 3 * s;
            if (chatH + lh > chatMax) break;
            chatH += lh; lines++;
        }
        bool picking = mp.Me != null && mp.Me.Tag.Length == 0;
        float rows = mp.Players.Count * 24 * s;
        var mpc = _app.Mpc;
        bool invite = mp.IsHost && mpc.LanCode.Length > 0;
        float inviteH = invite ? (40 + (mpc.InternetCode.Length > 0 ? 28 : 0) + 28 + 6) * s : 0;
        float h = 30 * s + inviteH + rows + 8 * s + chatH + 34 * s + (picking ? 190 * s : 0);
        var r = new Rectangle(sw - w - 6 * s, toggle.Y - h - 4 * s, w, h);
        Gui.Panel(r, mp.IsHost ? "Players (host)" : "Players");
        float x = r.X + 10 * s, y = r.Y + 30 * s;
        if (invite)
        {
            Gui.Wrapped(mpc.NetworkStatus, x, y, w - 20 * s, 12, mpc.InternetCode.Length > 0 ? Gui.Good : Gui.TextDim);
            y += 40 * s;
            if (mpc.InternetCode.Length > 0) { InviteRow(x, ref y, w, "Internet", mpc.InternetCode); }
            InviteRow(x, ref y, w, "Local", mpc.LanCode);
            y += 6 * s;
        }
        foreach (var p in mp.Players)
        {
            var c = p.Tag.Length > 0 ? G.World.GetCountry(p.Tag) : null;
            if (c != null) Gui.Flag(c, new Rectangle(x, y, 30 * s, 20 * s));
            string name = p.Name + (p.IsHost ? " (host)" : "") + (p.Id == mp.Me?.Id ? " (you)" : "");
            Gui.Label(Gui.Fit(name, 130 * s, 13), x + 36 * s, y + 2 * s, 13, p.Active ? Gui.Text : Gui.TextDim);
            string right = p.IsAi ? "AI" : !p.Active ? "left" : p.IsHost ? $"t{p.Ack}" : $"t{p.Ack} {p.PingMs}ms";
            Gui.Label(right, r.X + r.Width - 10 * s - Gui.Measure(right, 12).X, y + 3 * s, 12, p.IsAi ? Gui.Gold : Gui.TextDim);
            if (mp.IsHost && !p.IsHost && p.Active && Gui.Hover(new Rectangle(x, y, w - 20 * s, 22 * s)) && Raylib.IsKeyDown(KeyboardKey.LeftControl) && Gui.Clicked(new Rectangle(x, y, w - 20 * s, 22 * s))) mp.Kick(p.Id);
            y += 24 * s;
        }
        y += 8 * s;
        if (picking)
        {
            Gui.Label("Pick your country:", x, y, 13, Gui.Gold);
            y += 20 * s;
            var taken = mp.Players.Where(p => p.Active && p.Tag.Length > 0).Select(p => p.Tag).ToHashSet();
            var free = G.World.Countries.Where(c => c.Playable && c.Exists && !taken.Contains(c.Tag)).OrderBy(c => c.Name).ToList();
            var area = new Rectangle(x, y, w - 20 * s, 160 * s);
            float top = Gui.BeginScroll("mp-pick", area, free.Count * 26 * s);
            float cy = top;
            foreach (var c in free)
            {
                var row = new Rectangle(x, cy, area.Width - 10 * s, 24 * s);
                Gui.ButtonFrame(row, true, Gui.Hover(row), false);
                Gui.Flag(c, new Rectangle(row.X + 3, row.Y + 3, 26 * s, 18 * s));
                Gui.Label(c.Name, row.X + 36 * s, row.Y + 4 * s, 13, Gui.Text);
                if (Raylib.CheckCollisionRecs(row, area) && Gui.Clicked(row)) { var err = mp.Pick(c.Tag); if (err != null) Ui.SetStatus(err); }
                cy += 26 * s;
            }
            Gui.EndScroll();
            y += 170 * s;
        }
        foreach (var line in chat.Skip(Math.Max(0, chat.Count - lines)))
        {
            y += Gui.Wrapped(line, x, y, chatW, ChatSize, line.StartsWith("* ") ? Gui.Gold : Gui.Text) + 3 * s;
        }
        bool was = Gui.TextFocused;
        Gui.TextField("mp-chat", new Rectangle(x, y + 4 * s, w - 20 * s, 24 * s), ref _chatText, 120, "Chat (Enter sends)");
        if (was && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) && _chatText.Trim().Length > 0) { mp.SendChat(_chatText.Trim()); _chatText = ""; }
        Gui.Block(r);
    }

    private void InviteRow(float x, ref float y, float w, string label, string code)
    {
        float s = S;
        Gui.Label(code, x, y + 5 * s, 12, Gui.Gold);
        if (Gui.Button(new Rectangle(x + w - 20 * s - 96 * s, y, 96 * s, 24 * s), "Copy " + label.ToLowerInvariant(), true, label + " join code: copies it, then send it to your friend. Anyone with this code can join.", false, 13))
        {
            Raylib.SetClipboardText(code);
            Ui.SetStatus(label + " join code copied");
        }
        y += 28 * s;
    }
}
