using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Burger.Core.Modding;

/// <summary>
/// Static check of a compiled mod assembly. A mod may call the game's API and the safe parts of the .NET base library; it may not reach
/// the file system, the network, processes, reflection, native code, the environment or the clock, and it may not touch the
/// multiplayer layer. The check reads the metadata of the compiled image (every type, member and assembly it references), so it does not
/// depend on how the source was written. It runs on every load, including images taken from the compile cache.
/// <para>
/// This is a verifier, not an operating-system sandbox: it stops mods from naming dangerous APIs, which covers everything C# can express
/// without unsafe code. Mods are never received from the network (each player installs them), so it guards against a hostile or careless
/// mod package, not against a hostile peer. See docs/security-modding.md.
/// </para>
/// </summary>
public static class ModSandbox
{
    /// <summary>Assemblies a mod may reference. Everything else (System.IO.*, System.Net.*, System.Diagnostics.Process, Microsoft.CSharp...) is refused.</summary>
    private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.Ordinal)
    {
        "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq", "System.Collections.Immutable",
        "System.Memory", "System.Runtime.Numerics", "System.Numerics.Vectors", "System.ObjectModel", "netstandard", "mscorlib",
        "Burger.Core", "Burger.Backend",
    };

    /// <summary>Namespaces of the base library a mod may use. Everything not listed is refused (new namespaces start out blocked).</summary>
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "System", "System.Collections", "System.Collections.Generic", "System.Collections.ObjectModel", "System.Collections.Immutable",
        "System.Linq", "System.Text", "System.Globalization", "System.Numerics", "System.Buffers",
        "System.Diagnostics.CodeAnalysis", "System.Runtime.Versioning", "System.Runtime.CompilerServices",
    };

    /// <summary>Types of allowed namespaces that are still refused, and why: reflection, loading, process state, the clock and unseeded randomness.</summary>
    private static readonly HashSet<string> DeniedTypes = new(StringComparer.Ordinal)
    {
        "System.Activator", "System.AppDomain", "System.AppContext", "System.Environment", "System.GC", "System.Random",
        "System.OperatingSystem", "System.TimeZone", "System.TimeZoneInfo", "System.Console", "System.Buffer", "System.RuntimeMethodHandle",
        "System.TypedReference", "System.ArgIterator", "System.MarshalByRefObject", "System.AppDomainManager",
        "System.Runtime.CompilerServices.Unsafe", "System.Runtime.CompilerServices.RuntimeFeature", "System.Runtime.CompilerServices.ConditionalWeakTable`2",
        "System.Runtime.CompilerServices.CallSite", "System.Runtime.CompilerServices.DynamicAttribute", "System.Runtime.CompilerServices.RuntimeWrappedException",
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder", "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Threading.Thread", // System.Threading is not allowed anyway; listed for the error message
        "System.GCCollectionMode", "System.GCMemoryInfo", "System.Memory`1", // Memory<T> can wrap pinned native memory
    };

    /// <summary>For some allowed types only certain members are fine. A member not listed is refused.</summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedMembers = new(StringComparer.Ordinal)
    {
        ["System.Type"] = new(StringComparer.Ordinal) { "GetTypeFromHandle", "get_Name", "get_FullName", "IsAssignableFrom", "op_Equality", "op_Inequality", "Equals", "GetHashCode", "ToString", "get_IsEnum" },
        ["System.Runtime.CompilerServices.RuntimeHelpers"] = new(StringComparer.Ordinal) { "InitializeArray", "GetSubArray" },
        ["System.Delegate"] = new(StringComparer.Ordinal) { "Combine", "Remove", "Equals", "GetHashCode", "op_Equality", "op_Inequality" },
        ["System.MulticastDelegate"] = new(StringComparer.Ordinal) { "Equals", "GetHashCode", "op_Equality", "op_Inequality" },
        ["System.Attribute"] = new(StringComparer.Ordinal) { ".ctor" },
        ["System.Object"] = new(StringComparer.Ordinal) { ".ctor", "Equals", "GetHashCode", "ToString", "GetType", "ReferenceEquals", "Finalize", "MemberwiseClone" },
    };

    /// <summary>Members refused on otherwise allowed types.</summary>
    private static readonly Dictionary<string, HashSet<string>> DeniedMembers = new(StringComparer.Ordinal)
    {
        ["System.DateTime"] = new(StringComparer.Ordinal) { "get_Now", "get_UtcNow", "get_Today" },
        ["System.DateTimeOffset"] = new(StringComparer.Ordinal) { "get_Now", "get_UtcNow" },
        ["System.Guid"] = new(StringComparer.Ordinal) { "NewGuid" },
        ["System.String"] = new(StringComparer.Ordinal) { "Intern", "IsInterned" },
        ["System.Exception"] = new(StringComparer.Ordinal) { "get_TargetSite" },
    };

    /// <summary>Types of the game's own assemblies that a mod may not use (files, loading, saving, networking, the compiler itself).</summary>
    private static readonly string[] DeniedEngineNamespaces =
    {
        "Burger.Core.Multiplayer", "Burger.Core.Save",
    };

    private static readonly HashSet<string> DeniedEngineTypes = new(StringComparer.Ordinal)
    {
        "Burger.Core.Modding.ModManager", "Burger.Core.Modding.ModSandbox", "Burger.Core.Modding.FeaturePackages", "Burger.Core.Modding.EngineAssets",
        "Burger.Core.Map.MapCache", "Burger.Core.Map.MapLoader", "Burger.Core.Map.MapTools", "Burger.Core.Map.ImageIO", "Burger.Core.Map.StateWriter",
        "Burger.Core.Data.DataLoader", "Burger.Core.Script.ScriptWriter", "Burger.Core.Script.ScriptPatch", "Burger.Core.Figures.Glb",
    };

    private static readonly Dictionary<string, HashSet<string>> DeniedEngineMembers = new(StringComparer.Ordinal)
    {
        ["Burger.Core.Game"] = new(StringComparer.Ordinal) { "Load", "add_MapLoaded", "remove_MapLoaded" },
        ["Burger.Core.Log"] = new(StringComparer.Ordinal) { "SetLogFile", "Clear" },
        ["Burger.Core.Localisation"] = new(StringComparer.Ordinal) { "LoadDirectory" },
        ["Burger.Core.Script.ScriptParser"] = new(StringComparer.Ordinal) { "ParseFile" },
    };

    /// <summary>Checks a compiled assembly. Returns one readable line per violation (empty = the mod may load).</summary>
    public static List<string> Verify(byte[] image, ModPermissions? permissions = null)
    {
        var errors = new List<string>();
        permissions ??= ModPermissions.None;
        try
        {
            using var pe = new PEReader(new MemoryStream(image, false));
            if (!pe.HasMetadata) { errors.Add("not a managed assembly"); return errors; }
            var md = pe.GetMetadataReader();
            string self = md.GetString(md.GetAssemblyDefinition().Name);

            if (md.GetTableRowCount(TableIndex.ModuleRef) > 0 || md.GetTableRowCount(TableIndex.ImplMap) > 0) errors.Add("uses native code (DllImport / P/Invoke)");

            foreach (var h in md.AssemblyReferences)
            {
                string name = md.GetString(md.GetAssemblyReference(h).Name);
                if (!AllowedAssemblies.Contains(name) && !name.StartsWith("Backend_", StringComparison.Ordinal) && !name.StartsWith("Burger.Backend", StringComparison.Ordinal) && name != self)
                    errors.Add($"references assembly '{name}', which mods may not use");
            }

            foreach (var h in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(h);
                if ((td.Attributes & System.Reflection.TypeAttributes.ExplicitLayout) != 0) errors.Add($"type '{md.GetString(td.Name)}' uses explicit field layout");
            }

            foreach (var h in md.TypeReferences)
            {
                string? full = TypeName(md, h, out string asm);
                if (full == null) continue;
                string? why = CheckType(full, asm, permissions);
                if (why != null) errors.Add(why);
            }

            foreach (var h in md.MemberReferences)
            {
                var mr = md.GetMemberReference(h);
                string member = md.GetString(mr.Name);
                string? owner = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => TypeName(md, (TypeReferenceHandle)mr.Parent, out _),
                    _ => null,
                };
                if (owner == null) continue;
                string? why = CheckMember(owner, member);
                if (why != null) errors.Add(why);
            }

            foreach (var h in md.CustomAttributes)
            {
                var ca = md.GetCustomAttribute(h);
                if (ca.Constructor.Kind != HandleKind.MemberReference) continue;
                var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                if (mr.Parent.Kind == HandleKind.TypeReference && TypeName(md, (TypeReferenceHandle)mr.Parent, out _) == "System.Security.UnverifiableCodeAttribute") errors.Add("contains unsafe code");
            }
        }
        catch (Exception ex) { errors.Add("the assembly could not be read: " + ex.Message); }
        return errors.Distinct().ToList();
    }

    private static string? TypeName(MetadataReader md, TypeReferenceHandle h, out string assembly)
    {
        assembly = "";
        var tr = md.GetTypeReference(h);
        string name = md.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            string? outer = TypeName(md, (TypeReferenceHandle)tr.ResolutionScope, out assembly);
            return outer == null ? null : outer + "+" + name;
        }
        if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference) assembly = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name);
        string ns = md.GetString(tr.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string? CheckType(string full, string assembly, ModPermissions perms)
    {
        string outermost = full.Split('+')[0];
        int dot = outermost.LastIndexOf('.');
        string ns = dot < 0 ? "" : outermost[..dot];
        if (assembly.StartsWith("Burger.", StringComparison.Ordinal) || assembly.StartsWith("Backend_", StringComparison.Ordinal))
        {
            foreach (var d in DeniedEngineNamespaces) if (ns == d || ns.StartsWith(d + ".", StringComparison.Ordinal)) return $"uses the engine's {d} layer ({outermost}), which mods may not use";
            if (DeniedEngineTypes.Contains(outermost)) return $"uses {outermost}, which mods may not use (files, loading or compiling)";
            return null;
        }
        if (assembly.Length == 0) return null; // a type of this assembly
        if (!AllowedNamespaces.Contains(ns)) return $"uses {outermost} ({ns} is not available to mods)";
        if (DeniedTypes.Contains(outermost)) return $"uses {outermost}, which mods may not use";
        if (ns == "System.Runtime.CompilerServices" && !CompilerServicesAllowed.Contains(outermost)) return $"uses {outermost}, which mods may not use";
        return null;
    }

    private static readonly HashSet<string> CompilerServicesAllowed = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute", "System.Runtime.CompilerServices.IsExternalInit", "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute", "System.Runtime.CompilerServices.RuntimeHelpers", "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
        "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute", "System.Runtime.CompilerServices.InterpolatedStringHandlerArgumentAttribute", "System.Runtime.CompilerServices.ITuple",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute", "System.Runtime.CompilerServices.ExtensionAttribute", "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute", "System.Runtime.CompilerServices.IsByRefLikeAttribute", "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute", "System.Runtime.CompilerServices.FormattableStringFactory", "System.Runtime.CompilerServices.SwitchExpressionException",
        "System.Runtime.CompilerServices.StrongBox`1", "System.Runtime.CompilerServices.IsVolatile", "System.Runtime.CompilerServices.IsConst", "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.CallerMemberNameAttribute", "System.Runtime.CompilerServices.CallerLineNumberAttribute", "System.Runtime.CompilerServices.CallerFilePathAttribute",
        "System.Runtime.CompilerServices.EnumeratorCancellationAttribute", "System.Runtime.CompilerServices.ParamCollectionAttribute", "System.Runtime.CompilerServices.InlineArrayAttribute",
        "System.Runtime.CompilerServices.MethodImplOptions", "System.Runtime.CompilerServices.MethodImplAttribute", "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute", "System.Runtime.CompilerServices.ScopedRefAttribute", "System.Runtime.CompilerServices.IsUnmanagedAttribute",
    };

    private static string? CheckMember(string ownerFull, string member)
    {
        string owner = ownerFull.Split('+')[0];
        if (DeniedEngineMembers.TryGetValue(owner, out var denied) && denied.Contains(member)) return $"calls {owner}.{member}, which mods may not use";
        if (AllowedMembers.TryGetValue(owner, out var allowed) && !allowed.Contains(member)) return $"calls {owner}.{member}, which mods may not use (reflection or dynamic invocation)";
        if (DeniedMembers.TryGetValue(owner, out var dm) && dm.Contains(member))
            return $"calls {owner}.{member}, which mods may not use (not the same on every machine; use the game's clock and random streams)";
        return null;
    }
}

/// <summary>What a mod says it needs (<c>scripts/mod.json</c>). The game enforces it: a mod without a permission gets no access to that feature.</summary>
public sealed class ModPermissions
{
    public static readonly ModPermissions None = new();
    /// <summary>The developer's own locked code (backend) and scripts written as normal compiled C# have every permission.</summary>
    public static readonly ModPermissions All = new() { Network = true, Content = true };
    /// <summary>The mod may register custom network messages (<see cref="IModNetwork"/>).</summary>
    public bool Network;
    /// <summary>The mod may read data files from its own folder through <see cref="IModContent"/>.</summary>
    public bool Content;

    public static readonly string[] Known = { "network", "content" };

    public static ModPermissions Parse(IEnumerable<string> names, List<string> errors)
    {
        var p = new ModPermissions();
        foreach (var n in names)
        {
            switch (n)
            {
                case "network": p.Network = true; break;
                case "content": p.Content = true; break;
                default: errors.Add($"unknown permission '{n}' (known: {string.Join(", ", Known)})"); break;
            }
        }
        return p;
    }
}
