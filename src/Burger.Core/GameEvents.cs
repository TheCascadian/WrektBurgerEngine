using Burger.Core.Population;
using Burger.Core.Model;

namespace Burger.Core;

/// <summary>
/// The events the game systems raise about their own changes, so the others can react without being called directly
/// (diplomacy raises <see cref="SubjectMade"/>, politics decides what a puppet's government should be). Subscribers are
/// added when the game is built; handlers run in the order they were added.
/// </summary>
public sealed class GameEvents
{
    /// <summary>A country became a subject (also raised for each subject that passed to a new overlord); the last value is the support its overlord's ideology gains.</summary>
    public event Action<Country, Country, double>? SubjectMade;
    /// <summary>Every month for each subject that stays one, after the checks for release and annexation.</summary>
    public event Action<Country, Country>? SubjectMonthly;
    /// <summary>A subject became independent (the second country was its overlord).</summary>
    public event Action<Country, Country?>? SubjectReleased;
    /// <summary>A country's ruling ideology or party changed (country, old ideology).</summary>
    public event Action<Country, string>? GovernmentChanged;
    /// <summary>A state changed owner (state, old owner, new owner).</summary>
    public event Action<State, Country?, Country>? StateTransferred;
    /// <summary>A state was divided at run time: the parent, the new child state, and the child's share (0-1) of the parent's land. Systems carry their per-state data over.</summary>
    public event Action<State, State, double>? StateSplit;
    /// <summary>A split is being undone (a save is loading): the parent and the child about to go.</summary>
    public event Action<State, State>? StateUnsplit;

    /// <summary>A war was declared (attacker, defender, war).</summary>
    public event Action<Country, Country, War>? WarDeclared;
    /// <summary>A non-aggression pact was broken (breaker, the other party).</summary>
    public event Action<Country, Country>? PactBroken;
    /// <summary>An ally answered a call to arms (ally, caller, joined the war).</summary>
    public event Action<Country, Country, bool>? CallAnswered;

    /// <summary>A country's ruler changed (the old one may be dead or deposed, or null).</summary>
    public event Action<Country, Character?, Character>? RulerChanged;
    /// <summary>A character died, with the reason.</summary>
    public event Action<Character, string>? CharacterDied;
    /// <summary>A minister was appointed (true) or dismissed (false).</summary>
    public event Action<Country, Character, bool>? MinisterChanged;
    /// <summary>The ruling party of a country changed: old and new party key and the reason ("coup", "election", ...).</summary>
    public event Action<Country, string, string, string>? RulingPartyChanged;
    /// <summary>An election was held; the last value is the party with the most seats.</summary>
    public event Action<Country, string>? ElectionHeld;
    /// <summary>A party was banned by a law (country, party key).</summary>
    public event Action<Country, string>? PartyBanned;
    /// <summary>An enemy took control of a location (location, old controller, new controller).</summary>
    public event Action<Location, Country, Country>? LocationOccupied;
    /// <summary>Some of a pop group took on the owner's culture.</summary>
    public event Action<State, Pop, Country>? PopAssimilated;

    public void RaiseRulerChanged(Country c, Character? old, Character now) => RulerChanged?.Invoke(c, old, now);
    public void RaiseCharacterDied(Character c, string reason) => CharacterDied?.Invoke(c, reason);
    public void RaiseMinisterChanged(Country c, Character m, bool appointed) => MinisterChanged?.Invoke(c, m, appointed);
    public void RaiseRulingPartyChanged(Country c, string old, string now, string reason) => RulingPartyChanged?.Invoke(c, old, now, reason);
    public void RaiseElectionHeld(Country c, string winner) => ElectionHeld?.Invoke(c, winner);
    public void RaisePartyBanned(Country c, string party) => PartyBanned?.Invoke(c, party);
    public void RaiseLocationOccupied(Location l, Country old, Country by) => LocationOccupied?.Invoke(l, old, by);
    public void RaisePopAssimilated(State s, Pop p, Country owner) => PopAssimilated?.Invoke(s, p, owner);

    public void RaiseWarDeclared(Country a, Country d, War w) => WarDeclared?.Invoke(a, d, w);
    public void RaisePactBroken(Country breaker, Country other) => PactBroken?.Invoke(breaker, other);
    public void RaiseCallAnswered(Country ally, Country caller, bool joined) => CallAnswered?.Invoke(ally, caller, joined);
    public void RaiseSubjectMade(Country subject, Country overlord, double popularity) => SubjectMade?.Invoke(subject, overlord, popularity);
    public void RaiseSubjectMonthly(Country subject, Country overlord) => SubjectMonthly?.Invoke(subject, overlord);
    public void RaiseSubjectReleased(Country subject, Country? overlord) => SubjectReleased?.Invoke(subject, overlord);
    public void RaiseGovernmentChanged(Country c, string old) => GovernmentChanged?.Invoke(c, old);
    public void RaiseStateSplit(State parent, State child, double share) => StateSplit?.Invoke(parent, child, share);
    public void RaiseStateUnsplit(State parent, State child) => StateUnsplit?.Invoke(parent, child);
    public void RaiseStateTransferred(State s, Country? old, Country now) => StateTransferred?.Invoke(s, old, now);
}
