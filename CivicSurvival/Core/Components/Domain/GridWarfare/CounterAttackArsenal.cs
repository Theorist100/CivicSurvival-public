using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// Kind of counter-attack munition held in the arsenal. Mirrors
    /// <c>ThreatSpawnIntent.Kind</c> (drone=0 / ballistic=1) so the launch phase
    /// (3.0.3) can map a player's outbound shot directly onto a stock bucket.
    /// </summary>
    public enum ArsenalKind : byte
    {
        /// <summary>Loitering drone (Shahed-class). Stock = <see cref="CounterAttackArsenal.DroneStock"/>.</summary>
        Drone = 0,

        /// <summary>Ballistic rocket (Fatah-class). Stock = <see cref="CounterAttackArsenal.BallisticStock"/>.</summary>
        Ballistic = 1
    }

    /// <summary>
    /// Maps a counter-strike's <see cref="AttackCategory"/> onto the munition kind it
    /// consumes / launches. One home for the mapping (used by the launch spend-gate and the
    /// outbound spawn): the kinetic drone-swarm flies drones; cyber and psyops carriers fly a
    /// ballistic. The exact distribution is a balance concern (Phase 3.0.6) — this is the
    /// single seam to retune it without touching call sites.
    /// </summary>
    public static class ArsenalKindMap
    {
        public static ArsenalKind ForCategory(AttackCategory category) => category switch
        {
            AttackCategory.Kinetic => ArsenalKind.Drone,
            AttackCategory.Cyber => ArsenalKind.Ballistic,
            AttackCategory.Psyops => ArsenalKind.Ballistic,
            _ => ArsenalKind.Drone
        };
    }

    /// <summary>
    /// Counter-attack arsenal singleton — the player's stock of outbound drones and
    /// ballistic rockets. One unit is spent per launch (Phase 3.0.3) and replenished
    /// through two channels (Phase 3.0.5): shadow import / donors (fast, paid) and
    /// hidden factories (strategic, production — Phase-30b). Lives in <c>Core</c>
    /// (Axiom 5) so the launch phase, the replenish pipeline, and a future hidden
    /// factory (all different domains) can read/write the same source of truth.
    ///
    /// Carries state → persisted via a keyed codec in the owner system
    /// (<c>CounterAttackArsenalSystem</c>), mirroring how <see cref="EnemyState"/> is
    /// serialized through <c>EnemyStateCodec</c>. NOT IEmptySerializable — that would
    /// drop the stock on load and the player would lose every purchased munition
    /// across a save/load (see memory empty_iserializable_save_crash).
    /// </summary>
    public struct CounterAttackArsenal : IComponentData
    {
        /// <summary>Outbound loitering drones in stock (never negative).</summary>
        public int DroneStock;

        /// <summary>Outbound ballistic rockets in stock (never negative).</summary>
        public int BallisticStock;

        /// <summary>Empty arsenal — a fresh game starts with nothing in reserve.</summary>
        public static CounterAttackArsenal Default => default;

        /// <summary>Stock for the given munition kind.</summary>
        public readonly int StockOf(ArsenalKind kind) =>
            kind == ArsenalKind.Ballistic ? BallisticStock : DroneStock;

        /// <summary>
        /// Domain-Driven Initialization: creates the singleton if it does not exist.
        /// Call from the owner system's OnCreate()/OnStartRunning()/OnLoadRestore().
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        /// <summary>Reset to domain defaults (empty arsenal).</summary>
        public void SetDefaults()
        {
            this = Default;
        }
    }
}
