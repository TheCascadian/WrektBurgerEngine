using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>An ideology from <c>common/ideologies/*.txt</c> (democratic, communist, fascist, neutrality...).</summary>
public sealed class IdeologyDef
{
    public string Key = "";
    public string Name = "";
    /// <summary>Used for country names of rebels ("Nationalist Spain") and in texts.</summary>
    public string Adjective = "";
    /// <summary>Default party name (countries can name their own party: set_party_name, history).</summary>
    public string PartyName = "";
    public string Desc = "";
    public Rgb Color = new(160, 160, 160);
    /// <summary>Countries ruled by this ideology hold elections.</summary>
    public bool Elections;
    /// <summary>Gains support when stability is low or a war goes badly.</summary>
    public bool Extremist;
    /// <summary>Countries without a color of their own for this ideology are tinted towards this color (country_color_blend) by <see cref="ColorBlendAmount"/>.</summary>
    public Rgb? ColorBlend;
    public double ColorBlendAmount;
    /// <summary>Popularity it gains each month (points) in a weak democracy: elections, but low stability or a government with little support (weak_democracy_drift; fascists).</summary>
    public double WeakDemocracyDrift;
    /// <summary>The country has a council or assembly while this ideology rules, even without elections (parliament and parties are shown).</summary>
    public bool Assembly;
    /// <summary>When set, only the currents named here (and the ruling one) sit in the chamber while this ideology rules; the others are shut out and become movements. Empty = every current may sit.</summary>
    public HashSet<string> Chamber = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Seats of the chamber while this ideology rules (0 = the country's own number, define parliament_seats).</summary>
    public int ChamberSeats;
    /// <summary>The ideology exists only once it is unlocked (an unlock block): from a year, when any country knows a technology, or by script (unlock_ideology). No block = there from the start.</summary>
    public bool HasUnlock;
    public int? UnlockYear;
    public string? UnlockTech;
    /// <summary>Share of every country that follows it the day it appears.</summary>
    public double StartPopularity = 0.03;
    /// <summary>Winning wars, countries ruled by this ideology set up subjects (puppets) of the peoples they defeat instead of
    /// taking foreign land (peace_puppets; default: ideologies that hold elections, i.e. democracies).</summary>
    public bool PeacePuppets;
    /// <summary>Ideologies whose parties this one will form a coalition with (both must accept each other).</summary>
    public HashSet<string> Coalition = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Opinion a country ruled by this ideology has of countries ruled by each ideology.</summary>
    public Dictionary<string, double> Opinion = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Modifier values for countries ruled by this ideology.</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional government word set when this ideology takes power (e.g. socialist_republic).</summary>
    public string? Government;
    public int Order;
    public Node Definition = Node.Root("");
}

/// <summary>Starting politics for countries with a government word, from <c>common/governments/*.txt</c>.</summary>
public sealed class GovernmentDef
{
    public string Key = "";
    public string? Ruling;
    public Dictionary<string, double> Popularity = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Starting law per law group.</summary>
    public Dictionary<string, string> Laws = new(StringComparer.OrdinalIgnoreCase);
    public int? Seats;
}

/// <summary>A group of laws (conscription, economy...) of which a country has exactly one, from <c>common/laws/*.txt</c>.</summary>
public sealed class LawGroup
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Law countries start with when nothing else says so (first law if not set).</summary>
    public string Default = "";
    public List<LawDef> Laws = new();
    public int Order;
    /// <summary>The group is shown (and used) only while true (country scope), e.g. has_feature = population.</summary>
    public Node? Potential;
    /// <summary>The group does not exist before it is unlocked (an unlock block: year, technology, or script). Its default law stays in force and should do nothing.</summary>
    public bool HasUnlock;
    public int? UnlockYear;
    public string? UnlockTech;
}

public sealed class LawDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Optional law icon path, relative to the game folder (e.g. gfx/interface/politics/conscription.png).</summary>
    public string? Icon;
    public LawGroup Group = null!;
    /// <summary>Political power to enact it.</summary>
    public double Cost;
    /// <summary>Shown only while true (country scope).</summary>
    public Node? Potential;
    /// <summary>The law cannot be chosen before it is unlocked (an unlock block: year, technology, or script).</summary>
    public bool HasUnlock;
    public int? UnlockYear;
    public string? UnlockTech;
    /// <summary>Can be enacted only while true (country scope).</summary>
    public Node? Allow;
    public Node? AiWillDo;
    /// <summary>Effects run when the law is enacted (country scope).</summary>
    public Node? OnEnact;
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Parties of these ideologies may not win seats or govern.</summary>
    public HashSet<string> Bans = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>No elections while this law is in force.</summary>
    public bool SuspendsElections;
    /// <summary>One-party state: every party except the ruling one is banned and there are no elections.</summary>
    public bool SingleParty;
    public Node Definition = Node.Root("");
}

/// <summary>A cabinet post (cabinet feature), from <c>common/cabinet/*.txt</c>.</summary>
public sealed class CabinetPosition
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Character stat that makes a minister good at this post.</summary>
    public string Stat = "";
    /// <summary>Modifier values per point of that stat.</summary>
    public Dictionary<string, double> PerPoint = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Modifier values while the post is filled at all.</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    public double Cost;
    public int Order;
}

/// <summary>The political state of one country.</summary>
public sealed class CountryPolitics
{
    /// <summary>Ideology of the ruling party.</summary>
    public string Ruling = "";
    /// <summary>Ideologies of the parties in government (ruling party first, then coalition partners).</summary>
    public List<string> Government = new();
    /// <summary>Support of each ideology (fractions that add up to 1).</summary>
    public Dictionary<string, double> Popularity = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Seats in parliament by ideology (aggregated for legacy rules and government mechanics).</summary>
    public Dictionary<string, int> Seats = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Independent support by named party scope key (legacy ideology keys stand in when no party scope exists).</summary>
    public Dictionary<string, double> PartyPopularity = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Independent seats by party scope key, plus culture:/religion: keys for minority parties.</summary>
    public Dictionary<string, int> PartySeats = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Actual organization leading the government, while Ruling remains its ideology for legacy mechanics.</summary>
    public string RulingOrganization = "";
    /// <summary>Organizations represented in government; Government remains the distinct ideology list for legacy mechanics.</summary>
    public List<string> GovernmentOrganizations = new();
    public int TotalSeats = 100;
    public GameDate? LastElection;
    public GameDate? NextElection;
    /// <summary>Law in force per law group.</summary>
    public Dictionary<string, string> Laws = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Day each law group last changed (for the cooldown).</summary>
    public Dictionary<string, int> LawChangedDay = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Minister per cabinet post.</summary>
    public Dictionary<string, Character> Ministers = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Party names that replace the ideology's default party name.</summary>
    public Dictionary<string, string> PartyNames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Day number until which no coup or civil war can start (after one just happened).</summary>
    public int CalmUntilDay = int.MinValue;
}

/// <summary>Who decides the government of a subject (rule SubjectGovernment).</summary>
public enum SubjectControl { Imposed, Preferred, Free }

public enum MovementDemand
{
    /// <summary>Lift the ban on the ideology's party (change to <see cref="Movement.TargetLaw"/>).</summary>
    Legalize,
    /// <summary>Put the ideology in power.</summary>
    Power,
}

/// <summary>
/// A political movement (Victoria 3 style): supporters of an ideology that is shut out of power gather
/// strength until they revolt. Giving in to the demand dissolves it.
/// </summary>
public sealed class Movement
{
    public Country Country = null!;
    public string Ideology = "";
    public MovementDemand Demand;
    public string? TargetLaw;
    /// <summary>0-100. At 100 the movement revolts.</summary>
    public double Progress;
    public GameDate Started;
    /// <summary>The player has been told it is about to revolt (on_movement_ultimatum fired).</summary>
    public bool Warned;
    /// <summary>0-100. How much of the movement's support is organised (cells, unions, press, arms). Support alone never revolts.</summary>
    public double Organization;
    /// <summary>0-100. Persistent grievance of its supporters; drifts slowly toward what exclusion, hardship and past repression justify.</summary>
    public double Grievance;
    /// <summary>Memory of concessions made to it (decays slowly): they ease grievance but legitimise and embolden it.</summary>
    public double Concessions;
    /// <summary>Memory of crackdowns on it (decays slowly): they cut organisation now, and feed grievance and martyrs later.</summary>
    public double Crackdowns;
    /// <summary>Months in a row it had the strength to revolt but no opening (shown as a warning; not saved).</summary>
    public int Stalled;
    /// <summary>0-100. Recent violence and disorder (riots, bombings, mutinies); it eats stability and fades by itself.</summary>
    public double Heat;
    /// <summary>The interest groups that came out with the movement: what they want more of (freedom, security, a larger army). Not laws.</summary>
    public List<InterestGroup> Groups = new();
}

/// <summary>
/// A pressure group inside a movement. It spawns only with a movement and wants something the state can give or deny: more
/// <c>freedom</c>, more <c>security</c> or a larger <c>army</c>. Unmet, its strength grows and the movement turns aggressive.
/// </summary>
public sealed class InterestGroup
{
    public string Key = "";
    /// <summary>0-100. How much of the movement's push this group carries.</summary>
    public double Strength;
    /// <summary>0-1. How far the state currently gives it what it wants (drifts toward what the country is actually like).</summary>
    public double Satisfaction = 0.5;
}

/// <summary>A running civil war between a country and the rebels that split from it.</summary>
public sealed class CivilWar
{
    public War War = null!;
    public Country Origin = null!;
    public Country Rebel = null!;
    public string Ideology = "";
}
