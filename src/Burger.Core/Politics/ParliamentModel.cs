using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>What one pop group (culture, religion, class) in one state thinks of the government.</summary>
public sealed class PopMood
{
    /// <summary>-100 hates the government, +100 loves it.</summary>
    public double Opinion;
    /// <summary>0-100. Builds while opinion is negative.</summary>
    public double Grievance;
    /// <summary>0-100. Rises under a ban on the group's preferred party or lasting grievance; feeds extremist and banned parties.</summary>
    public double Radicalism;
    /// <summary>What this group wants: autonomy, religious_rights, labour_rights, property or stability.</summary>
    public string Objective = "stability";
    /// <summary>Approve, Comply, Disapprove, Protest or Radicalise.</summary>
    public string Reaction = "Comply";
    /// <summary>The party the group would vote for.</summary>
    public string PreferredParty = "";
}

/// <summary>Country-wide summary of the pop moods (recounted monthly, not saved).</summary>
public sealed class PopSummary
{
    public double AverageOpinion;
    /// <summary>Share (0-1) of people who protest or radicalise.</summary>
    public double UnrestShare;
    /// <summary>Per ideology: share (0-1) of people whose preferred party is banned, weighted by their radicalism.</summary>
    public Dictionary<string, double> Underground = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per ideology: share (0-1) of people whose preferred party has this ideology (banned or not).</summary>
    public Dictionary<string, double> Backing = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per ideology: size-weighted grievance (0-100) of the people who prefer it.</summary>
    public Dictionary<string, double> BackerGrievance = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per ideology: the state holding the largest part of its backers and that part's share (0-1). Concentrated support can secede or fight; scattered support cannot.</summary>
    public Dictionary<string, (string State, double Share)> Stronghold = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A person in the pool of a pop group who could be seated in parliament (not a <see cref="Character"/> until drawn).</summary>
public sealed class PoolCandidate
{
    public string Id = "";
    public bool Female;
    public int Age;
    public string FirstName = "", Family = "";
    public string Culture = "";
    public string? Religion, PopType;
    public string? StateKey;
    /// <summary>The party this person would stand for.</summary>
    public string Party = "";
    public string? Faction;
    public List<Trait> Traits = new();
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The light form of a generated seat holder: what a person needs to sit, age and be remembered. Culture, religion, class and home state are the seat's.</summary>
public sealed class SeatMember
{
    public string FirstName = "", Family = "";
    public bool Female;
    public GameDate BirthDate;
    /// <summary>How many separate times this person has sat in parliament.</summary>
    public int Times = 1;
    /// <summary>Base stats in the order of the game's character stats.</summary>
    public byte[] Stats = Array.Empty<byte>();
    public Trait[] Traits = Array.Empty<Trait>();

    public string DisplayName => string.IsNullOrEmpty(Family) ? Localisation.Get(FirstName) : $"{Localisation.Get(FirstName)} {Localisation.Get(Family)}";
    public int Age(GameDate now) => GameDate.YearsBetween(BirthDate, now);
}

/// <summary>One seat in a country's parliament.</summary>
public sealed class Seat
{
    public string Id = "";
    public string Party = "";
    /// <summary>Constituency (null when the game has no population feature).</summary>
    public State? State;
    /// <summary>The pop group the seat was won from: <c>culture|religion|class</c>.</summary>
    public string PopGroup = "";
    /// <summary>
    /// Who sits here, in one of two forms: a light record (<see cref="Member"/>, the usual case) or, once the game has asked for it, a full
    /// <see cref="Character"/> (see <c>PoliticsSystem.CharacterOf</c>); never both.
    /// </summary>
    public SeatMember? Member;
    public Character? Character;
    public bool Occupied => Member != null || Character != null;
    public string? HolderName => Character?.DisplayName ?? Member?.DisplayName;
    public int TimesSeated => Character?.ParliamentTimes ?? Member?.Times ?? 0;
    public string CandidateId = "";
    public string? Faction;
    public GameDate TermStart, TermEnd;
    /// <summary>Candidates drawn so far for this seat (seeds the next fallback candidate).</summary>
    public int Draws;

}

public sealed class CountryParliament
{
    public List<Seat> Seats = new();
    /// <summary>Which party seat totals and constituencies the seat list was built from.</summary>
    public string Signature = "";
    /// <summary>Counts elections; part of the seed of every candidate pool, so a new election gives new people.</summary>
    public int Epoch;
}
