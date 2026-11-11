using Burger.Core.Model;
using Burger.Core.Politics;
using Burger.Core.Population;

namespace Burger.Core.Diplomacy;

public sealed partial class DiplomacySystem
{
    private DiplomacyRules? _batchRulesChecked;
    private bool _batchRulesPlain;

    /// <summary>True when the rules work out base opinion as <see cref="DiplomacyRules"/> does, so an <see cref="OpinionBatch"/> gives what <see cref="Opinion"/> gives.</summary>
    internal bool CanBatchOpinions
    {
        get
        {
            var rules = Rules;
            if (!ReferenceEquals(rules, _batchRulesChecked))
            {
                _batchRulesChecked = rules;
                _batchRulesPlain = rules.GetType().GetMethod(nameof(DiplomacyRules.BaseOpinion), new[] { typeof(Game), typeof(Country), typeof(Country) })!.DeclaringType == typeof(DiplomacyRules);
            }
            return _batchRulesPlain;
        }
    }

    /// <summary>
    /// The opinions many countries have of one, exactly as <see cref="Opinion"/> works them out one by one, with what depends on one side only looked up once.
    /// Good while the game does not change: <see cref="Begin"/> again after it may have.
    /// </summary>
    internal sealed class OpinionBatch
    {
        private readonly Game _game;
        internal OpinionBatch(Game game) => _game = game;

        private readonly List<Country> _from = new();
        private Country?[] _overlord = Array.Empty<Country?>();
        private string?[] _culture = Array.Empty<string?>();
        private IdeologyDef?[] _ideology = Array.Empty<IdeologyDef?>();
        private ReligionDef?[] _religion = Array.Empty<ReligionDef?>();
        private int[] _colonies = Array.Empty<int>();
        private int[] _enemyStamp = Array.Empty<int>();
        private int _enemyNow;
        private bool _population, _politics, _religionOn, _independence;

        /// <summary>Of the last <see cref="Row"/>: the two are allied, or one is the other's overlord (as <see cref="Relations.AreAllied"/> and <see cref="Relations.AreOverlordAndSubject"/>).</summary>
        public bool[] Allied = Array.Empty<bool>(), OverlordOrSubject = Array.Empty<bool>();

        public void Begin(List<Country> froms)
        {
            var g = _game;
            var r = g.World.Relations;
            _population = g.Population.Enabled;
            _politics = g.Politics.Enabled;
            _religionOn = g.Population.ReligionEnabled;
            _independence = g.Population.IndependenceEnabled;
            _from.Clear();
            _from.AddRange(froms);
            int n = froms.Count;
            if (_overlord.Length < n)
            {
                int size = Math.Max(n, _overlord.Length * 2);
                _overlord = new Country?[size]; _culture = new string?[size]; _ideology = new IdeologyDef?[size]; _religion = new ReligionDef?[size]; _colonies = new int[size];
                Allied = new bool[size]; OverlordOrSubject = new bool[size];
            }
            for (int i = 0; i < n; i++)
            {
                var c = froms[i];
                _overlord[i] = r.OverlordOf(c);
                _culture[i] = _population ? g.Population.Of(c).Culture : null;
                _ideology[i] = _politics ? g.Politics.Ideology(g.Politics.Of(c).Ruling) : null;
                _religion[i] = _religionOn ? g.Population.Religion(g.Population.Of(c).Religion) : null;
                _colonies[i] = _independence ? g.Population.ColonyCount(c) : 0;
            }
        }

        /// <summary>Opinion of <paramref name="to"/> of each country given to <see cref="Begin"/>, in its order.</summary>
        public void Row(Country to, double[] into)
        {
            var g = _game;
            var r = g.World.Relations;
            var date = g.World.Date;
            var popRules = g.Rules.Population;
            Country? overlordTo = r.OverlordOf(to);
            double opinionModifiers = g.Modifiers.Sum(to, "opinion");
            bool scriptedBase = g.ScriptValues.Has("base_opinion");

            // countries at war with "to" (as World.AtWar)
            if (++_enemyNow == int.MaxValue) { Array.Clear(_enemyStamp); _enemyNow = 1; }
            foreach (var w in g.World.Wars)
            {
                if (w.Armistice) continue;
                if (w.IsAttacker(to)) foreach (var c in w.Defenders) MarkEnemy(c);
                if (w.IsDefender(to)) foreach (var c in w.Attackers) MarkEnemy(c);
            }

            // (what of "to" only some pairs need is looked up when the first of them needs it, as one by one)
            bool cultureToKnown = false, rulingToKnown = false, religionToKnown = false, coloniesToKnown = false, sameCultureKnown = false;
            string? cultureTo = null, rulingTo = null;
            ReligionDef? religionTo = null;
            int coloniesTo = 0;
            double sameCulture = 0;

            for (int i = 0; i < _from.Count; i++)
            {
                var from = _from[i];
                var overlordFrom = _overlord[i];
                bool allied = from != to && r.Alliances.Contains(Relations.Pair(from, to));
                bool overlordOrSubject = overlordFrom == to || overlordTo == from;
                Allied[i] = allied;
                OverlordOrSubject[i] = overlordOrSubject;
                if (from == to) { into[i] = 200; continue; }

                // DiplomacyRules.BaseOpinion
                double v = 0;
                if (allied) v += 25;
                if (overlordOrSubject) v += 10;
                if (r.IsGuaranteeing(to, from)) v += 15;
                if (from.Index < _enemyStamp.Length && _enemyStamp[from.Index] == _enemyNow) v -= 100;
                if (r.HasTruce(from, to, date)) v -= 10;
                if (_population && overlordFrom != to && overlordTo != from && _culture[i] is { } people)
                {
                    if (!cultureToKnown) { cultureTo = g.Population.Of(to).Culture; cultureToKnown = true; }
                    if (people == cultureTo)
                    {
                        if (!sameCultureKnown) { sameCulture = g.Data.Defines.Get("same_culture_opinion", -60); sameCultureKnown = true; }
                        v += sameCulture;
                    }
                }
                v += opinionModifiers;
                if (scriptedBase && g.ScriptValues.TryEval("base_opinion", from, out var scripted,
                        new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["target"] = to, ["recipient"] = to }))
                    v += scripted;

                // Politics.IdeologyOpinion, Population.ReligionOpinion and Population.ColonialOpinion
                double ideology = 0;
                if (_politics && _ideology[i] is { } a)
                {
                    if (!rulingToKnown) { rulingTo = g.Politics.Of(to).Ruling; rulingToKnown = true; }
                    ideology = a.Opinion.GetValueOrDefault(rulingTo!);
                }
                double religion = 0;
                if (_religionOn)
                {
                    if (!religionToKnown) { religionTo = g.Population.Religion(g.Population.Of(to).Religion); religionToKnown = true; }
                    religion = popRules.ReligionOpinion(g, _religion[i], religionTo);
                }
                double colonial = 0;
                if (_independence)
                {
                    if (!coloniesToKnown) { coloniesTo = g.Population.ColonyCount(to); coloniesToKnown = true; }
                    colonial = popRules.ColonialOpinion(g, from, to, _colonies[i], coloniesTo);
                }
                into[i] = Math.Clamp(v + r.ModifierSum(from, to) + ideology + religion + colonial, -200, 200);
            }
        }

        private void MarkEnemy(Country c)
        {
            if (c.Index >= _enemyStamp.Length) Array.Resize(ref _enemyStamp, Math.Max(c.Index + 1, _enemyStamp.Length * 2));
            _enemyStamp[c.Index] = _enemyNow;
        }
    }
}
