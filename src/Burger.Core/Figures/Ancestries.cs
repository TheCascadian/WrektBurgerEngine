using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>How a person's hair is cut and how much of it there is.</summary>
public enum HairStyle
{
    /// <summary>Only a darker scalp; nothing sits on the head.</summary>
    Cropped,
    /// <summary>A thin cap of hair over the crown and down the back of the neck.</summary>
    Short,
    /// <summary>More hair, with some volume.</summary>
    Full,
    /// <summary>Dense, tightly curled hair with the most volume.</summary>
    Curly,
    /// <summary>Hair cut at the jaw, over the nape.</summary>
    Bob,
    /// <summary>Hair worn loose to the shoulders.</summary>
    Long,
    /// <summary>Hair pinned in a bun at the back of the head.</summary>
    Bun,
    /// <summary>Hair gathered in a tail at the back.</summary>
    Ponytail,
    /// <summary>Hair in one plait down the back.</summary>
    Braid,
}

/// <summary>
/// The look of a people: skin tone, hair colour and cut, and small differences in the shape of the face (every factor is a scale, 1 = the
/// default head, and stays within a few percent: people differ by far more in tone and hair than in outline). It is applied to the body
/// only, so every uniform, animation and piece of gear fits all of them. <see cref="Name"/> is shown in tools.
/// </summary>
public sealed record Ancestry(string Key, string Name, Vector3 Skin, Vector3 Hair, HairStyle Style = HairStyle.Short,
    float Cheek = 1f, float Jaw = 1f, float Face = 1f, float Forehead = 1f, float Nose = 1f, float NoseWidth = 1f)
{
    /// <summary>A woman of this people: the same rig and head, a different build and cut of hair.</summary>
    public bool Female { get; init; }

    /// <summary>The unchanged default body (what the engine drew before there were ancestries).</summary>
    public static readonly Ancestry Default = new("default", "Default", new Vector3(0.87f, 0.68f, 0.56f), new Vector3(0.52f, 0.37f, 0.28f), HairStyle.Cropped);
}

/// <summary>
/// The looks by culture group (<c>common/cultures/*.txt</c>), with a few refinements for single cultures. A game or mod adds or changes one with
/// <see cref="Set"/> before the figures are first used; a key is a culture (checked first) or a culture group. Soldiers of a country come from the
/// groups of its people: see <see cref="Pick"/> and <see cref="FigureSystem.SoldierFor"/>.
/// </summary>
public static class Ancestries
{
    private static Vector3 V(float r, float g, float b) => new(r, g, b);
    private static readonly Dictionary<string, Ancestry> _byKey = new(StringComparer.OrdinalIgnoreCase);

    static Ancestries()
    {
        void Add(string key, string name, Vector3 skin, Vector3 hair, HairStyle style = HairStyle.Short, float cheek = 1f, float jaw = 1f, float face = 1f, float forehead = 1f, float nose = 1f, float noseWidth = 1f)
            => _byKey[key] = new Ancestry(key, name, skin, hair, style, cheek, jaw, face, forehead, nose, noseWidth);
        // Europe
        Add("germanic", "Germanic", V(0.89f, 0.70f, 0.60f), V(0.42f, 0.32f, 0.20f));
        Add("anglo", "Anglo", V(0.89f, 0.69f, 0.58f), V(0.33f, 0.23f, 0.15f));
        Add("nordic", "Nordic", V(0.91f, 0.73f, 0.63f), V(0.62f, 0.50f, 0.30f), cheek: 1.02f);
        Add("gallic", "Gallic", V(0.87f, 0.69f, 0.57f), V(0.25f, 0.17f, 0.12f));
        Add("italic", "Italic", V(0.83f, 0.64f, 0.50f), V(0.16f, 0.12f, 0.09f), HairStyle.Full);
        Add("iberian", "Iberian", V(0.82f, 0.63f, 0.48f), V(0.15f, 0.11f, 0.08f), HairStyle.Full);
        Add("romanian", "Romanian", V(0.82f, 0.64f, 0.51f), V(0.20f, 0.14f, 0.10f));
        Add("west_slavic", "West Slavic", V(0.88f, 0.70f, 0.58f), V(0.38f, 0.28f, 0.18f), cheek: 1.02f);
        Add("east_slavic", "East Slavic", V(0.88f, 0.70f, 0.58f), V(0.40f, 0.30f, 0.20f), cheek: 1.03f, face: 0.99f);
        Add("south_slavic", "South Slavic", V(0.84f, 0.66f, 0.52f), V(0.22f, 0.16f, 0.11f));
        Add("baltic", "Baltic", V(0.90f, 0.72f, 0.62f), V(0.55f, 0.44f, 0.28f));
        Add("finno_ugric", "Finno-Ugric", V(0.89f, 0.71f, 0.60f), V(0.50f, 0.38f, 0.24f), cheek: 1.03f);
        Add("hellenic", "Hellenic", V(0.81f, 0.62f, 0.48f), V(0.14f, 0.11f, 0.08f), HairStyle.Full);
        Add("albanian", "Albanian", V(0.83f, 0.65f, 0.51f), V(0.20f, 0.15f, 0.10f));
        Add("jewish", "Jewish", V(0.83f, 0.65f, 0.51f), V(0.17f, 0.12f, 0.09f));
        // west and central Asia
        Add("turkic", "Turkic", V(0.80f, 0.62f, 0.47f), V(0.18f, 0.13f, 0.09f), cheek: 1.02f);
        Add("iranian", "Iranian", V(0.78f, 0.59f, 0.44f), V(0.12f, 0.09f, 0.07f), HairStyle.Full);
        Add("caucasian", "Caucasian", V(0.82f, 0.64f, 0.50f), V(0.15f, 0.11f, 0.08f), HairStyle.Full);
        Add("arabic", "Arabic", V(0.76f, 0.57f, 0.41f), V(0.11f, 0.08f, 0.06f));
        // south, east and south-east Asia
        Add("indo_aryan", "Indo-Aryan", V(0.68f, 0.48f, 0.33f), V(0.10f, 0.08f, 0.07f));
        Add("dravidian", "Dravidian", V(0.55f, 0.38f, 0.26f), V(0.09f, 0.07f, 0.06f));
        Add("east_asian", "East Asian", V(0.85f, 0.68f, 0.50f), V(0.09f, 0.08f, 0.08f), HairStyle.Cropped, cheek: 1.04f, face: 1.02f, nose: 0.97f);
        Add("tibetan", "Tibeto-Mongol", V(0.76f, 0.56f, 0.40f), V(0.09f, 0.08f, 0.08f), cheek: 1.04f, face: 1.02f);
        Add("southeast_asian", "Southeast Asian", V(0.74f, 0.54f, 0.38f), V(0.09f, 0.08f, 0.07f), HairStyle.Cropped, cheek: 1.02f);
        Add("malay", "Malayo-Polynesian", V(0.72f, 0.52f, 0.36f), V(0.09f, 0.08f, 0.07f), cheek: 1.02f);
        // the Americas
        Add("american", "American", V(0.88f, 0.69f, 0.58f), V(0.36f, 0.26f, 0.16f));
        Add("native_american", "Native American", V(0.72f, 0.52f, 0.38f), V(0.09f, 0.08f, 0.08f), HairStyle.Full, cheek: 1.03f);
        Add("latin_american", "Latin American", V(0.78f, 0.58f, 0.43f), V(0.18f, 0.13f, 0.09f));
        Add("african_diaspora", "African diaspora", V(0.46f, 0.31f, 0.22f), V(0.07f, 0.06f, 0.06f), HairStyle.Curly);
        // Africa and Oceania
        Add("west_african", "West African", V(0.38f, 0.25f, 0.18f), V(0.07f, 0.06f, 0.06f), HairStyle.Curly);
        Add("bantu", "Bantu", V(0.40f, 0.27f, 0.19f), V(0.07f, 0.06f, 0.06f), HairStyle.Curly);
        Add("horn", "Horn of Africa", V(0.50f, 0.34f, 0.24f), V(0.08f, 0.07f, 0.06f), HairStyle.Curly);
        Add("nilotic", "Nilotic", V(0.30f, 0.20f, 0.15f), V(0.06f, 0.05f, 0.05f), HairStyle.Cropped);
        Add("oceanian", "Oceanian", V(0.52f, 0.36f, 0.26f), V(0.08f, 0.07f, 0.06f), HairStyle.Curly);
    }

    /// <summary>The keys that have a look (cultures and culture groups).</summary>
    public static IEnumerable<string> Keys => _byKey.Keys;

    /// <summary>Adds or replaces the look of a culture or culture group.</summary>
    public static void Set(string key, Ancestry look) => _byKey[key] = look with { Key = key };

    /// <summary>
    /// A look from a variant spec: <c>people[.f][.style]</c> (for example <c>nilotic.f.bun</c>). <c>.f</c> makes a woman, a hair style name overrides the people's own.
    /// </summary>
    public static Ancestry? Parse(string spec)
    {
        var parts = spec.Split('.');
        var look = Find(parts[0]) ?? (parts[0].Equals("default", StringComparison.OrdinalIgnoreCase) ? Ancestry.Default : null);
        if (look == null) return null;
        bool female = false; var style = look.Style;
        foreach (string p in parts.Skip(1))
            if (p.Equals("f", StringComparison.OrdinalIgnoreCase)) female = true;
            else if (Enum.TryParse<HairStyle>(p, true, out var st)) style = st;
            else return null;
        return look with { Key = spec, Female = female, Style = style };
    }

    /// <summary>Hair styles worn by men and by women of a people (the first is the people's own).</summary>
    public static HairStyle[] StylesOf(Ancestry look, bool female) => female
        ? (look.Style == HairStyle.Curly ? new[] { HairStyle.Curly, HairStyle.Bun, HairStyle.Braid, HairStyle.Ponytail, HairStyle.Bob }
            : new[] { HairStyle.Bob, HairStyle.Long, HairStyle.Bun, HairStyle.Ponytail, HairStyle.Braid })
        : (look.Style == HairStyle.Curly ? new[] { HairStyle.Curly, HairStyle.Short, HairStyle.Cropped }
            : new[] { look.Style, HairStyle.Short, HairStyle.Cropped, HairStyle.Full });

    /// <summary>The spec of one person of a people: sex and hair style chosen by <paramref name="seed"/>.</summary>
    public static string Person(string people, bool female, int seed)
    {
        var look = Find(people) ?? Ancestry.Default;
        var styles = StylesOf(look, female);
        uint h = (uint)seed * 2246822519u + 374761393u; h ^= h >> 15; h *= 2654435761u; h ^= h >> 13;
        // the first style is the most common, the others share the rest
        int i = h % 100 < 45 ? 0 : 1 + (int)(h / 100 % (uint)(styles.Length - 1));
        return people + (female ? ".f" : "") + "." + styles[i].ToString().ToLowerInvariant();
    }

    /// <summary>The look of a culture (or of a culture group), or null.</summary>
    public static Ancestry? Find(string? key) => key != null && _byKey.TryGetValue(key, out var a) ? a : null;

    /// <summary>
    /// The key a soldier of <paramref name="culture"/> (in <paramref name="group"/>) is drawn with: the culture's own look, else the group's, else null (the
    /// default body). A uniform's <see cref="Uniform.Remap"/> says a country dresses a people with another look.
    /// </summary>
    public static string? KeyFor(string? culture, string? group, Uniform? uniform = null)
    {
        if (uniform?.Remap is { } map)
        {
            if (culture != null && map.TryGetValue(culture, out var rc) && _byKey.ContainsKey(rc)) return rc;
            if (group != null && map.TryGetValue(group, out var rg) && _byKey.ContainsKey(rg)) return rg;
        }
        if (culture != null && _byKey.ContainsKey(culture)) return culture;
        return group != null && _byKey.ContainsKey(group) ? group : null;
    }

    /// <summary>The peoples whose soldiers a country historically fielded in this period (for contact sheets and as a starting point for mods).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Rosters = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ENG"] = new[] { "anglo", "gallic", "indo_aryan", "west_african", "african_diaspora", "malay" },
        ["USA"] = new[] { "american", "african_diaspora", "latin_american", "native_american", "germanic", "italic" },
        ["GER"] = new[] { "germanic", "west_slavic", "baltic" },
        ["FRA"] = new[] { "gallic", "arabic", "west_african", "southeast_asian", "italic" },
        ["SOV"] = new[] { "east_slavic", "turkic", "caucasian", "finno_ugric", "baltic", "east_asian" },
        ["CHI"] = new[] { "east_asian", "tibetan", "turkic" },
        ["JAP"] = new[] { "east_asian" },
        ["BRA"] = new[] { "latin_american", "african_diaspora", "italic", "germanic" },
        ["MEX"] = new[] { "latin_american", "native_american", "iberian" },
    };

    /// <summary>One of the weighted keys, chosen by <paramref name="seed"/> (the same seed always gives the same answer); null when nothing is weighted.</summary>
    public static string? Pick(IReadOnlyList<(string Key, double Weight)> mix, int seed)
    {
        double total = mix.Sum(m => m.Weight);
        if (mix.Count == 0 || total <= 0) return null;
        uint h = (uint)seed * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        double r = h / (double)uint.MaxValue * total;
        foreach (var (key, w) in mix)
            if ((r -= w) <= 0) return key;
        return mix[^1].Key;
    }
}
