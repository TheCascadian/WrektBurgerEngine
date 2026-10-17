using Burger.Core;
using Burger.Core.History;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The start date of the start screen: tabs for the eras, their suggested dates and a slider; the map changes to the history of the date picked.</summary>
public sealed partial class App
{
    private void PickDate(GameDate date)
    {
        _menuDate = date;
        _menuDateDirty = true;
    }

    /// <summary>
    /// Draws the date, a tab for each era with a line about it and its suggested dates, the other dates of the era and a slider with a tick for every bookmark, and what has
    /// happened by then. The game reloads with the history up to the date once the slider is let go or a date is clicked.
    /// </summary>
    private void DrawStartDate(Game game, float x, ref float y, float w)
    {
        float s = Gui.Scale;
        var tl = game.Timeline;
        if (!tl.Enabled)
        {
            Gui.Label($"Start date: {game.World.Date.ToDisplay()}", x, y, 15, Gui.TextDim);
            y += 26 * s;
            return;
        }
        var shown = _menuDate ?? tl.Earliest;
        Gui.Label($"Start date: {shown.ToDisplay()}", x, y, 16, Gui.Gold, FontKind.Bold);
        y += 26 * s;

        // eras
        var era = tl.EraAt(shown);
        if (tl.Eras.Count > 0)
        {
            float ex = x;
            foreach (var e in tl.Eras)
            {
                string label = Gui.Fit(e.Name, 190 * s, 13, FontKind.Bold);
                float bw = Gui.Measure(label, 13, FontKind.Bold).X + 16 * s;
                if (ex + bw > x + w && ex > x) { ex = x; y += 28 * s; }
                if (Gui.Button(new Rectangle(ex, y, bw, 25 * s), label, true, $"{e.From.YearDisplay} to {e.To.YearDisplay}\n{e.Desc}", e == era, 13) && e != era)
                    PickDate(e.Suggested.Count > 0 ? e.Suggested[0] : e.From);
                ex += bw + 3 * s;
            }
            y += 31 * s;
            if (era != null && era.Desc.Length > 0) y += Gui.Wrapped(era.Desc, x, y, w, 12, Gui.TextDim) + 6 * s;
        }

        // the suggested dates of the era, then the others
        if (era != null)
        {
            var suggested = era.Suggested.Select(d => tl.Bookmarks.FirstOrDefault(b => b.Date == d)).Where(b => b != null).Cast<Bookmark>().ToList();
            var others = tl.Bookmarks.Where(b => era.Contains(b.Date) && !suggested.Contains(b)).ToList();
            if (suggested.Count > 0)
            {
                Gui.Label("Suggested:", x, y, 12, Gui.Gold, FontKind.Bold);
                y += 18 * s;
                y = DrawDateButtons(tl, suggested, x, y, w, shown, true);
            }
            if (others.Count > 0)
            {
                Gui.Label("Also:", x, y, 12, Gui.TextDim, FontKind.Bold);
                y += 18 * s;
                y = DrawDateButtons(tl, others, x, y, w, shown, false);
            }
        }

        // the slider, with a tick for every bookmark and a brighter one for the suggested dates
        double min = tl.Earliest.Year, max = tl.Latest.Year, value = Math.Clamp(shown.Year, min, max);
        var bar = new Rectangle(x, y, w, 20 * s);
        foreach (var b in tl.Bookmarks)
        {
            float tx = bar.X + (float)((b.Date.Year - min) / Math.Max(1, max - min)) * bar.Width;
            bool sug = tl.Eras.Any(e => e.Suggested.Contains(b.Date));
            Raylib.DrawLineEx(new System.Numerics.Vector2(tx, bar.Y + 2 * s), new System.Numerics.Vector2(tx, bar.Y + bar.Height - 2 * s), sug ? 2f : 1f, new Color(232, 196, 90, sug ? 200 : 90));
        }
        if (Gui.Slider("start_date", bar, ref value, min, max))
        {
            int year = (int)Math.Round(value);
            if (year != shown.Year) { _menuDate = year <= tl.Earliest.Year ? tl.Earliest : new GameDate(year, 1, 1); _menuDateDirty = true; shown = _menuDate.Value; }
        }
        y += 24 * s;
        Gui.Label(tl.Earliest.YearDisplay, x, y, 12, Gui.TextDim);
        string end = tl.Latest.YearDisplay;
        Gui.Label(end, x + w - Gui.Measure(end, 12).X, y, 12, Gui.TextDim);
        y += 20 * s;

        // what has happened by the date
        var snap = tl.SnapshotFor(shown);
        var mark = tl.Bookmarks.FirstOrDefault(b => b.Date == shown) ?? tl.BookmarkAt(shown);
        if (mark != null) y += Gui.Wrapped($"{mark.Name}: {mark.Desc}", x, y, w, 13, Gui.Text) + 4 * s;
        if (snap != null && snap.Date != shown) y += Gui.Wrapped($"The map is that of {snap.Date.YearDisplay}.", x, y, w, 12, Gui.TextDim) + 6 * s;
        // let go of the slider: load the world of that date (the map changes)
        if (_menuDateDirty && !Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            _menuDateDirty = false;
            if (_menuDate != null && _menuDate.Value != game.World.Date) _pending = () => LoadGame(keepMap: true);
        }
    }

    private float DrawDateButtons(Burger.Core.History.TimelineSystem tl, List<Bookmark> list, float x, float y, float w, GameDate shown, bool suggested)
    {
        float s = Gui.Scale, bx = x;
        foreach (var b in list)
        {
            string label = Gui.Fit($"{b.Name} ({b.Date.YearDisplay})", 230 * s, 12);
            float bw = Gui.Measure(label, 12).X + 14 * s;
            if (bx + bw > x + w && bx > x) { bx = x; y += 25 * s; }
            if (Gui.Button(new Rectangle(bx, y, bw, 22 * s), label, true, $"{b.Date.ToDisplay()}\n{b.Desc}", b.Date == shown, 12)) PickDate(b.Date);
            bx += bw + 3 * s;
        }
        return y + 28 * s;
    }
}
