using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Cross-domain signal that a player's outbound counter-strike reached the frontier.
    /// The producer (<c>ThreatArrivalSystem</c>, ThreatDamage domain) appends one element
    /// per arrived outbound projectile, copying the axis/damage off its
    /// <see cref="OutboundStrikePayload"/>; the consumer (<c>EnemyOperationEffectSystem</c>,
    /// GridWarfare domain, ModificationEnd) drains the buffer, rolls the enemy's
    /// <c>InterceptChance</c>, and lowers the targeted enemy axis (<c>ReduceAxis</c>).
    ///
    /// This is the deferred half of the launch→arrival split: the launch commit spends the
    /// resource and fires the projectile; the axis effect lands here, at arrival, and may be
    /// zero (the enemy intercepted it). It lives in <c>Core</c> (Axiom 5) so the ThreatDamage
    /// producer and the GridWarfare consumer share one truth without a domain→domain import.
    ///
    /// Pause-safe: the producer runs in a 16-tick GameSimulation system (does not tick while
    /// paused, so nothing is recorded in pause); the consumer (ModificationEnd, which DOES
    /// tick in pause) just sees an empty buffer and does nothing.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct OutboundArrivalSignal : IBufferElementData
    {
        /// <summary>Enemy axis the arrived strike targets (Kinetic→Physical, Cyber→Digital, Psyops→Social).</summary>
        public AttackCategory Axis;

        /// <summary>Axis reduction to apply, before the enemy's intercept roll.</summary>
        public float Damage;

        /// <summary>
        /// Launch-frozen intercept-roll seed, copied off the arriving projectile's
        /// <see cref="OutboundStrikePayload.Seed"/>. The consumer feeds it to
        /// <c>StrikeResolver.Resolve</c> so the intercept verdict is deterministic — identical after
        /// a mid-flight save/load (the seed survives on the serialized payload) and reproducible by a
        /// server given the same launch seed. Not serialized on this transient signal (the buffer
        /// holds ≤1 frame of arrivals); the durable copy lives on the payload, and the signal is
        /// rebuilt from it at arrival.
        /// </summary>
        public uint Seed;
    }

    /// <summary>
    /// Singleton host for the <see cref="OutboundArrivalSignal"/> buffer.
    ///
    /// Deliberately NOT serializable (mirror of <c>ThreatSpawnIntentHost</c>): this buffer is
    /// transient, holding at most one frame of in-flight arrivals, so dropping it on save/load
    /// is correct — it is rebuilt the next frame from the still-in-flight projectiles. The
    /// projectiles themselves DO survive a mid-flight save/load: the durable
    /// <see cref="OutboundStrikePayload"/> persists and <c>ThreatLoadRenderReinitSystem</c>
    /// re-enables the <see cref="PlayerOutboundThreat"/> faction bit from it on load. The
    /// consumer recreates the empty host in its lifecycle hooks.
    /// </summary>
    public struct OutboundArrivalSignalHost : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(OutboundArrivalSignalHost), new EnsureSingletonPolicy<OutboundArrivalSignalHost>
            {
                EnsureShape = EnsureBuffer
            });
        }

        private static void EnsureBuffer(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<OutboundArrivalSignal>(entity))
                em.AddBuffer<OutboundArrivalSignal>(entity);
        }
    }
}
