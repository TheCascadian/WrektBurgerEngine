using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Burger.Core.Modding;

/// <summary>
/// Compiles the C# files in the game's <c>scripts/</c> folder at load time (with Roslyn) and runs them.
/// Because scripts are compiled when the game starts, players of an exported game can mod the C# too.
/// Compile errors are written to the error log with file and line.
/// </summary>
public sealed class ModManager
{
    private readonly Game _game;
    private readonly List<GameScript> _scripts = new();
    private readonly HashSet<string> _failed = new();

    public ModManager(Game game) { _game = game; }

    public IReadOnlyList<GameScript> Scripts => _scripts;
    public List<string> CompileErrors { get; } = new();
    public Assembly? ScriptAssembly { get; private set; }

    private const string GlobalUsings = @"
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using Burger.Core;
global using Burger.Core.AI;
global using Burger.Core.Characters;
global using Burger.Core.Data;
global using Burger.Core.Diplomacy;
global using Burger.Core.Economy;
global using Burger.Core.Events;
global using Burger.Core.Map;
global using Burger.Core.Mechanics;
global using Burger.Core.Model;
global using Burger.Core.Modding;
global using Burger.Core.Script;
global using Burger.Core.Scripting;
global using Burger.Core.Warfare;
global using Burger.Core.Production;
global using Burger.Core.Supply;
global using Burger.Core.Navy;
global using Burger.Core.Air;
global using Burger.Core.Politics;
global using Burger.Core.Population;
global using Burger.Core.Technology;
";

    /// <summary>Adds a script instance written in normal (pre-compiled) C# code.</summary>
    public void Add(GameScript script) => Add(script, ModPermissions.All);

    public void Add(GameScript script, ModPermissions permissions)
    {
        script.Game = _game;
        string mod = script.GetType().Name;
        script._network = new Burger.Core.Multiplayer.ModNetworkHandle(_game.ModNet, mod, permissions.Network);
        script._content = new Burger.Core.Multiplayer.ModContentHandle(_game.Data.Root, permissions.Content);
        _scripts.Add(script);
        _scripts.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>All script source files of the game (ignores bin/ and obj/ folders).</summary>
    public static List<string> FindScriptFiles(string gameRoot)
    {
        string dir = Path.Combine(gameRoot, "scripts");
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                return !rel.StartsWith("bin/") && !rel.StartsWith("obj/") && !rel.Contains("/bin/") && !rel.Contains("/obj/");
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The project's backend folder (<c>projects/X/backend</c>, next to <c>game/</c>): C# that is compiled into the exported
    /// EXE so players cannot change it. While developing it is compiled at load time like the scripts.
    /// </summary>
    public static string BackendDir(string gameRoot) => Path.GetFullPath(Path.Combine(gameRoot, "..", "backend"));

    public static List<string> FindBackendFiles(string gameRoot)
    {
        string dir = BackendDir(gameRoot);
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => { var r = Path.GetRelativePath(dir, f).Replace('\\', '/'); return !r.StartsWith("bin/") && !r.StartsWith("obj/") && !r.Contains("/bin/") && !r.Contains("/obj/"); })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The backend compiled into an exported EXE (assembly Burger.Backend), if this is one.</summary>
    public static Assembly? BuiltInBackend()
    {
        try { return Assembly.Load("Burger.Backend"); }
        catch { return null; }
    }

    /// <summary>The compiled backend (built into the EXE, or compiled at load time while developing).</summary>
    public Assembly? BackendAssembly { get; private set; }

    public void CompileAndLoad()
    {
        string root = _game.Data.Root;
        var extraRefs = new List<MetadataReference>();
        // 1) backend: locked game code
        var builtIn = BuiltInBackend();
        if (builtIn != null)
        {
            BackendAssembly = builtIn;
            AddScriptsFrom(builtIn, "backend");
        }
        else
        {
            var backendFiles = FindBackendFiles(root);
            if (backendFiles.Count > 0)
            {
                var asm = Compile(backendFiles, root, CompileErrors, null, out var image, "Backend_");
                if (asm != null && image != null)
                {
                    BackendAssembly = asm;
                    extraRefs.Add(MetadataReference.CreateFromImage(image));
                    AddScriptsFrom(asm, "backend");
                }
            }
        }
        // (a built-in backend is already loaded, so GetReferences() includes it)

        // 2) scripts: moddable by players
        var files = FindScriptFiles(root);
        if (files.Count > 0)
        {
            Permissions = ReadManifest(root, new List<string>());
            var asm = Compile(files, root, CompileErrors, extraRefs, out _);
            if (asm != null)
            {
                ScriptAssembly = asm;
                AddScriptsFrom(asm, "scripts");
            }
        }
        if (_scripts.Count > 0) Log.Info($"Loaded {_scripts.Count} C# script(s)" + (BackendAssembly != null ? " (including the backend)" : ""));
    }

    /// <summary>Compiles backend + scripts without running them (editor compile check). Returns (files, script classes).</summary>
    public static (int Files, int Classes) CompileCheck(string gameRoot, List<string> errors)
    {
        int files = 0, classes = 0;
        var extra = new List<MetadataReference>();
        int Count(Assembly a) => a.GetTypes().Count(t => typeof(GameScript).IsAssignableFrom(t) && !t.IsAbstract);
        var backend = FindBackendFiles(gameRoot);
        if (backend.Count > 0)
        {
            files += backend.Count;
            var a = Compile(backend, gameRoot, errors, null, out var image, "Backend_");
            if (a == null) return (files, 0);
            classes += Count(a);
            extra.Add(MetadataReference.CreateFromImage(image!));
        }
        var scripts = FindScriptFiles(gameRoot);
        if (scripts.Count > 0)
        {
            files += scripts.Count;
            var a = Compile(scripts, gameRoot, errors, extra, out _);
            if (a != null) classes += Count(a);
        }
        return (files, classes);
    }

    private void AddScriptsFrom(Assembly asm, string where)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        foreach (var type in types.Where(t => typeof(GameScript).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass))
        {
            try
            {
                if (Activator.CreateInstance(type) is GameScript s) Add(s, where == "backend" ? ModPermissions.All : Permissions);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not create script '{type.Name}': {(ex.InnerException ?? ex).Message}", where);
            }
        }
    }

    private static readonly Dictionary<string, Assembly> _inMemory = new();
    private static bool _resolverInstalled;

    /// <summary>Assemblies compiled in memory cannot be found by name; scripts that use the backend need this.</summary>
    private static void RegisterInMemory(Assembly asm)
    {
        lock (_inMemory)
        {
            _inMemory[asm.GetName().Name!] = asm;
            if (_resolverInstalled) return;
            _resolverInstalled = true;
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                lock (_inMemory) return name != null && _inMemory.TryGetValue(name, out var a) ? a : null;
            };
        }
    }

    /// <summary>Compiles script files. Returns null on errors (which are logged and added to <paramref name="errors"/>).</summary>
    public static Assembly? Compile(List<string> files, string gameRoot, List<string> errors)
        => Compile(files, gameRoot, errors, null, out _);

    /// <summary>What the scripts folder asked for in <c>scripts/mod.json</c>.</summary>
    public ModPermissions Permissions { get; private set; } = ModPermissions.None;

    /// <summary>Reads <c>scripts/mod.json</c> (optional): <c>{ "id": "...", "version": "...", "permissions": ["network", "content"] }</c>.</summary>
    public static ModPermissions ReadManifest(string gameRoot, List<string> errors)
    {
        string path = Path.Combine(gameRoot, "scripts", "mod.json");
        if (!File.Exists(path)) return ModPermissions.None;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("permissions", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray()) if (e.ValueKind == System.Text.Json.JsonValueKind.String) names.Add(e.GetString()!);
            return ModPermissions.Parse(names, errors);
        }
        catch (Exception ex) { errors.Add("scripts/mod.json: " + ex.Message); return ModPermissions.None; }
    }

    public static Assembly? Compile(List<string> files, string gameRoot, List<string> errors, List<MetadataReference>? extraRefs, out byte[]? image, string prefix = "GameScripts_")
    {
        image = null;
        // scripts (moddable by players) are verified against the sandbox policy; the backend is the developer's own locked code
        bool sandbox = prefix == "GameScripts_";
        var manifestErrors = new List<string>();
        var perms = sandbox ? ReadManifest(gameRoot, manifestErrors) : ModPermissions.None;
        foreach (var e in manifestErrors) { Log.Error(e, "scripts"); errors.Add(e); }
        bool Allowed(byte[] img, string source)
        {
            if (!sandbox) return true;
            var v = ModSandbox.Verify(img, perms);
            foreach (var m in v) { string msg = $"Sandbox: the scripts {m}"; Log.Error(msg, "scripts"); errors.Add(msg); }
            return v.Count == 0;
        }
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(GlobalUsings, parse, path: "GlobalUsings.g.cs") };
        foreach (var f in files)
        {
            try { trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f, encoding: System.Text.Encoding.UTF8)); }
            catch (Exception ex) { Log.Error($"Cannot read script: {ex.Message}", f); }
        }
        // compiled scripts are kept on disk, keyed by a hash of every source text, the referenced backend image and the engine build
        string cacheFile = "", cacheKey = "";
        if (Burger.Core.Map.MapCache.Enabled)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            void Add(string t) { sha.TransformBlock(System.Text.Encoding.UTF8.GetBytes(t + "\0"), 0, System.Text.Encoding.UTF8.GetByteCount(t) + 1, null, 0); }
            Add(prefix); Add(sandbox ? FuelRewriter.Version : ""); Add(Environment.Version.ToString()); Add(typeof(Game).Assembly.ManifestModule.ModuleVersionId.ToString());
            foreach (var t in trees) { Add(t.FilePath); Add(t.ToString()); }
            if (extraRefs != null) foreach (var r in extraRefs) Add(r.Display ?? "");
            if (extraRefs != null) foreach (var r in extraRefs) if (r is PortableExecutableReference pe && pe.GetMetadata() is AssemblyMetadata am) Add(am.GetModules()[0].GetModuleVersionId().ToString());
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            cacheKey = Convert.ToHexString(sha.Hash!);
            cacheFile = Path.Combine(gameRoot, "cache", "scripts", prefix.TrimEnd('_') + ".dll");
            try
            {
                if (File.Exists(cacheFile))
                {
                    var bytes = File.ReadAllBytes(cacheFile);
                    int kl = cacheKey.Length;
                    if (bytes.Length > kl && System.Text.Encoding.ASCII.GetString(bytes, 0, kl) == cacheKey)
                    {
                        image = bytes[kl..];
                        if (!Allowed(image, "cache")) { image = null; return null; }
                        var hit = Assembly.Load(image);
                        RegisterInMemory(hit);
                        return hit;
                    }
                }
            }
            catch (Exception ex) { Log.Warn($"Script cache unreadable, compiling again: {ex.Message}"); }
        }
        var refs = GetReferences(out string? refError);
        if (refError != null)
        {
            Log.Error(refError, "scripts");
            errors.Add(refError);
            return null;
        }
        if (extraRefs != null) refs.AddRange(extraRefs);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: !sandbox,
            nullableContextOptions: NullableContextOptions.Annotations);
        var compilation = CSharpCompilation.Create(prefix + Guid.NewGuid().ToString("N"), trees, refs, options);
        if (sandbox && !compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            // every function, loop and handler of a mod gets a step meter (Fuel); errors are reported on the mod's own source above
            var metered = trees.Select(t => FuelRewriter.Rewrite(t, compilation.GetSemanticModel(t))).ToList();
            compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(metered);
        }
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var span = d.Location.GetMappedLineSpan();
            string where = span.Path != null && span.Path.Length > 0
                ? $"{Path.GetRelativePath(gameRoot, span.Path).Replace('\\', '/')}:{span.StartLinePosition.Line + 1}"
                : "scripts";
            string msg = $"C# error {d.Id}: {d.GetMessage()}";
            Log.Error(msg, where);
            errors.Add($"{where}: {msg}");
        }
        if (!result.Success) return null;
        image = ms.ToArray();
        if (!Allowed(image, "build")) { image = null; return null; }
        if (cacheFile.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                string tmp = cacheFile + ".tmp";
                File.WriteAllBytes(tmp, System.Text.Encoding.ASCII.GetBytes(cacheKey).Concat(image).ToArray());
                File.Move(tmp, cacheFile, true);
            }
            catch (Exception ex) { Log.Warn($"Could not write the script cache: {ex.Message}"); }
        }
        var asm = Assembly.Load(image);
        RegisterInMemory(asm);
        return asm;
    }

    private static List<MetadataReference> GetReferences(out string? error)
    {
        error = null;
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void AddPath(string? p)
        {
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
            var name = Path.GetFileName(p);
            if (!paths.ContainsKey(name)) paths[name] = p;
        }
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var p in tpa.Split(Path.PathSeparator)) AddPath(p);
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic) continue;
            string loc;
            try { loc = a.Location; } catch { continue; }
            AddPath(loc);
        }
        AddPath(typeof(Game).Assembly.Location);
        AddPath(typeof(object).Assembly.Location);
        string? runtimeDir = string.IsNullOrEmpty(typeof(object).Assembly.Location) ? null : Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir != null)
            foreach (var p in new[] { "System.Runtime.dll", "netstandard.dll", "System.Collections.dll", "System.Linq.dll", "System.Console.dll" })
                AddPath(Path.Combine(runtimeDir, p));

        if (!paths.ContainsKey("Burger.Core.dll"))
        {
            error = "C# scripts cannot be compiled: the engine assembly location is unknown (is this a single-file build without IncludeAllContentForSelfExtract?)";
            return new List<MetadataReference>();
        }
        var refs = new List<MetadataReference>();
        foreach (var p in paths.Values)
        {
            try
            {
                AssemblyName.GetAssemblyName(p); // throws for native dlls
                refs.Add(MetadataReference.CreateFromFile(p));
            }
            catch { /* not a managed assembly */ }
        }
        return refs;
    }

    /// <summary>Calls a hook on every script, logging (once) instead of crashing when a script throws.</summary>
    public void Invoke(Action<GameScript> call)
    {
        if (_scripts.Count == 0) return;
        foreach (var s in _scripts)
        {
            try { call(s); }
            catch (Exception ex)
            {
                string key = s.GetType().FullName + ":" + ex.GetType().Name + ":" + ex.Message;
                if (_failed.Add(key))
                    Log.Error($"Script '{s.GetType().Name}' threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", "scripts");
            }
        }
    }
}
