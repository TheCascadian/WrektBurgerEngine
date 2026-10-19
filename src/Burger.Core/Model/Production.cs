using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>
/// Equipment from <c>common/equipment/*.txt</c> (production feature): rifles, artillery pieces, tanks, ships' guns...
/// Factories produce it into a country's stockpile; training and reinforcing divisions use it.
/// </summary>
public sealed class EquipmentType
{
    public string Key = "";
    public string Name = "";
    /// <summary>Production cost of one unit (factory output per day is compared against it).</summary>
    public double Cost = 1;
    public string Category = "army";
    /// <summary>Unit categories (infantry, support...) that use this equipment when a unit type lists none.</summary>
    public List<string> DefaultFor = new();
    /// <summary>How many a unit of those categories needs by default.</summary>
    public double DefaultAmount = 100;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");
}

/// <summary>
/// A state building from <c>common/buildings/*.txt</c>: factories, dockyards, infrastructure, railways, supply hubs,
/// air bases... Levels are stored per state (<see cref="State.Buildings"/>).
/// </summary>
public sealed class BuildingType
{
    public string Key = "";
    public string Name = "";
    public string Description = "";
    /// <summary>Construction cost of one level (civilian factories add construction points per day).</summary>
    public double Cost = 5000;
    /// <summary>Construction method key. Empty uses the era-aware default selected by production rules.</summary>
    public string Method = "";
    /// <summary>Technology keys required before this building can be constructed.</summary>
    public List<string> RequiresTech = new();
    /// <summary>Relative workforce required while this building is under construction.</summary>
    public double LabourRequired;
    /// <summary>Industrial inputs required by one construction project. Consumption is handled by the economy layer.</summary>
    public Dictionary<string, double> ConstructionInputs = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional method-specific capacity contribution from this building definition.</summary>
    public double CapacityOutput;
    public int MaxLevel = 10;
    /// <summary>factory, infrastructure, military... (free text, used by the interface to group buildings).</summary>
    public string Category = "other";
    public int StartYear = int.MinValue;
    public int EndYear = int.MaxValue;
    /// <summary>True if levels are shared with other buildings of the same category in a state (HOI4 building slots).</summary>
    public bool SharesSlots;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node? Potential;
    public Node Definition = Node.Root("");

    public bool Active(GameDate date) => date.Year >= StartYear && date.Year <= EndYear;
}

/// <summary>
/// A named construction method. The representation is data-oriented so scenario definitions can provide method metadata
/// through a building's definition without changing the modern civilian-factory path.
/// </summary>
public sealed class ConstructionMethod
{
    public string Key = "";
    /// <summary>manual, public_works, workshop or civilian_factory.</summary>
    public string Source = "manual";
    public double CapacityOutput = 1;
    public double LabourPerThousand = 0.05;
    public double WorkshopCapacity = 3;
    public int StartYear = int.MinValue;
    public int EndYear = int.MaxValue;
    public List<string> RequiresTech = new();
    public Dictionary<string, double> Inputs = new(StringComparer.OrdinalIgnoreCase);

    public bool Active(GameDate date) => date.Year >= StartYear && date.Year <= EndYear;
}

/// <summary>Factories assigned to producing one kind of equipment.</summary>
public sealed class ProductionLine
{
    public EquipmentType Equipment = null!;
    /// <summary>Military factories (or dockyards for naval equipment) the player assigned.</summary>
    public int Factories = 1;
    /// <summary>0-1: grows while the line keeps producing the same thing.</summary>
    public double Efficiency = 0.1;
    /// <summary>Production points carried over to the next unit.</summary>
    public double Progress;
    /// <summary>Units produced yesterday (for the interface).</summary>
    public double LastOutput;
}

/// <summary>A building being constructed by an era-appropriate construction method.</summary>
public sealed class ConstructionProject
{
    public BuildingType Building = null!;
    public State State = null!;
    public double Progress;
    /// <summary>Civilian factories working on it yesterday (for the interface).</summary>
    public int Factories;
    /// <summary>Resolved construction method key used by the current project.</summary>
    public string Method = "";
    /// <summary>Workforce allocated to this project during the last construction tick.</summary>
    public double LabourAllocated;
    /// <summary>Construction capacity applied during the last construction tick.</summary>
    public double Capacity;
    /// <summary>Inputs consumed by this project. The economy input-consumption layer fills this later.</summary>
    public Dictionary<string, double> InputsConsumed = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Why the project made no progress during the last construction tick.</summary>
    public string? BlockedReason;
}
