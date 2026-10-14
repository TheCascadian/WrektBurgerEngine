using Burger.Core.Figures;
using System.Text;

namespace Burger.Runtime;

public sealed class AppOptions
{
    public string GameRoot = "";
    public string? LogDir;
    public string? Tag;
    public bool Observe;
    public string? LoadSave;
    public int Width = 1600;
    public int Height = 900;
    public bool Hidden;
    /// <summary>Take a screenshot after this many frames and quit (for automated tests).</summary>
    public string? Screenshot;
    public int ScreenshotFrame = 120;
    public int TicksBeforeScreenshot;
    /// <summary>Start unpaused at this speed (1-5).</summary>
    public int Speed;
    /// <summary>Write frame and simulation timings to the log every few seconds.</summary>
    public bool Perf;
    /// <summary>Console commands to run after the game starts (testing / automation).</summary>
    public List<string> Exec = new();
    public string? MapMode;
    public float? Zoom;
    public bool NoFogShading;
    public bool NoUnits;
    /// <summary>Historical bias 0-1 for this run (default: the player's setting).</summary>
    public double? Bias;
    public string? Open;
    /// <summary>Render the contact sheet of a figure to this file and quit.</summary>
    public string? FigureSheet;
    /// <summary>Render every clip of a figure as numbered frames on a rotating platform into this folder and quit (tools/figure-gifs.sh turns them into .gif files).</summary>
    public string? FigureFrames, FigureGifs, FigureGear, FigurePeople, FigureVariants;
    public int FigureSize = 480;
    public bool FigureForce;
    public string[]? FigureClips;
    public string FigureKey = Burger.Core.Figures.HumanMale.Key;
}

public static class Program
{
    private const string Usage = @"Usage: Game.exe [--game <folder>] [--tag TAG | --observe] [--load <save>] [--size 1600x900]
  --game       game folder (default: 'game' next to the exe)
  --tag        skip the menu and play as TAG
  --observe    skip the menu and watch the AI
  --load       load a save file
  --log-dir <folder>     write error.log and --perf output to this folder
  --screenshot <file.png> [--frames N] [--days D]   render, save a screenshot and quit (testing)
  --figure-sheet <file.png> [--figure KEY]          render a figure and its animations to an image and quit
  --figure-frames <folder> [--figure KEY] [--figure-clip a,b]  render each (or the named) animation as frames on a turntable (see tools/figure-gifs.sh)
  --figure-gear <file.png>                          contact sheet of every piece of kit of every soldier variation
  --figure-gear <file.png>                          contact sheet of every kit piece for every soldier variation
  --figure-variants <dir>                           one contact sheet per country: its historical peoples, men and women, bare and in uniform
  --figure-people <file.png>                        contact sheet of every people's look, bare and as a soldier
  --figure-gif <folder> --figure KEY[,KEY...|all] [--figure-clip a,b] [--figure-size 480] [--figure-force]  .gif per animation, straight to ffmpeg; unchanged ones are skipped";

    public static int Main(string[] args)
    {
        var opt = new AppOptions();
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
                switch (a.ToLowerInvariant())
                {
                    case "--game": opt.GameRoot = Next(); break;
                    case "--log-dir": opt.LogDir = Path.GetFullPath(Next()); break;
                    case "--tag": opt.Tag = Next().ToUpperInvariant(); break;
                    case "--observe": opt.Observe = true; break;
                    case "--load": opt.LoadSave = Next(); break;
                    case "--screenshot": opt.Screenshot = Path.GetFullPath(Next()); break;
                    case "--frames": opt.ScreenshotFrame = int.Parse(Next()); break;
                    case "--days": opt.TicksBeforeScreenshot = int.Parse(Next()) * 24; break;
                    case "--hidden": opt.Hidden = true; break;
                    case "--figure-sheet": opt.FigureSheet = Path.GetFullPath(Next()); break;
                    case "--figure-frames": opt.FigureFrames = Path.GetFullPath(Next()); break;
                    case "--figure-gif": opt.FigureGifs = Path.GetFullPath(Next()); break;
                    case "--figure-variants": opt.FigureVariants = Path.GetFullPath(Next()); break;
                    case "--figure-people": opt.FigurePeople = Path.GetFullPath(Next()); break;
                    case "--figure-gear": opt.FigureGear = Path.GetFullPath(Next()); break;
                    case "--figure-size": opt.FigureSize = int.Parse(Next()); break;
                    case "--figure-force": opt.FigureForce = true; break;
                    case "--figure-clip": opt.FigureClips = Next().Split(',', StringSplitOptions.RemoveEmptyEntries); break;
                    case "--figure": opt.FigureKey = Next(); break;
                    case "--exec": opt.Exec.Add(Next()); break;
                    case "--mapmode": opt.MapMode = Next(); break;
                    case "--no-fog-shading": opt.NoFogShading = true; break;
                    case "--no-units": opt.NoUnits = true; break;
                    case "--bias": opt.Bias = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--zoom": opt.Zoom = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--open": opt.Open = Next(); break;
                    case "--speed": opt.Speed = int.Parse(Next()); break;
                    case "--perf": opt.Perf = true; break;
                    case "--size":
                        var parts = Next().ToLowerInvariant().Split('x');
                        opt.Width = int.Parse(parts[0]);
                        opt.Height = int.Parse(parts[1]);
                        break;
                    case "--help": case "-h": case "/?":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        // a bare folder argument also works:  Game.exe path\to\game
                        if (Directory.Exists(a)) opt.GameRoot = a;
                        else throw new ArgumentException($"Unknown argument '{a}'");
                        break;
                }
            }
            opt.GameRoot = FindGameRoot(opt.GameRoot);
            if (opt.FigureSheet != null || opt.FigureFrames != null || opt.FigureGifs != null || opt.FigureGear != null || opt.FigurePeople != null || opt.FigureVariants != null)
            {
                // the figures of the game folder (its gfx/models/ over the built-in body), without loading the game itself
                var figures = new Burger.Core.Game().Figures;
                figures.Load(opt.GameRoot);
                if (opt.FigureVariants != null)
                {
                    Directory.CreateDirectory(opt.FigureVariants);
                    foreach (string t in Burger.Core.Figures.Uniforms.Tags)
                    {
                        string tg = t.ToLowerInvariant();
                        var peoples = Ancestries.Rosters.TryGetValue(t, out var roster) ? roster : new[] { "default" };
                        string Spec(string p, bool f) => p + (f ? ".f." : ".") + Ancestries.StylesOf(Ancestries.Find(p) ?? Ancestry.Default, f)[0].ToString().ToLowerInvariant();
                        // per sex (the country's first people), per people (men), per people and sex
                        FigureRenderer.RenderSoldierSheet(figures, Path.Combine(opt.FigureVariants, $"sex_{tg}.png"), t, new[] { ("male", Spec(peoples[0], false)), ("female", Spec(peoples[0], true)) });
                        FigureRenderer.RenderSoldierSheet(figures, Path.Combine(opt.FigureVariants, $"ancestry_{tg}.png"), t, peoples.Select(p => (p, Spec(p, false))).ToList(), 4);
                        FigureRenderer.RenderSoldierSheet(figures, Path.Combine(opt.FigureVariants, $"ancestry_sex_{tg}.png"), t,
                            peoples.SelectMany(p => new[] { (p + " male", Spec(p, false)), (p + " female", Spec(p, true)) }).ToList(), 4);
                    }
                    return 0;
                }
                if (opt.FigurePeople != null) { FigureRenderer.RenderPeopleSheet(figures, opt.FigurePeople); return 0; }
                if (opt.FigureGear != null) { FigureRenderer.RenderGearSheet(figures, opt.FigureGear); return 0; }
                if (opt.FigureGifs != null)
                {
                    // --figure takes several keys, or "all" for every model that has animations
                    var keys = opt.FigureKey.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var list = keys.Contains("all", StringComparer.OrdinalIgnoreCase)
                        ? figures.All.Where(f => f.Clips.Count > 0).OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).ToList()
                        : keys.Select(k => figures.Find(k) ?? throw new ArgumentException($"No figure '{k}'")).ToList();
                    FigureRenderer.RenderGifs(list, opt.FigureGifs, opt.FigureSize, opt.FigureClips, opt.FigureForce);
                    return 0;
                }
                var model = figures.Find(opt.FigureKey) ?? throw new ArgumentException($"No figure '{opt.FigureKey}'");
                if (opt.FigureFrames != null) FigureRenderer.RenderTurntable(model, opt.FigureFrames, clips: opt.FigureClips);
                else FigureRenderer.RenderSheet(model, opt.FigureSheet!);
                return 0;
            }
            new App(opt).Run();
            return 0;
        }
        catch (Exception ex)
        {
            WriteCrash(opt, ex);
            return 1;
        }
    }

    /// <summary>Locates the game folder: argument, then 'game' next to the exe, then 'game' in the working directory.</summary>
    public static string FindGameRoot(string arg)
    {
        if (!string.IsNullOrEmpty(arg)) return Path.GetFullPath(arg);
        var candidates = new List<string>();
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null) candidates.Add(Path.Combine(exeDir, "game"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "game"));
        candidates.Add(Directory.GetCurrentDirectory());
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "game.txt")) || Directory.Exists(Path.Combine(c, "common"))) return Path.GetFullPath(c);
        return Path.GetFullPath(candidates[0]);
    }

    private static void WriteCrash(AppOptions opt, Exception ex)
    {
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "logs");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"BurgerEngine crash - {DateTime.Now}");
            sb.AppendLine($"Game folder: {opt.GameRoot}");
            sb.AppendLine(ex.ToString());
            File.WriteAllText(Path.Combine(dir, "crash.log"), sb.ToString());
        }
        catch { /* nothing else we can do */ }
    }
}
