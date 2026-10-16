using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>The shape of a soldier's headgear.</summary>
public enum HeadgearStyle
{
    /// <summary>The plain steel pot with a rolled brim (the generic soldier).</summary>
    Pot,
    /// <summary>Wide, shallow bowl with a broad brim all round.</summary>
    Brodie,
    /// <summary>Deep coal-scuttle shape with a flared neck guard and ear flaps.</summary>
    Stahlhelm,
    /// <summary>Domed helmet with a crest along the top, a front peak and a short rear brim.</summary>
    Adrian,
    /// <summary>Stiff peaked field cap.</summary>
    PeakedCap,
    /// <summary>Soft cloth cap with a short peak.</summary>
    SoftCap,
    /// <summary>Field cap with a cloth curtain hanging over the neck.</summary>
    FlapCap,
}

/// <summary>The cut of the boots.</summary>
public enum BootKind { Ankle, Hobnail, Heavy, Tall, Soft, Lace, Cloth }

/// <summary>The cut of the tunic (and the pockets, collar and skirts that go with it).</summary>
public enum ShirtKind { Service, Jacket, Waffenrock, Capote, Gymnastyorka, Cotton, Stand }

/// <summary>The pack and how the blanket or greatcoat is carried on it.</summary>
public enum PackKind { Rucksack, Light, Tornister, Horseshoe, Sack, Diagonal, Knapsack }

/// <summary>The belt and what hangs from it (and, for the bandolier, across the chest).</summary>
public enum BeltKind { Standard, Web, Cartridge, Leather3, Bidon, Sam, Bandolier, Box }

/// <summary>
/// What a country's infantry wears: colours and the shape of the headgear. Plain cloth and leather only, with no badges, flags or
/// emblems of any kind: nations read apart by colour and silhouette. <see cref="Puttees"/> is the colour of leg wraps, or null.
/// <see cref="FireClip"/> is the firing drill the country's soldiers use (one of <see cref="HumanMale.FireStyles"/>, or a clip a mod adds), and
/// <see cref="Rifle"/> the rifle they carry (<see cref="SoldierGear.SemiAuto"/>); null for either is the default drill and the basic rifle.
/// Who wears it comes from the country's people (<see cref="FigureSystem.SoldierFor"/>): <see cref="Look"/> makes every soldier one look (an <see cref="Ancestries"/> key),
/// <see cref="Mix"/> replaces the shares taken from the population (culture or group key to weight), and <see cref="Remap"/> dresses a culture or group in another look (key to look key).
/// </summary>
public sealed record Uniform(Vector3 Tunic, Vector3 Trousers, Vector3 Leather, Vector3 Webbing, Vector3 Headgear, HeadgearStyle Style,
    Vector3 Canvas, Vector3 Bedroll, Vector3? Puttees = null, string? FireClip = null, string? Rifle = null,
    BootKind BootKind = default, ShirtKind ShirtKind = default, PackKind PackKind = default, BeltKind BeltKind = default,
    string? Look = null, double FemaleShare = 0, IReadOnlyDictionary<string, string>? Remap = null, IReadOnlyDictionary<string, double>? Mix = null)
{
    /// <summary>The generic soldier, worn by any country without a uniform of its own.</summary>
    public static readonly Uniform Standard = new(new(0.36f, 0.37f, 0.26f), new(0.31f, 0.32f, 0.23f), new(0.22f, 0.14f, 0.085f),
        new(0.30f, 0.27f, 0.19f), new(0.29f, 0.33f, 0.25f), HeadgearStyle.Pot, new(0.34f, 0.33f, 0.22f), new(0.42f, 0.37f, 0.25f));
}

/// <summary>
/// The uniforms by country tag. The built-in major powers are here; a game or mod adds or changes one with <see cref="Set"/>
/// before the figures are first used. Each becomes the outfit <c>soldier_&lt;tag&gt;</c> (lower case), and a .glb called
/// <c>gear_helmet_&lt;tag&gt;.glb</c> (and so on for the other pieces) replaces a piece of it.
/// </summary>
public static class Uniforms
{
    private static readonly Dictionary<string, Uniform> _byTag = new(StringComparer.OrdinalIgnoreCase)
    {
        // khaki wool, Brodie helmet, puttees
        ["ENG"] = new(new(0.45f, 0.40f, 0.27f), new(0.42f, 0.37f, 0.25f), new(0.24f, 0.15f, 0.09f), new(0.36f, 0.34f, 0.24f),
            new(0.30f, 0.31f, 0.23f), HeadgearStyle.Brodie, new(0.36f, 0.35f, 0.24f), new(0.40f, 0.36f, 0.25f), new Vector3(0.38f, 0.35f, 0.24f), BootKind: BootKind.Ankle, ShirtKind: ShirtKind.Service, PackKind: PackKind.Rucksack, BeltKind: BeltKind.Web),
        // olive drab, Brodie style helmet
        ["USA"] = new(new(0.40f, 0.39f, 0.25f), new(0.39f, 0.37f, 0.25f), new(0.26f, 0.16f, 0.09f), new(0.38f, 0.36f, 0.25f),
            new(0.32f, 0.34f, 0.23f), HeadgearStyle.Brodie, new(0.37f, 0.36f, 0.24f), new(0.42f, 0.38f, 0.26f), Rifle: SoldierGear.SemiAuto, BootKind: BootKind.Lace, ShirtKind: ShirtKind.Jacket, PackKind: PackKind.Light, BeltKind: BeltKind.Cartridge),
        // field grey, steel helmet with a neck guard, black leather
        ["GER"] = new(new(0.33f, 0.36f, 0.34f), new(0.30f, 0.32f, 0.31f), new(0.10f, 0.09f, 0.085f), new(0.12f, 0.115f, 0.11f),
            new(0.27f, 0.30f, 0.30f), HeadgearStyle.Stahlhelm, new(0.30f, 0.31f, 0.28f), new(0.36f, 0.35f, 0.30f), FireClip: HumanMale.FireSide, BootKind: BootKind.Tall, ShirtKind: ShirtKind.Waffenrock, PackKind: PackKind.Tornister, BeltKind: BeltKind.Leather3),
        // horizon blue, crested helmet
        ["FRA"] = new(new(0.44f, 0.52f, 0.63f), new(0.40f, 0.48f, 0.59f), new(0.25f, 0.16f, 0.10f), new(0.30f, 0.26f, 0.18f),
            new(0.33f, 0.41f, 0.53f), HeadgearStyle.Adrian, new(0.36f, 0.39f, 0.40f), new(0.42f, 0.40f, 0.34f), new Vector3(0.38f, 0.44f, 0.54f), BootKind: BootKind.Hobnail, ShirtKind: ShirtKind.Capote, PackKind: PackKind.Horseshoe, BeltKind: BeltKind.Bidon),
        // khaki green, peaked field cap
        ["SOV"] = new(new(0.38f, 0.40f, 0.24f), new(0.30f, 0.33f, 0.25f), new(0.12f, 0.10f, 0.085f), new(0.26f, 0.24f, 0.17f),
            new(0.38f, 0.40f, 0.24f), HeadgearStyle.PeakedCap, new(0.30f, 0.31f, 0.22f), new(0.38f, 0.35f, 0.26f), FireClip: HumanMale.FireAggressive, FemaleShare: 0.04, Rifle: SoldierGear.SemiAuto, BootKind: BootKind.Soft, ShirtKind: ShirtKind.Gymnastyorka, PackKind: PackKind.Sack, BeltKind: BeltKind.Sam),
        // grey-blue cotton, soft cloth cap, cloth shoes
        ["CHI"] = new(new(0.42f, 0.46f, 0.47f), new(0.40f, 0.43f, 0.44f), new(0.22f, 0.20f, 0.17f), new(0.32f, 0.31f, 0.26f),
            new(0.40f, 0.44f, 0.45f), HeadgearStyle.SoftCap, new(0.36f, 0.35f, 0.28f), new(0.44f, 0.41f, 0.32f), new Vector3(0.40f, 0.42f, 0.40f), BootKind: BootKind.Cloth, ShirtKind: ShirtKind.Cotton, PackKind: PackKind.Diagonal, BeltKind: BeltKind.Bandolier),
        // tan khaki, field cap with a neck curtain, puttees
        ["JAP"] = new(new(0.46f, 0.40f, 0.22f), new(0.44f, 0.38f, 0.21f), new(0.27f, 0.17f, 0.09f), new(0.30f, 0.24f, 0.14f),
            new(0.45f, 0.39f, 0.22f), HeadgearStyle.FlapCap, new(0.38f, 0.34f, 0.20f), new(0.42f, 0.36f, 0.22f), new Vector3(0.42f, 0.37f, 0.22f), HumanMale.FireAggressive, BootKind: BootKind.Heavy, ShirtKind: ShirtKind.Stand, PackKind: PackKind.Knapsack, BeltKind: BeltKind.Box),
        // sand khaki, crested helmet
        ["MEX"] = new(new(0.52f, 0.45f, 0.30f), new(0.49f, 0.43f, 0.29f), new(0.28f, 0.18f, 0.10f), new(0.40f, 0.34f, 0.22f),
            new(0.40f, 0.38f, 0.25f), HeadgearStyle.Adrian, new(0.42f, 0.38f, 0.26f), new(0.48f, 0.42f, 0.30f)),
        // saffron tunic, brown leather, soft cloth cap (Achaemenid; gfx/models can replace each piece with gear_*_ach.glb)
        ["ACH"] = new(new(0.85f, 0.60f, 0.13f), new(0.45f, 0.30f, 0.20f), new(0.28f, 0.17f, 0.10f), new(0.44f, 0.23f, 0.17f),
            new(0.45f, 0.30f, 0.20f), HeadgearStyle.SoftCap, new(0.36f, 0.35f, 0.24f), new(0.40f, 0.36f, 0.25f), Rifle: SoldierGear.None, BootKind: BootKind.Soft),
        // green-olive, crested helmet
        ["BRA"] = new(new(0.40f, 0.43f, 0.25f), new(0.36f, 0.40f, 0.24f), new(0.24f, 0.15f, 0.09f), new(0.32f, 0.31f, 0.20f),
            new(0.32f, 0.38f, 0.24f), HeadgearStyle.Adrian, new(0.34f, 0.37f, 0.22f), new(0.40f, 0.38f, 0.26f)),
    };

    /// <summary>The tags that have a uniform of their own.</summary>
    public static IEnumerable<string> Tags => _byTag.Keys;

    /// <summary>The uniform of a country, or null (then the generic soldier is worn).</summary>
    public static Uniform? Find(string tag) => _byTag.GetValueOrDefault(tag);

    /// <summary>Adds or replaces the uniform of a country.</summary>
    public static void Set(string tag, Uniform uniform) => _byTag[tag] = uniform;

    /// <summary>
    /// The clip a country's soldiers play for <paramref name="clip"/> (an animation family such as <see cref="HumanMale.Fire"/>) on this model: a
    /// clip made for the country (<c>fire_ger</c>), else the variant its uniform names, else the default clip, else the fallback drill. The first that exists.
    /// </summary>
    public static string ClipFor(string tag, string clip, FigureModel model)
    {
        var u = Find(tag);
        var candidates = new List<string?> { clip + "_" + tag.ToLowerInvariant(), clip == HumanMale.Fire ? u?.FireClip : null, clip };
        if (clip == HumanMale.Fire) candidates.Add(HumanMale.FireSide);
        return candidates.FirstOrDefault(c => c != null && model.Clips.ContainsKey(c)) ?? clip;
    }

    /// <summary>The key of the outfit a country's soldiers use (it exists only for countries with a uniform).</summary>
    public static string OutfitKey(string tag) => SoldierGear.Soldier + "_" + tag.ToLowerInvariant();
}
