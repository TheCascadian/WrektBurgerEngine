using System.Collections.Immutable;
using System.Diagnostics;
using Burger.Core.Model;

namespace Burger.Core.Simulation;

/// <summary>
/// What the panels show of one country, state, location or character: read from the game when a panel asks (with the world held) and kept for a moment, so a panel is never older than a quarter of a second
/// and nothing is made for the thousands of things no panel shows. Only for the thread that draws the interface.
/// </summary>
public sealed class LiveRows
{
    private const double Moment = 0.25, ListMoment = 1.0;

    private sealed class Kept<TKey, TRow> where TKey : notnull
    {
        private readonly Dictionary<TKey, (long At, TRow Row)> _rows = new();
        private readonly Func<TKey, TRow> _make;
        private readonly long _life;

        public Kept(double seconds, Func<TKey, TRow> make)
        {
            _life = (long)(seconds * Stopwatch.Frequency);
            _make = make;
        }

        public TRow Get(TKey key)
        {
            long now = Stopwatch.GetTimestamp();
            if (_rows.TryGetValue(key, out var kept) && now - kept.At < _life) return kept.Row;
            if (_rows.Count > 4096) _rows.Clear();
            var row = _make(key);
            _rows[key] = (now, row);
            return row;
        }
    }

    public Game Game { get; }

    private readonly Kept<Location, RenderLocation> _locations;
    private readonly Kept<State, RenderState> _states;
    private readonly Kept<Country, RenderCountry> _countries;
    private readonly Kept<Character, RenderCharacter> _characters;
    private readonly Kept<Country, Dictionary<int, RenderCharacter>> _charactersOf;
    private readonly Kept<Country, RenderPoliticsOverview> _politics;
    private readonly Kept<(Country From, Country To), RenderDiplomacyRelation> _relations;
    private readonly Kept<Country, List<RenderState>> _statesOf;
    private readonly Kept<int, List<RenderCountry>> _countryList;
    private readonly Kept<int, ImmutableArray<RenderDecision>> _decisions;
    private readonly Kept<int, (ImmutableArray<RenderFocus> Rows, string? CurrentKey, double CurrentProgress)> _focuses;
    private readonly Kept<int, ImmutableArray<RenderTechnology>> _technologies;

    public LiveRows(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;
        _locations = new(Moment, l => RenderSnapshotBuilder.LocationRow(game, l));
        _states = new(Moment, s => RenderSnapshotBuilder.StateRow(game, s));
        _countries = new(Moment, c => RenderSnapshotBuilder.CountryRow(game, c, RenderSnapshotBuilder.DivisionCounts(game), RenderSnapshotBuilder.WarCounts(game), RenderSnapshotBuilder.RelatedCountries(game)));
        _characters = new(Moment, ch => RenderSnapshotBuilder.CharacterRow(game, ch));
        _charactersOf = new(Moment, c =>
        {
            var rows = new Dictionary<int, RenderCharacter>();
            foreach (var ch in game.World.Characters)
                if (ch.Country == c) rows[ch.Id] = RenderSnapshotBuilder.CharacterRow(game, ch);
            return rows;
        });
        _politics = new(Moment, c => game.Politics.Enabled && c.Exists ? RenderSnapshotBuilder.PoliticsRow(game, c) : default);
        _relations = new(Moment, pair => RenderSnapshotBuilder.RelationRow(game, pair.From, pair.To));
        _statesOf = new(ListMoment, c => c.OwnedStates.Select(s => RenderSnapshotBuilder.StateRow(game, s)).ToList());
        _countryList = new(ListMoment, _ =>
        {
            var divisions = RenderSnapshotBuilder.DivisionCounts(game);
            var wars = RenderSnapshotBuilder.WarCounts(game);
            var related = RenderSnapshotBuilder.RelatedCountries(game);
            return game.World.Countries.Where(c => c.Exists).Select(c => RenderSnapshotBuilder.CountryRow(game, c, divisions, wars, related))
                .OrderByDescending(c => c.LocationCount).ToList();
        });
        _decisions = new(Moment, _ => RenderSnapshotBuilder.DecisionRows(game));
        _focuses = new(Moment, _ => RenderSnapshotBuilder.FocusRows(game));
        _technologies = new(Moment, _ => RenderSnapshotBuilder.TechnologyRows(game));
    }

    public RenderLocation LocationRow(Location l) => _locations.Get(l);
    public RenderState StateRow(State s) => _states.Get(s);
    public RenderCountry CountryRow(Country c) => _countries.Get(c);
    public RenderCharacter CharacterRow(Character ch) => _characters.Get(ch);
    /// <summary>The characters of a country by their id.</summary>
    public Dictionary<int, RenderCharacter> CharactersOf(Country c) => _charactersOf.Get(c);
    /// <summary>The politics of a country; an empty row (no CountryTag) if it has none.</summary>
    public RenderPoliticsOverview PoliticsRow(Country c) => _politics.Get(c);
    public RenderDiplomacyRelation RelationRow(Country from, Country to) => _relations.Get((from, to));
    public List<RenderState> StatesOf(Country c) => _statesOf.Get(c);
    /// <summary>All existing countries, the largest first.</summary>
    public List<RenderCountry> CountryList() => _countryList.Get(0);
    /// <summary>The decisions the player can see.</summary>
    public ImmutableArray<RenderDecision> DecisionRows() => _decisions.Get(0);
    /// <summary>The player's focus tree.</summary>
    public (ImmutableArray<RenderFocus> Rows, string? CurrentKey, double CurrentProgress) FocusRows() => _focuses.Get(0);
    /// <summary>The technology tree as the player knows it.</summary>
    public ImmutableArray<RenderTechnology> TechnologyRows() => _technologies.Get(0);
}
