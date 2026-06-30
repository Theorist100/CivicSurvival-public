using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Off-barrier spawn intent — one buffer element per threat the producer
    /// (<c>ThreatSpawnSystem</c>, GameSimulation) decided to spawn. The producer does all
    /// target selection / CEP / position / RNG work and records the fully-resolved spawn
    /// data here; the consumer (<c>ThreatSpawnApplySystem</c>, Modification4) does the actual
    /// <c>CreateEntity(drone/ballistic archetype)</c> + component writes from the render-safe
    /// phase, where vanilla's render batch pipeline expects the archetype migration.
    ///
    /// Why a buffer-on-singleton intent rather than the producer creating the drone itself:
    /// creating a drone/ballistic render entity migrates a chunk the render writer
    /// (<c>DroneRenderWriteJob</c>) iterates. Doing that from GameSimulation (where the render
    /// job is scheduled and not yet completed) lands the migration into a live render chunk →
    /// native AV (crash <c>9db2bedf</c>). The consumer runs in Modification4 of the next frame,
    /// after a render-completion gate (<c>RenderWriteBarrier.Consume</c>), so the render job is
    /// finished before any structural change — exactly the contract the deletion split
    /// (<c>PendingThreatDeletion</c> / <c>ThreatDeletionApplySystem</c>) already relies on.
    ///
    /// Not serialized (mirror of <c>PendingThreatDeletion</c>): the host singleton
    /// (<see cref="ThreatSpawnIntentHost"/>) carries no serializer, so the buffer is stripped
    /// on save/load. A save taken in the 1-frame window between producer (frame N) and consumer
    /// (frame N+1) loses the un-applied intent — but the wave is NOT lost: the
    /// <c>WaveExecutor</c> / <c>WaveStateSingleton</c> resume path re-issues the interrupted
    /// wave on load (and <see cref="ThreatsSpawnedEvent"/> is published by the consumer AFTER
    /// the real <c>CreateEntity</c>, never in the producer before the entity exists, so a save
    /// in the gap never records a wave as spawned while its entities do not exist).
    /// </summary>
    // Capacity covers a typical wave (~10-30 threats) without a heap allocation; the buffer lives
    // ≤1 frame (recorded by the producer in GameSimulation, drained+cleared by the consumer in the
    // next Modification4), so the inline storage is reused every wave.
    [InternalBufferCapacity(32)]
    public struct ThreatSpawnIntent : IBufferElementData
    {
        /// <summary>0 = Shahed (drone), 1 = Ballistic.</summary>
        public byte Kind;

        /// <summary>
        /// Projectile faction: 0 = <c>EnemyInbound</c> (wave threat on the city — default), 1 =
        /// <c>PlayerOutbound</c> (player counter-strike). The consumer
        /// (<c>ThreatSpawnApplySystem</c>) enables <see cref="PlayerOutboundThreat"/> on the
        /// spawned entity iff this is <see cref="FactionPlayerOutbound"/>; wave producers leave it
        /// 0 so inbound behavior is unchanged.
        /// </summary>
        public byte Faction;

        /// <summary>Faction value: enemy wave threat inbound on the city (default, bit disabled).</summary>
        public const byte FactionEnemyInbound = 0;

        /// <summary>Faction value: player counter-strike outbound (enables <see cref="PlayerOutboundThreat"/>).</summary>
        public const byte FactionPlayerOutbound = 1;

        /// <summary>
        /// Outbound only: which enemy axis the counter-strike lowers at arrival
        /// (Kinetic→Physical, Cyber→Digital, Psyops→Social). The consumer copies this and
        /// <see cref="OutboundDamage"/> onto the spawned projectile's
        /// <see cref="OutboundStrikePayload"/>. Ignored for inbound waves
        /// (<see cref="FactionEnemyInbound"/>).
        /// </summary>
        public AttackCategory OutboundAxis;

        /// <summary>
        /// Outbound only: axis reduction applied at arrival before the enemy's intercept roll.
        /// Recorded on the projectile's <see cref="OutboundStrikePayload"/> by the consumer.
        /// Ignored for inbound waves.
        /// </summary>
        public float OutboundDamage;

        /// <summary>
        /// Outbound only: the launch-frozen intercept-roll seed. Distinct from
        /// <see cref="PseudoSeed"/> (a render-jitter seed drawn per spawn from the producer's RNG) —
        /// this one is supplied by the launch caller (<c>EnemyOperationEffectSystem</c>), derived
        /// deterministically from the operation's stable identity + game time, and copied by the
        /// consumer onto <see cref="OutboundStrikePayload.Seed"/> so the arrival intercept verdict is
        /// reproducible (after load and on a server). Ignored for inbound waves (0).
        /// </summary>
        public uint OutboundSeed;

        /// <summary>World spawn position (terrain-adjusted at the map edge).</summary>
        public float3 SpawnPos;

        /// <summary>Initial facing rotation.</summary>
        public quaternion Rotation;

        /// <summary>Initial velocity (direction * speed).</summary>
        public float3 Velocity;

        /// <summary>Flight speed in m/s.</summary>
        public float Speed;

        /// <summary>Total distance spawn→target (Shahed only; 0 for ballistic).</summary>
        public float TotalDistance;

        /// <summary>Resolved target world position.</summary>
        public float3 TargetPos;

        /// <summary>Target building reference (Index — Axiom 11, no Entity fields).</summary>
        public int TargetBuildingIndex;

        /// <summary>Target building reference (Version).</summary>
        public int TargetBuildingVersion;

        /// <summary>Target category for AA prioritization (Shahed).</summary>
        public TargetCategory Category;

        /// <summary>Focus-cluster flag → <c>ShahedCombatState.IsFocusStrike</c> (Shahed).</summary>
        public bool IsFocusStrike;

        /// <summary>Blast radius in meters (Ballistic).</summary>
        public float ImpactRadius;

        /// <summary>Damage severity multiplier (Ballistic).</summary>
        public float DamageSeverity;

        /// <summary>
        /// PseudoRandomSeed value — the producer drew it from <c>m_Random</c>; the consumer
        /// never touches RNG. Range [1, ushort.MaxValue), so 0 is reserved for "unset".
        /// </summary>
        public ushort PseudoSeed;

        /// <summary><c>ThreatGenerationClock.Current</c> at producer time.</summary>
        public int ThreatGeneration;

        /// <summary>
        /// Prefab submesh count, computed by the producer (so the consumer never does a
        /// prefab <c>SubMesh</c> buffer lookup). The consumer appends this many white
        /// <c>MeshColor</c> entries.
        /// </summary>
        public byte SubMeshCount;

        /// <summary>Prefab entity Index → consumer rebuilds <c>new Entity{Index,Version}</c>.</summary>
        public int PrefabIndex;

        /// <summary>Prefab entity Version.</summary>
        public int PrefabVersion;

        /// <summary>
        /// <c>SystemAPI.Time.ElapsedTime</c> read by the producer in GameSimulation, used for
        /// <c>ThreatFlightProgress.MinDistanceTime</c>. Recorded in the intent so the watchdog
        /// baseline is the spawn-tick time, not the (different) Modification4 consumer-tick time.
        /// </summary>
        public double SpawnElapsedTime;

        /// <summary>Wave number this intent belongs to (for the <see cref="ThreatsSpawnedEvent"/>).</summary>
        public int WaveNumber;

        /// <summary>
        /// Per-<c>SpawnWave</c>-call monotonic batch key (distinct from <see cref="ThreatGeneration"/>,
        /// which is the world-generation stamp from <c>ThreatGenerationClock</c>). The consumer
        /// groups intents by this key, publishes exactly one <see cref="ThreatsSpawnedEvent"/> per
        /// group, and (idempotency safety) records the last applied key so the same group cannot be
        /// double-applied if a later sim tick of the same frame re-enters before the buffer clear
        /// flushes.
        /// </summary>
        public uint WaveBatchKey;
    }

    /// <summary>
    /// Singleton host for the <see cref="ThreatSpawnIntent"/> buffer.
    ///
    /// Deliberately NOT serializable (mirror of the deletion-signal canon): no
    /// <c>ISerializable</c>/<c>IEmptySerializable</c> on the host, so the buffer is stripped on
    /// save/load and a save in the producer→consumer gap re-issues the wave through the
    /// WaveExecutor resume path rather than re-applying a stale serialized intent (which would
    /// double-spawn). <c>EnsureExists</c> recreates the empty host in
    /// <c>OnStartRunning</c>/<c>OnLoadRestore</c>.
    /// </summary>
    public struct ThreatSpawnIntentHost : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(ThreatSpawnIntentHost), new EnsureSingletonPolicy<ThreatSpawnIntentHost>
            {
                EnsureShape = EnsureBuffer
            });
        }

        private static void EnsureBuffer(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<ThreatSpawnIntent>(entity))
                em.AddBuffer<ThreatSpawnIntent>(entity);
        }
    }
}
