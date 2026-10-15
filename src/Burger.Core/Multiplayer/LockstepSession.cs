using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Burger.Core.Modding;
using Burger.Core.Save;

namespace Burger.Core.Multiplayer;

public sealed class PlayerInfo
{
    public int Id;
    public string Name = "";
    /// <summary>The country this player holds ("" = observer). Kept after leaving: the country is then run by the AI.</summary>
    public string Tag = "";
    public bool Active = true;
    public bool IsHost;
    /// <summary>Ticks this player's machine has executed ("ready tick" in the player list).</summary>
    public long Ack;
    public int PingMs;
    internal ITransport? Link;
    internal bool Ready;
    internal bool? VotePause;
    internal int? VoteSpeed;
    internal double PingSentAt;

    /// <summary>A player who left: the country they held is now played by the AI.</summary>
    public bool IsAi => !Active && Tag.Length > 0;
}

/// <summary>
/// Lockstep multiplayer for one host and up to <see cref="MaxPlayers"/> humans (the host included). Every machine runs the same sim;
/// only commands travel. The host owns the clock: it stamps each command with tick <c>hostTick + InputDelay</c>, broadcasts it, and
/// tells clients how many ticks it has run (<c>HORIZON</c>); a client never runs past that, so it always has every command it needs.
/// Call <see cref="Update"/> once per frame instead of <see cref="GameClock.Update"/>.
/// </summary>
public sealed class LockstepSession
{
    public const int MaxPlayers = 8;
    public const int InputDelay = 2;
    /// <summary>The host waits when the slowest client is this many ticks behind.</summary>
    public const int MaxLead = 48;
    public const int Protocol = 1;

    public bool IsHost { get; }
    public Game Game { get; private set; }
    public string Project { get; private set; } = "";
    public int Seed { get; private set; }
    /// <summary>Ticks the simulation has executed on this machine.</summary>
    public long Tick { get; private set; }
    public bool Paused { get; private set; } = true;
    public int Speed { get; private set; } = 2;
    public List<PlayerInfo> Players { get; private set; } = new();
    public PlayerInfo? Me { get; private set; }
    public bool Disconnected { get; private set; }
    /// <summary>Players of the save this game was loaded from (name, country): they must pick their country again.</summary>
    public List<(string Name, string Tag)> SavedPlayers { get; } = new();
    /// <summary>Desyncs found (host) or resyncs received (client).</summary>
    public int Resyncs { get; private set; }

    public event Action<string>? Toast;
    public event Action<string, string>? Chat;
    /// <summary>The game object was replaced by a resync: the renderer and UI must re-bind to <see cref="Game"/>.</summary>
    public event Action<Game>? GameReplaced;

    private readonly Func<string, int, Game>? _loader;
    private readonly SortedDictionary<long, List<GameCommand>> _queue = new();
    private readonly string _scriptsHash;
    private double _accumulator;
    private long _horizon, _horizonSent = -1;
    private int _nextId = 1;
    private double _lastPlayersSent;
    private readonly List<(ITransport Link, double Since)> _pending = new();
    private readonly Dictionary<long, ulong> _sums = new();
    private readonly HashSet<PlayerInfo> _resync = new();
    private ITransport? _hostLink;
    private bool _loadedForJoin;

    private static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    // ---------------------------------------------------------------------------------------- creation

    public static LockstepSession Host(Game game, string project, int seed, string hostName)
    {
        var s = new LockstepSession(game, true, null) { Project = project, Seed = seed };
        s.Me = new PlayerInfo { Id = 0, Name = hostName, IsHost = true, Ready = true };
        s._nextId = 1;
        s.Players.Add(s.Me);
        return s;
    }

    /// <summary>Joins a host. <paramref name="loader"/> loads and starts the named project with the host's seed (it throws when the project is missing).</summary>
    public static LockstepSession Join(ITransport link, string playerName, Func<string, int, Game> loader)
    {
        var s = new LockstepSession(null!, false, loader) { _hostLink = link };
        link.Send($"HELLO|{Protocol}|{Net.Esc(playerName)}");
        return s;
    }

    private LockstepSession(Game game, bool host, Func<string, int, Game>? loader)
    {
        IsHost = host;
        Game = game;
        _loader = loader;
        if (game != null) { Attach(game); _scriptsHash = Net.ScriptsHash(game.Data.Root); }
        else _scriptsHash = "";
    }

    private void Attach(Game g)
    {
        g.SaveSections.Add(new SaveSection
        {
            Name = "multiplayer",
            Write = n =>
            {
                foreach (var p in Players.Where(p => p.Tag.Length > 0)) n.Add("p", Net.Esc(p.Name) + " " + p.Tag, true);
                PendingState.Write(g, n);
            },
            Read = n =>
            {
                PendingState.Read(g, n);
                SavedPlayers.Clear();
                foreach (var e in n.GetAll("p"))
                {
                    var parts = (e.Value ?? "").Split(' ', 2);
                    if (parts.Length == 2) SavedPlayers.Add((Net.Unesc(parts[0]), parts[1]));
                }
            },
        });
    }

    /// <summary>Accepts a joining player's connection (host).</summary>
    public void AddPeer(ITransport link) => _pending.Add((link, Now()));

    // ---------------------------------------------------------------------------------------- player actions

    /// <summary>Takes a country. Returns an error text, or null when the request went through.</summary>
    public string? Pick(string tag)
    {
        if (Me == null) return "Not joined yet";
        if (IsHost) return PickFor(Me, tag);
        _hostLink!.Send("PICK|" + Net.Esc(tag));
        return null;
    }

    /// <summary>Sends a player command (the host applies it to the sim two ticks from now, same as everyone).</summary>
    public void Submit(string type, params string[] args)
    {
        if (Me == null || Me.Tag.Length == 0) return;
        if (IsHost) { if (GameCommand.IsPlayerCommand(type)) Stamp(Me.Tag, type, args); }
        else _hostLink!.Send($"CMD|0||{Net.Esc(type)}|{string.Join(',', args.Select(Net.Esc))}");
    }

    public void SendChat(string text)
    {
        if (Me == null || text.Length == 0) return;
        if (IsHost) { Broadcast($"CHAT|{Net.Esc(Me.Name)}|{Net.Esc(text)}"); Chat?.Invoke(Me.Name, text); }
        else _hostLink!.Send("CHAT|" + Net.Esc(text));
    }

    /// <summary>Host: pause or resume now. Client: vote (needs a majority of the humans).</summary>
    public void SetPaused(bool paused)
    {
        if (IsHost) { ApplyPace(paused, Speed, Me!.Name + (paused ? " paused the game" : " resumed the game")); }
        else _hostLink!.Send("VOTE|pause|" + (paused ? 1 : 0));
    }

    /// <summary>Host: set the speed now. Client: vote.</summary>
    public void SetSpeed(int speed)
    {
        speed = Math.Clamp(speed, 1, 5);
        if (IsHost) ApplyPace(Paused, speed, Me!.Name + " set speed " + speed);
        else _hostLink!.Send("VOTE|speed|" + speed);
    }

    /// <summary>The host's own vote counts for the current pause and speed, so a majority of all humans must want a change.</summary>
    private void Vote(PlayerInfo p, string kind, int value)
    {
        if (kind == "pause") p.VotePause = value != 0; else if (kind == "speed") p.VoteSpeed = Math.Clamp(value, 1, 5); else return;
        Tally(kind, p);
    }

    private void Tally(string kind, PlayerInfo? voter = null)
    {
        var humans = Players.Where(p => p.Active && p.Ready).ToList();
        int total = humans.Count;
        if (kind == "pause")
        {
            bool want = !Paused;
            int yes = humans.Count(p => !p.IsHost && p.VotePause == want);
            if (voter != null && yes * 2 <= total) ToastAll($"{voter.Name} votes to {(want ? "pause" : "resume")} ({yes}/{total})");
            if (yes * 2 > total) { foreach (var p in Players) p.VotePause = null; ApplyPace(want, Speed, $"Majority voted to {(want ? "pause" : "resume")}"); }
        }
        else
        {
            foreach (int s in humans.Where(p => !p.IsHost && p.VoteSpeed.HasValue && p.VoteSpeed != Speed).Select(p => p.VoteSpeed!.Value).Distinct().OrderBy(x => x))
            {
                int yes = humans.Count(p => !p.IsHost && p.VoteSpeed == s);
                if (yes * 2 > total) { foreach (var p in Players) p.VoteSpeed = null; ApplyPace(Paused, s, $"Majority voted for speed {s}"); return; }
            }
            if (voter?.VoteSpeed is int v && v != Speed) ToastAll($"{voter.Name} votes for speed {v}");
        }
    }

    private void ApplyPace(bool paused, int speed, string toast)
    {
        bool changed = paused != Paused || speed != Speed;
        Paused = paused; Speed = speed;
        Broadcast($"PACE|{(paused ? 1 : 0)}|{speed}");
        if (changed) ToastAll(toast);
    }

    private void ToastAll(string text) { Broadcast("TOAST|" + Net.Esc(text)); Toast?.Invoke(text); }

    // ---------------------------------------------------------------------------------------- the frame

    /// <summary>Call once per frame. Pumps messages and runs as many ticks as the clock and the protocol allow. Returns the ticks run.</summary>
    public int Update(double dt)
    {
        if (Disconnected) return 0;
        if (IsHost) { HostPump(); return HostRun(dt); }
        ClientPump();
        return ClientRun();
    }

    private int HostRun(double dt)
    {
        int ran = 0;
        if (!Paused && Game.Started)
        {
            double dps = Game.Clock.DaysPerSecond[Math.Clamp(Speed, 1, 5)];
            bool unlimited = dps <= 0;
            if (!unlimited) _accumulator += dt * dps * 24;
            long t0 = Stopwatch.GetTimestamp();
            while ((unlimited || _accumulator >= 1) && !Paused && ElapsedMs(t0) < (Speed >= 5 ? 30 : 14))
            {
                long minAck = Players.Where(p => p.Active && p.Ready && !p.IsHost).Select(p => p.Ack).DefaultIfEmpty(Tick).Min();
                if (Tick - minAck >= MaxLead) { if (!unlimited) _accumulator = Math.Min(_accumulator, 2); break; }
                if (!unlimited) _accumulator -= 1;
                RunOneTick();
                ran++;
            }
            if (_accumulator > 48) _accumulator = 48;
        }
        Me!.Ack = Tick;
        // a resync is sent at a tick boundary, with the commands that are still waiting
        foreach (var p in _resync) if (p.Active && p.Link != null) SendSync(p);
        _resync.Clear();
        if (Tick != _horizonSent) { _horizonSent = Tick; Broadcast($"HORIZON|{Tick}"); }
        double now = Now();
        if (now - _lastPlayersSent > 1000)
        {
            _lastPlayersSent = now;
            foreach (var p in Players.Where(p => p.Active && !p.IsHost && p.Link != null)) { p.PingSentAt = now; p.Link!.Send("PING|" + now.ToString("R", CultureInfo.InvariantCulture)); }
            BroadcastPlayers();
        }
        return ran;
    }

    private static double ElapsedMs(long t0) => (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

    private int ClientRun()
    {
        int ran = 0;
        long t0 = Stopwatch.GetTimestamp();
        while (Game != null && Tick < _horizon && ElapsedMs(t0) < 30) { RunOneTick(); ran++; }
        if (ran > 0 && Me != null) _hostLink!.Send($"ACK|{Tick}");
        return ran;
    }

    private void RunOneTick()
    {
        if (_queue.Remove(Tick, out var due))
            foreach (var c in due) ApplyCommand(c);
        Game.Tick();
        Tick++;
        var d = Game.World.Date;
        if (d.Day == 1 && d.Hour == 0) // the 1st of a month: compare state
        {
            ulong sum = StateChecksum.Compute(Game);
            if (IsHost)
            {
                _sums[Tick] = sum;
                foreach (long k in _sums.Keys.Where(k => k < Tick - 24 * 365).ToList()) _sums.Remove(k);
            }
            else _hostLink!.Send($"SUM|{Tick}|{sum:x16}");
        }
    }

    private void ApplyCommand(GameCommand c)
    {
        if (!CommandExecutor.Apply(Game, c)) return;
        if (c.Type == GameCommand.Claim && Me != null && c.Args.Length > 0 && c.Args[0] == Me.Id.ToString(CultureInfo.InvariantCulture))
            Game.World.Player = Game.World.GetCountry(c.Country);
    }

    // ---------------------------------------------------------------------------------------- host side

    private void Enqueue(GameCommand c)
    {
        if (!_queue.TryGetValue(c.Tick, out var list)) _queue[c.Tick] = list = new();
        list.Add(c);
    }

    private void Stamp(string country, string type, string[] args)
    {
        var c = new GameCommand(Tick + InputDelay, country, type, args);
        Enqueue(c);
        Broadcast("CMD|" + c.Encode());
    }

    private void Broadcast(string line)
    {
        foreach (var p in Players) if (p.Active && !p.IsHost && p.Ready && p.Link != null) p.Link.Send(line);
    }

    private void HostPump()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (link, since) = _pending[i];
            if (link.TryReceive(out var line))
            {
                _pending.RemoveAt(i);
                Hello(link, line.Split('|'));
            }
            else if (!link.Connected || Now() - since > 10000) { _pending.RemoveAt(i); link.Close(); }
        }
        foreach (var p in Players.Where(p => p.Active && !p.IsHost).ToList())
        {
            while (p.Link!.TryReceive(out var line)) OnHostMessage(p, line.Split('|'));
            if (!p.Link.Connected && p.Active) Remove(p, "disconnected");
        }
    }

    private void Hello(ITransport link, string[] f)
    {
        string? reject = null;
        if (f[0] != "HELLO" || f.Length < 3 || f[1] != Protocol.ToString(CultureInfo.InvariantCulture)) reject = "Protocol mismatch";
        else if (Players.Count(p => p.Active) >= MaxPlayers) reject = "Game is full";
        if (reject != null) { link.Send("REJECT|" + Net.Esc(reject)); link.Close(); return; }
        var p = new PlayerInfo { Id = _nextId++, Name = UniqueName(Net.Unesc(f[2])), Link = link };
        Players.Add(p);
        link.Send($"WELCOME|{p.Id}|{Net.Esc(Project)}|{Seed}|{_scriptsHash}");
    }

    private string UniqueName(string name)
    {
        if (name.Length == 0) name = "Player";
        string n = name; int k = 2;
        while (Players.Any(p => p.Active && p.Name == n)) n = name + " " + k++;
        return n;
    }

    private void OnHostMessage(PlayerInfo p, string[] f)
    {
        switch (f[0])
        {
            case "READY": p.Ready = true; SendSync(p); ToastAll($"{p.Name} joined"); BroadcastPlayers(); break;
            case "PICK": if (p.Ready && f.Length > 1) { var err = PickFor(p, Net.Unesc(f[1])); if (err != null) p.Link!.Send("TOAST|" + Net.Esc(err)); } break;
            case "CMD":
                if (p.Ready && p.Tag.Length > 0 && GameCommand.Decode(f, 1) is { } c && GameCommand.IsPlayerCommand(c.Type))
                    Stamp(p.Tag, c.Type, c.Args);
                break;
            case "VOTE": if (p.Ready && f.Length > 2 && int.TryParse(f[2], out int v)) Vote(p, f[1], v); break;
            case "CHAT": if (p.Ready && f.Length > 1) { string t = Net.Unesc(f[1]); Broadcast($"CHAT|{Net.Esc(p.Name)}|{Net.Esc(t)}"); Chat?.Invoke(p.Name, t); } break;
            case "ACK": if (f.Length > 1 && long.TryParse(f[1], out long a)) p.Ack = Math.Max(p.Ack, a); break;
            case "PONG": p.PingMs = (int)(Now() - p.PingSentAt); break;
            case "SUM":
                if (f.Length > 2 && long.TryParse(f[1], out long tick) && _sums.TryGetValue(tick, out var mine) && f[2] != mine.ToString("x16"))
                {
                    Resyncs++;
                    ToastAll($"Desync of {p.Name} at {Game.World.Date.ToDisplay()}: sending a full save");
                    _resync.Add(p);
                }
                break;
        }
    }

    private string? PickFor(PlayerInfo p, string tag)
    {
        var c = Game.World.GetCountry(tag);
        if (c == null || !c.Exists) return $"No such country: {tag}";
        if (Players.Any(o => o != p && o.Active && o.Tag == c.Tag)) return $"{c.Name} is taken";
        if (p.Tag == c.Tag) return null;
        if (p.Tag.Length > 0) Stamp(p.Tag, GameCommand.Release, Array.Empty<string>());
        p.Tag = c.Tag;
        Stamp(c.Tag, GameCommand.Claim, new[] { p.Id.ToString(CultureInfo.InvariantCulture) });
        BroadcastPlayers();
        return null;
    }

    private void Remove(PlayerInfo p, string why)
    {
        p.Active = false;
        p.Link?.Close();
        p.VotePause = null; p.VoteSpeed = null;
        _resync.Remove(p);
        if (p.Tag.Length > 0) Stamp(p.Tag, GameCommand.Release, Array.Empty<string>());
        ToastAll(p.Tag.Length > 0 ? $"{p.Name} left ({why}); {p.Tag} is now AI" : $"{p.Name} left ({why})");
        BroadcastPlayers();
        Tally("pause"); Tally("speed");
    }

    /// <summary>Host: a player leaves by choice (or is kicked).</summary>
    public void Kick(int playerId) { if (Players.FirstOrDefault(p => p.Id == playerId && !p.IsHost && p.Active) is { } p) Remove(p, "removed"); }

    private void BroadcastPlayers() => Broadcast(PlayersLine());

    private string PlayersLine() => "PLAYERS|" + string.Join(';', Players.Select(p =>
        string.Join(',', p.Id, Net.Esc(p.Name), Net.Esc(p.Tag), p.Active ? 1 : 0, p.IsHost ? 1 : 0, p.Ack, p.PingMs)));

    private void SendSync(PlayerInfo p)
    {
        string path = Path.Combine(Path.GetTempPath(), $"burger_mp_{Environment.ProcessId}_{p.Id}.sav");
        SaveGame.Save(Game, path);
        byte[] raw = File.ReadAllBytes(path);
        try { File.Delete(path); } catch { }
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(raw);
        string tags = string.Join(',', Game.World.Countries.Where(c => c.IsPlayer).Select(c => c.Tag).OrderBy(t => t, StringComparer.Ordinal));
        var link = p.Link!;
        link.Send($"SYNC|{Tick}|{tags}|{Convert.ToBase64String(ms.ToArray())}");
        link.Send(PlayersLine());
        foreach (var c in _queue.Values.SelectMany(l => l)) link.Send("CMD|" + c.Encode());
        link.Send($"PACE|{(Paused ? 1 : 0)}|{Speed}");
        link.Send($"HORIZON|{Tick}");
        p.Ack = Tick;
    }

    // ---------------------------------------------------------------------------------------- client side

    private void ClientPump()
    {
        var link = _hostLink!;
        while (link.TryReceive(out var line)) OnClientMessage(line.Split('|'));
        if (!link.Connected && !Disconnected && !link.TryReceive(out _)) { Disconnected = true; Toast?.Invoke("Lost the connection to the host"); }
    }

    private void OnClientMessage(string[] f)
    {
        switch (f[0])
        {
            case "REJECT": Disconnected = true; Toast?.Invoke("Join refused: " + Net.Unesc(f[1])); break;
            case "WELCOME":
            {
                int id = int.Parse(f[1], CultureInfo.InvariantCulture);
                Project = Net.Unesc(f[2]); Seed = int.Parse(f[3], CultureInfo.InvariantCulture);
                Me = new PlayerInfo { Id = id, Ready = true };
                try
                {
                    var g = _loader!(Project, Seed);
                    if (Net.ScriptsHash(g.Data.Root) != f[4]) throw new InvalidDataException("scripts differ from the host's");
                    Game = g; Attach(g); _loadedForJoin = true;
                }
                catch (Exception e)
                {
                    Disconnected = true; _hostLink!.Close();
                    Toast?.Invoke($"Cannot join '{Project}': {e.Message}");
                    return;
                }
                _hostLink!.Send("READY");
                break;
            }
            case "SYNC": Sync(f); break;
            case "PLAYERS": ReadPlayers(f[1]); break;
            case "CMD": if (GameCommand.Decode(f, 1) is { } c) Enqueue(c); break;
            case "HORIZON": _horizon = Math.Max(_horizon, long.Parse(f[1], CultureInfo.InvariantCulture)); break;
            case "PACE": Paused = f[1] == "1"; Speed = int.Parse(f[2], CultureInfo.InvariantCulture); break;
            case "CHAT": Chat?.Invoke(Net.Unesc(f[1]), Net.Unesc(f[2])); break;
            case "TOAST": Toast?.Invoke(Net.Unesc(f[1])); break;
            case "PING": _hostLink!.Send("PONG|" + f[1]); break;
        }
    }

    private void Sync(string[] f)
    {
        long tick = long.Parse(f[1], CultureInfo.InvariantCulture);
        byte[] gz = Convert.FromBase64String(f[3]);
        string path = Path.Combine(Path.GetTempPath(), $"burger_mp_{Environment.ProcessId}_in.sav");
        using (var ms = new MemoryStream(gz))
        using (var z = new GZipStream(ms, CompressionMode.Decompress))
        using (var fs = File.Create(path)) z.CopyTo(fs);
        // the very first sync reuses the game loaded at WELCOME; later ones (desync) need a fresh game
        Game g = _loadedForJoin ? Game : _loader!(Project, Seed);
        _loadedForJoin = false;
        bool first = ReferenceEquals(g, Game) && Tick == 0;
        if (!ReferenceEquals(g, Game)) Attach(g); // before the load: its multiplayer section is read then
        SaveGame.Load(g, path);
        try { File.Delete(path); } catch { }
        foreach (var c in g.World.Countries) c.IsPlayer = false;
        foreach (var tag in f[2].Split(',', StringSplitOptions.RemoveEmptyEntries)) { var c = g.World.GetCountry(tag); if (c != null) c.IsPlayer = true; }
        g.World.Player = null;
        Game = g;
        Tick = tick; _horizon = tick; _queue.Clear();
        if (!first) { Resyncs++; Toast?.Invoke("Resynchronized with the host"); }
        GameReplaced?.Invoke(Game);
    }

    private void ReadPlayers(string s)
    {
        var list = new List<PlayerInfo>();
        foreach (var rec in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var a = rec.Split(',');
            list.Add(new PlayerInfo
            {
                Id = int.Parse(a[0], CultureInfo.InvariantCulture), Name = Net.Unesc(a[1]), Tag = Net.Unesc(a[2]),
                Active = a[3] == "1", IsHost = a[4] == "1", Ack = long.Parse(a[5], CultureInfo.InvariantCulture), PingMs = int.Parse(a[6], CultureInfo.InvariantCulture),
            });
        }
        Players = list;
        if (Me != null)
        {
            var mine = list.FirstOrDefault(p => p.Id == Me.Id);
            if (mine != null) { Me.Name = mine.Name; Me.Tag = mine.Tag; }
            if (Game != null && Me.Tag.Length > 0 && Game.World.Player == null) { var c = Game.World.GetCountry(Me.Tag); if (c != null && c.IsPlayer) Game.World.Player = c; }
        }
    }

    public void Leave()
    {
        if (IsHost) { foreach (var p in Players.Where(p => p.Active && !p.IsHost)) p.Link?.Send("REJECT|" + Net.Esc("Host closed the game")); }
        else _hostLink?.Close();
        Disconnected = true;
    }
}
