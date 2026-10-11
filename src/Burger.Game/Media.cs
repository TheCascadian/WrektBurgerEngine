using System.Diagnostics;
using System.Globalization;
using Burger.Core;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Animated pictures for the UI: .gif files (decoded by raylib) and video files such as .mp4, .webm or .mov (decoded by an
/// external <c>ffmpeg</c> and <c>ffprobe</c> found on the PATH). A clip only runs while something draws it, and clips with
/// sound play it through raylib's audio device when <see cref="AudioEnabled"/> is on and the drawer asks for audio.
/// Call <see cref="Get"/> where the image is drawn and <see cref="EndFrame"/> once per frame.
/// </summary>
public static class Media
{
    private static readonly string[] GifExt = { ".gif" };
    private static readonly string[] VideoExt = { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v" };
    private static readonly Dictionary<string, Clip?> _clips = new(StringComparer.OrdinalIgnoreCase);
    private static bool _audioInit, _audioReady, _warnedNoFfmpeg;
    private static long _frame;

    /// <summary>Master switch for clip sound (a UI mute button toggles it; the choice is kept in <see cref="UserSettings"/>).</summary>
    public static bool AudioEnabled = true;
    /// <summary>Playback volume of clip sound, 0-1.</summary>
    public static float Volume = 0.6f;
    /// <summary>True once the audio device opened; false on machines without sound output.</summary>
    public static bool AudioReady => _audioReady;
    /// <summary>Longest side a clip is decoded at; larger sources are scaled down to save memory.</summary>
    public const int MaxSide = 256;

    internal static string Root = "";

    /// <summary>True for file types this class animates; everything else is an ordinary texture.</summary>
    public static bool IsAnimated(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path);
        return GifExt.Contains(ext, StringComparer.OrdinalIgnoreCase) || VideoExt.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The clip for a game-folder path (loaded on first use), or null when the file is missing or cannot be decoded.</summary>
    public static Clip? Get(string? relative)
    {
        if (!IsAnimated(relative)) return null;
        if (_clips.TryGetValue(relative!, out var cached)) return cached;
        Clip? clip = null;
        string full = Path.Combine(Root, relative!.Replace('\\', '/'));
        try
        {
            if (File.Exists(full))
                clip = GifExt.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase) ? GifClip.Load(full) : VideoClip.Load(full);
            else Log.WarnOnce("media:" + relative, $"Animated image '{relative}' not found");
        }
        catch (Exception ex) { Log.WarnOnce("media:" + relative, $"Cannot load animated image '{relative}': {ex.Message}"); }
        _clips[relative] = clip;
        return clip;
    }

    /// <summary>Advances every clip that was drawn this frame and parks the others. Call after drawing, once per frame.</summary>
    public static void EndFrame()
    {
        _frame++;
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        foreach (var clip in _clips.Values)
            if (clip != null) clip.Step(_frame, now);
    }

    /// <summary>Stops decoders and frees textures (call when the game folder changes and on exit).</summary>
    public static void Shutdown()
    {
        foreach (var clip in _clips.Values) clip?.Dispose();
        _clips.Clear();
        if (_audioReady) { Raylib.CloseAudioDevice(); }
        _audioReady = _audioInit = false;
    }

    internal static bool EnsureAudio()
    {
        if (_audioInit) return _audioReady;
        _audioInit = true;
        try
        {
            Raylib.InitAudioDevice();
            _audioReady = Raylib.IsAudioDeviceReady();
        }
        catch (Exception ex) { Log.Warn("Audio unavailable: " + ex.Message); }
        if (!_audioReady) Log.Warn("No audio device: animated images play silently");
        return _audioReady;
    }

    internal static long CurrentFrame => _frame;

    internal static bool ToolAvailable(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            p.WaitForExit(4000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    internal static void WarnNoFfmpeg()
    {
        if (_warnedNoFfmpeg) return;
        _warnedNoFfmpeg = true;
        Log.Warn("ffmpeg/ffprobe not found on the PATH: video portraits are unavailable (gif and still images still work)");
    }

    /// <summary>One animated image. Draw <see cref="Texture"/> after calling <see cref="Use"/>.</summary>
    public abstract class Clip : IDisposable
    {
        private long _usedFrame = -10;
        private bool _wantAudio;
        /// <summary>The current picture (valid while the clip lives).</summary>
        public Texture2D Texture { get; protected set; }
        public int Width => Texture.Width;
        public int Height => Texture.Height;
        /// <summary>True when the file has a sound track.</summary>
        public virtual bool HasAudio => false;

        /// <summary>Marks the clip as visible this frame; <paramref name="audio"/> asks for its sound too.</summary>
        public Texture2D Use(bool audio = false)
        {
            _usedFrame = CurrentFrame;
            _wantAudio |= audio;
            return Texture;
        }

        /// <summary>Returns the picture without starting playback: a video shows its first frame (decoded once in the background), a gif stays on its current frame.</summary>
        public Texture2D Still()
        {
            _stillWanted = true;
            return Texture;
        }

        internal bool StillWanted => _stillWanted;
        private bool _stillWanted;

        internal void Step(long frame, double now)
        {
            bool visible = _usedFrame >= frame - 1;
            bool audio = visible && _wantAudio && AudioEnabled;
            _wantAudio = false;
            Advance(visible, audio, now);
        }

        protected abstract void Advance(bool visible, bool audio, double now);
        public abstract void Dispose();
    }

    // ---------------------------------------------------------------------------------------------- gif
    private sealed class GifClip : Clip
    {
        private Texture2D[] _frames = Array.Empty<Texture2D>();
        private double[] _delays = Array.Empty<double>();
        private int _index;
        private double _next;
        private bool _wasVisible;

        public static unsafe GifClip? Load(string path)
        {
            // raylib returns one frame's width and height; the pixel data holds every frame back to back
            var img = Raylib.LoadImageAnim(path, out int count);
            if (img.Width <= 0 || img.Height <= 0 || count <= 0) return null;
            int w = img.Width, h = img.Height;
            var clip = new GifClip();
            clip._frames = new Texture2D[count];
            clip._delays = ReadGifDelays(path, count);
            int bytes = w * h * 4;
            var buf = new byte[bytes];
            for (int i = 0; i < count; i++)
            {
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)((byte*)img.Data + (long)i * bytes), buf, 0, bytes);
                var blank = Raylib.GenImageColor(w, h, Color.Blank);
                var tex = Raylib.LoadTextureFromImage(blank);
                Raylib.UnloadImage(blank);
                Raylib.UpdateTexture(tex, buf);
                Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
                clip._frames[i] = tex;
            }
            Raylib.UnloadImage(img);
            clip.Texture = clip._frames[0];
            return clip;
        }

        protected override void Advance(bool visible, bool audio, double now)
        {
            if (!visible || _frames.Length < 2) { _wasVisible = false; return; }
            if (!_wasVisible) { _wasVisible = true; _next = now + _delays[_index]; return; }
            // catch up after a slow frame, but never spin through more than one loop at once
            for (int guard = 0; now >= _next && guard < _frames.Length; guard++)
            {
                _index = (_index + 1) % _frames.Length;
                _next += _delays[_index];
            }
            if (now >= _next) _next = now + _delays[_index];
            Texture = _frames[_index];
        }

        public override void Dispose()
        {
            foreach (var t in _frames) if (t.Id != 0) Raylib.UnloadTexture(t);
            _frames = Array.Empty<Texture2D>();
        }

        /// <summary>Frame delays in seconds from the graphic control extensions (raylib does not return them); 10 fps when absent.</summary>
        private static double[] ReadGifDelays(string path, int count)
        {
            var result = new double[count];
            Array.Fill(result, 0.1);
            try
            {
                var b = File.ReadAllBytes(path);
                int found = 0;
                for (int i = 13; i + 7 < b.Length && found < count; i++)
                {
                    if (b[i] == 0x21 && b[i + 1] == 0xF9 && b[i + 2] == 4)
                    {
                        int cs = b[i + 4] | (b[i + 5] << 8);
                        result[found++] = cs <= 1 ? 0.1 : cs / 100.0;
                        i += 7;
                    }
                }
            }
            catch { /* the default pace is fine */ }
            return result;
        }
    }

    // ---------------------------------------------------------------------------------------------- video
    private sealed class VideoClip : Clip
    {
        private string _path = "";
        private int _w, _h;
        private double _fps = 24;
        private bool _hasAudio;
        private byte[] _front = Array.Empty<byte>(), _back = Array.Empty<byte>();
        private readonly object _lock = new();
        private bool _dirty;
        private int _loops, _seenLoops;
        private Thread? _thread;
        private volatile bool _stop;
        private Process? _proc;

        private string? _wavPath;
        private volatile bool _wavReady, _wavStarted;
        private Music _music;
        private bool _musicLoaded, _playing;

        public override bool HasAudio => _hasAudio;

        public static VideoClip? Load(string path)
        {
            if (!ToolAvailable("ffprobe") || !ToolAvailable("ffmpeg")) { WarnNoFfmpeg(); return null; }
            var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate", "-of", "csv=p=0", path })
                psi.ArgumentList.Add(a);
            string line;
            using (var p = Process.Start(psi)!)
            {
                line = p.StandardOutput.ReadLine() ?? "";
                p.WaitForExit(8000);
            }
            var f = line.Split(',');
            if (f.Length < 3 || !int.TryParse(f[0], out int sw) || !int.TryParse(f[1], out int sh) || sw <= 0 || sh <= 0) return null;
            double fps = 24;
            var fr = f[2].Split('/');
            if (fr.Length == 2 && double.TryParse(fr[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.TryParse(fr[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && n > 0)
                fps = Math.Clamp(n / d, 5, 30);

            float scale = Math.Min(1f, MaxSide / (float)Math.Max(sw, sh));
            var clip = new VideoClip { _path = path, _w = Math.Max(2, (int)(sw * scale) / 2 * 2), _h = Math.Max(2, (int)(sh * scale) / 2 * 2), _fps = fps };
            clip._hasAudio = ProbeAudio(path);
            clip._front = new byte[clip._w * clip._h * 4];
            clip._back = new byte[clip._front.Length];
            var blank = Raylib.GenImageColor(clip._w, clip._h, new Color(24, 24, 28, 255));
            var tex = Raylib.LoadTextureFromImage(blank);
            Raylib.UnloadImage(blank);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            clip.Texture = tex;
            return clip;
        }

        private static bool ProbeAudio(string path)
        {
            var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_type", "-of", "csv=p=0", path })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return o.Contains("audio", StringComparison.OrdinalIgnoreCase);
        }

        protected override void Advance(bool visible, bool audio, double now)
        {
            if (StillWanted && !visible && !_posterDone && !_posterBusy) { _posterBusy = true; Task.Run(DecodePoster); }
            lock (_lock)
            {
                if (_dirty)
                {
                    Raylib.UpdateTexture(Texture, _front);
                    _dirty = false;
                }
            }
            if (!visible) { StopDecoder(); PauseAudio(); return; }
            StartDecoder();
            if (_hasAudio) StepAudio(audio);
        }

        // ---- decoder thread: ffmpeg writes raw RGBA frames, paced to the clip's frame rate, restarting at the end (loop)
        private void StartDecoder()
        {
            if (_thread is { IsAlive: true }) return;
            _stop = false;
            _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "media-decoder" };
            _thread.Start();
        }

        private void StopDecoder()
        {
            if (_thread == null) return;
            _stop = true;
            try { _proc?.Kill(); } catch { /* already gone */ }
            _thread = null;
        }

        private void DecodeLoop()
        {
            int size = _w * _h * 4;
            var buf = new byte[size];
            while (!_stop)
            {
                var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "-v", "error", "-i", _path, "-an", "-vf", $"fps={_fps.ToString(CultureInfo.InvariantCulture)},scale={_w}:{_h}", "-f", "rawvideo", "-pix_fmt", "rgba", "-" })
                    psi.ArgumentList.Add(a);
                try
                {
                    using var p = Process.Start(psi);
                    if (p == null) return;
                    _proc = p;
                    p.ErrorDataReceived += (_, _) => { };
                    p.BeginErrorReadLine();
                    var stream = p.StandardOutput.BaseStream;
                    var clock = Stopwatch.StartNew();
                    long n = 0;
                    while (!_stop)
                    {
                        int got = 0;
                        while (got < size)
                        {
                            int r = stream.Read(buf, got, size - got);
                            if (r <= 0) break;
                            got += r;
                        }
                        if (got < size) break;
                        // hold each frame until its time (ffmpeg decodes faster than real time)
                        double due = n / _fps;
                        double wait = due - clock.Elapsed.TotalSeconds;
                        if (wait > 0.002) Thread.Sleep((int)(wait * 1000));
                        n++;
                        lock (_lock)
                        {
                            Buffer.BlockCopy(buf, 0, _front, 0, size);
                            _dirty = true;
                        }
                    }
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
                catch (Exception ex) { Log.WarnOnce("media-dec:" + _path, $"Video decoding failed for '{_path}': {ex.Message}"); return; }
                if (!_stop) Interlocked.Increment(ref _loops);
            }
        }

        private volatile bool _posterDone, _posterBusy;

        private void DecodePoster()
        {
            try
            {
                int size = _w * _h * 4;
                var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "-v", "error", "-i", _path, "-an", "-frames:v", "1", "-vf", $"scale={_w}:{_h}", "-f", "rawvideo", "-pix_fmt", "rgba", "-" })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                p.ErrorDataReceived += (_, _) => { };
                p.BeginErrorReadLine();
                var buf = new byte[size];
                var stream = p.StandardOutput.BaseStream;
                int got = 0;
                while (got < size) { int r = stream.Read(buf, got, size - got); if (r <= 0) break; got += r; }
                if (got == size) lock (_lock) { Buffer.BlockCopy(buf, 0, _front, 0, size); _dirty = true; }
            }
            catch (Exception ex) { Log.WarnOnce("media-poster:" + _path, $"Cannot read the first frame of '{_path}': {ex.Message}"); }
            finally { _posterDone = true; _posterBusy = false; }
        }

        // ---- sound: the track is extracted once to a temporary wav and streamed through raylib
        private void StepAudio(bool want)
        {
            if (!want) { PauseAudio(); return; }
            if (!EnsureAudio()) return;
            if (!_wavStarted)
            {
                _wavStarted = true;
                string dir = Path.Combine(Path.GetTempPath(), "burgerengine_media");
                Directory.CreateDirectory(dir);
                var info = new FileInfo(_path);
                string key = (_path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks).GetHashCode().ToString("x8");
                _wavPath = Path.Combine(dir, key + ".wav");
                if (File.Exists(_wavPath) && new FileInfo(_wavPath).Length > 44) _wavReady = true;
                else Task.Run(ExtractWav);
            }
            if (!_wavReady) return;
            if (!_musicLoaded)
            {
                _music = Raylib.LoadMusicStream(_wavPath!);
                _musicLoaded = _music.FrameCount > 0;
                if (!_musicLoaded) return;
                _music.Looping = true;
            }
            Raylib.SetMusicVolume(_music, Volume);
            if (_seenLoops != _loops) { _seenLoops = _loops; Raylib.SeekMusicStream(_music, 0); }
            if (!_playing) { Raylib.PlayMusicStream(_music); _playing = true; }
            Raylib.UpdateMusicStream(_music);
        }

        private void PauseAudio()
        {
            if (_musicLoaded && _playing) { Raylib.PauseMusicStream(_music); _playing = false; }
        }

        private void ExtractWav()
        {
            try
            {
                string tmp = _wavPath + ".part";
                var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "-y", "-v", "error", "-i", _path, "-vn", "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", "-f", "wav", tmp })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                if (p.ExitCode == 0 && File.Exists(tmp)) { File.Move(tmp, _wavPath!, true); _wavReady = true; }
            }
            catch (Exception ex) { Log.Warn($"Could not extract sound from '{_path}': {ex.Message}"); }
        }

        public override void Dispose()
        {
            StopDecoder();
            if (_musicLoaded) { Raylib.StopMusicStream(_music); Raylib.UnloadMusicStream(_music); _musicLoaded = false; }
            if (Texture.Id != 0) Raylib.UnloadTexture(Texture);
        }
    }
}
