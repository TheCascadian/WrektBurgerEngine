using Raylib_cs;

namespace Burger.Runtime;

/// <summary>A modal "are you sure" box shown before political power is spent.</summary>
public sealed partial class Hud
{
    private string? _confirmText;
    private Action? _confirmYes;

    /// <summary>Asks first; <paramref name="yes"/> runs only when the player agrees.</summary>
    private void Confirm(string text, Action yes)
    {
        _confirmText = text;
        _confirmYes = yes;
    }

    private void DrawConfirmDialog()
    {
        if (_confirmText == null) return;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var all = new Rectangle(0, 0, sw, sh);
        Raylib.DrawRectangleRec(all, new Color(0, 0, 0, 140));
        Gui.Block(all);
        float w = 420 * S;
        float th = Gui.WrappedHeight(_confirmText, w - 32 * S, 15);
        var r = new Rectangle((sw - w) / 2, (sh - (th + 120 * S)) / 2, w, th + 120 * S);
        Gui.Panel(r, "Are you sure?");
        Gui.Block(r);
        Gui.Wrapped(_confirmText, r.X + 16 * S, r.Y + Gui.HeaderHeight + 14 * S, w - 32 * S, 15);
        float by = r.Y + r.Height - 44 * S, bw = 120 * S;
        if (Gui.Button(new Rectangle(r.X + w / 2 - bw - 8 * S, by, bw, 32 * S), "Yes", true, null, false, 16) || Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            var yes = _confirmYes;
            _confirmText = null; _confirmYes = null;
            yes?.Invoke();
        }
        else if (Gui.Button(new Rectangle(r.X + w / 2 + 8 * S, by, bw, 32 * S), "No", true, null, false, 16) || Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _confirmText = null; _confirmYes = null;
        }
    }
}
