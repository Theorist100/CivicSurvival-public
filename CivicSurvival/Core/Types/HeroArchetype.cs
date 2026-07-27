namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Speaker choice for the deployed hero — one of three archetypes, each a hard-counter
    /// to one discrete PSYOPS attack <see cref="PsyOpsAttackType"/> (rock-paper-scissors):
    ///   Voice/Gerda  ← Rumor      (balanced −infection/+recovery; debuff: city-wide −happiness)
    ///   Arestovych   ← FakeVideo  (strong instant burst-tamer; debuff: accruing trust-debt)
    ///   Patriot      ← Propaganda (+resistance, no fatigue; debuff: raises exodus)
    ///
    /// Only ONE archetype is active at a time, so a mixed raid always has an off-type the
    /// current speaker is weak against. The hero's COUNTER effectiveness is by attack TYPE
    /// (<see cref="CivicSurvival.Core.Logic.StratumDefense"/>), NOT by household stratum;
    /// stratum-specialisation belongs to the tools (Buckwheat→poor, Telemarathon→middle).
    ///
    /// Voice is the default (= the original single "The Voice / Gerda" hero) so a save with
    /// no persisted archetype loads as Voice.
    /// </summary>
    public enum HeroArchetype : byte
    {
        /// <summary>The Voice / Gerda — balanced, best vs Rumor; debuff: city-wide −happiness.</summary>
        Voice = 0,

        /// <summary>Arestovych — strong instant cut, best vs FakeVideo; debuff: accruing trust-debt.</summary>
        Arestovych = 1,

        /// <summary>Patriot "to the bitter end" — +resistance, no fatigue, best vs Propaganda; debuff: +exodus.</summary>
        Patriot = 2
    }
}
