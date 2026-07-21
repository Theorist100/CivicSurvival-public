using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// Tier of a mirror-city target — what/why: the three-plus-one shape of every axis in
    /// the mirror enemy model. Each axis (Physical/Digital/Social) is built from targets of
    /// these tiers, and the tier decides a target's life-cycle rules (repair, rebuild,
    /// permanent-kill, invulnerability). Serialized as a byte (stable wire), so the numeric
    /// values are part of the save contract — append new tiers, never renumber.
    /// </summary>
    public enum MirrorTargetTier : byte
    {
        /// <summary>
        /// Indestructible fallback (bunker / backup node / underground press). Its contribution
        /// is the axis <em>floor</em>: it can never be damaged, so the axis can never drop below it.
        /// </summary>
        Reserve = 0,

        /// <summary>
        /// The single key target per axis. Destroying it is permanent (<see cref="EnemyTarget.DestroyedForever"/>)
        /// and softly cuts the axis <em>cap</em> — the only irreversible, visible progress against the
        /// otherwise self-repairing enemy.
        /// </summary>
        Key = 1,

        /// <summary>
        /// Ordinary target: destructible and self-rebuilding. Once destroyed it re-appears as a
        /// construction site (<see cref="EnemyTarget.RebuildProgress"/> ramping 0→1) whose partial
        /// contribution grows back with the build. The bulk of an axis lives here.
        /// </summary>
        Regular = 2,

        /// <summary>
        /// Enemy air-defence site (physical only, zero axis contribution). Positional: it raises the
        /// intercept chance of strikes flying over the targets it covers (<see cref="EnemyTarget.AaRange"/>).
        /// Can be suppressed (SEAD) but always rebuilds, so the late game is never a free sky.
        /// </summary>
        AaSite = 3
    }

    /// <summary>
    /// One target on the mirror enemy's city map — the authoritative, persisted unit the three
    /// stability axes are <em>derived</em> from (design decision 1: targets persisted, axes a
    /// derived cache; same persisted/derived split as Axiom 14). Lives in a
    /// <c>DynamicBuffer&lt;EnemyTarget&gt;</c> on the <see cref="EnemyState"/> singleton entity;
    /// <c>Domains.GridWarfare.Systems.MirrorCitySystem</c> owns the buffer and recomputes the axes
    /// from it. Consumers of the axes never read this type — the axes stay the public contract.
    ///
    /// Blittable over byte-enums / <c>ushort</c> / <c>float</c> / <c>bool</c> (buffer element must be
    /// unmanaged), so <c>Core.Logic.MirrorCityMath</c> can fold a <c>NativeArray</c> of these into
    /// axis values as a pure, Burst-compatible function.
    /// </summary>
    [InternalBufferCapacity(32)]
    public struct EnemyTarget : IBufferElementData
    {
        /// <summary>
        /// Sentinel target id meaning "no target selected / resolve automatically" (phase C). A strike
        /// carrying this id (or one that no longer resolves to a live target) is routed by the
        /// deterministic axis fallback in <c>MirrorCitySystem.ApplyStrikeToTarget</c>. <see cref="Id"/>
        /// is a real buffer index and can never legitimately equal this value.
        /// </summary>
        public const ushort NoTargetId = ushort.MaxValue;

        /// <summary>Stable index of this target within its city (assigned = buffer index at generation). Referenced by a strike's TargetId (phase C).</summary>
        public ushort Id;

        /// <summary>Which axis this target feeds (Kinetic→Physical, Cyber→Digital, Psyops→Social). AA sites are Kinetic/physical with zero contribution.</summary>
        public AttackCategory Axis;

        /// <summary>Life-cycle tier — decides repair/rebuild/permanent-kill/invulnerability (see <see cref="MirrorTargetTier"/>).</summary>
        public MirrorTargetTier Tier;

        /// <summary>Map X position (radar/world plane) — used for AA coverage geometry, not for the axis fold.</summary>
        public float X;

        /// <summary>Map Z position (radar/world plane) — used for AA coverage geometry, not for the axis fold.</summary>
        public float Z;

        /// <summary>
        /// Full axis contribution of this target when operational. The live sum of contributions
        /// (scaled by <see cref="RebuildProgress"/> for construction sites) is the axis value; the
        /// reserve target's contribution is the axis floor. AA sites carry 0.
        /// </summary>
        public float Contrib;

        /// <summary>Current hit points. <c>&lt;= 0</c> means destroyed (Regular → begins rebuilding; Key → gone if <see cref="DestroyedForever"/>).</summary>
        public float Hp;

        /// <summary>Full hit points at generation. Repair and rebuild restore <see cref="Hp"/> toward this.</summary>
        public float MaxHp;

        /// <summary>
        /// Rebuild completeness in <c>[0,1]</c>. <c>1</c> = fully built (operational). <c>&lt;1</c> = a
        /// construction site ("REBUILDING N%"): its contribution scales by this value and its hit
        /// points sit at a reduced construction level until the build finishes.
        /// </summary>
        public float RebuildProgress;

        /// <summary>
        /// Game-time hour at which a destroyed <see cref="MirrorTargetTier.Regular"/> target begins
        /// re-constructing (design decision 4's rebuild delay). Set to <c>now + RebuildDelayHours</c> when
        /// the target is destroyed; the repair tick holds the construction site at zero progress until game
        /// time reaches this mark, then ramps <see cref="RebuildProgress"/> 0→1 and clears this back to 0.
        /// <c>0</c> means "no rebuild scheduled" — an operational target, a permanently-killed key target,
        /// or an AA site (which repairs immediately without a delay). Absolute game-hour, stable across
        /// save/load (unlike <c>ElapsedTime</c>).
        /// </summary>
        public float RebuildAtHours;

        /// <summary>Air-defence coverage radius (world units). Only meaningful for <see cref="MirrorTargetTier.AaSite"/>; 0 for everything else.</summary>
        public float AaRange;

        /// <summary>
        /// Permanent-kill latch for a <see cref="MirrorTargetTier.Key"/> target: once true it never
        /// repairs or rebuilds, and its axis cap-cut stays applied. Always false for other tiers
        /// (they self-repair).
        /// </summary>
        public bool DestroyedForever;
    }

    /// <summary>
    /// Mirror-city singleton — what/why: the per-city header for the generated mirror enemy.
    /// Co-located on the <see cref="EnemyState"/> singleton entity alongside the
    /// <c>DynamicBuffer&lt;EnemyTarget&gt;</c> so the whole city (header + targets) persists as one
    /// serialization blob (Phase A: "one blob serialization" criterion). Owned/written by
    /// <c>Domains.GridWarfare.Systems.MirrorCitySystem</c>.
    ///
    /// Carries only what the derived axis fold needs on top of the target list: which catalogue
    /// variant produced this city, the generator format version (genVersion policy — a mismatch
    /// regenerates rather than migrates, per pre-release house policy), whether generation has run,
    /// and the accumulated per-axis cap cuts from destroyed key targets.
    /// </summary>
    public struct MirrorCityState : IComponentData
    {
        /// <summary>
        /// Current generator format version. Written onto <see cref="GenVersion"/> at generation.
        /// DRAFT: a persisted <see cref="GenVersion"/> that does not match this forces a regenerate
        /// (pre-release "bump = reset to defaults" policy — no legacy generators, no damage carry-over).
        /// Bumped to 2 in phase C when <c>EnemyTarget.RebuildAtHours</c> was added: pre-phase-C cities
        /// (GenVersion 1) simply regenerate on load rather than migrating the new field.
        /// Bumped to 3 when the <c>yeavering</c> baked map entered the catalog's MapPool: the
        /// round-robin re-maps every variant onto a different map, so persisted cities regenerate
        /// instead of drawing another map's contour under their targets.
        /// Bumped to 4 when the MapPool went from placeholders to the real baked map set: the
        /// <c>synthetic_test_island</c> pipeline-test tile (all land, one square lake) left the pool
        /// and 11 baked vanilla maps entered it, so every persisted city re-rolls onto the real set.
        /// </summary>
        public const int CurrentGenVersion = 4;

        /// <summary>Catalogue variant id (chosen at new game; the ONLY city identity written to the save — the map/seed pair is looked up from it).</summary>
        public int VariantId;

        /// <summary>Generator format version that produced the current buffer (see <see cref="CurrentGenVersion"/>).</summary>
        public int GenVersion;

        /// <summary>True once the target buffer has been generated for this city (guards the one-shot generation seam).</summary>
        public bool Generated;

        /// <summary>
        /// Hash of the catalog-affecting balance tuning (target counts / AA geometry) the current
        /// buffer was generated under (<c>MirrorCityGenerator.ComputeTuningHash</c>). A load whose
        /// live config hashes differently regenerates the city (same pre-release policy as a
        /// <see cref="GenVersion"/> mismatch): those fields shift the generator's RNG draw sequence,
        /// so without the stamp a remote balance retune would silently leave a persisted city no
        /// (variant, config) pair can reproduce — and the offline-validated catalog would no longer
        /// describe what players see.
        /// </summary>
        public int TuningHash;

        /// <summary>Accumulated cut to the Physical axis cap from destroyed key (physical) targets. Subtracted from the base cap in the fold.</summary>
        public float CapCutPhysical;

        /// <summary>Accumulated cut to the Digital axis cap from destroyed key (digital) targets.</summary>
        public float CapCutDigital;

        /// <summary>Accumulated cut to the Social axis cap from destroyed key (social) targets.</summary>
        public float CapCutSocial;

        /// <summary>Fresh, un-generated mirror city — a new game generates its buffer on the first tick.</summary>
        public static MirrorCityState Default => default;

        /// <summary>
        /// Domain-Driven Initialization: ensures the mirror-city singleton and its target buffer
        /// exist, CO-LOCATED on the <see cref="EnemyState"/> singleton entity. Ensures
        /// <see cref="EnemyState"/> first (idempotent) so the shared entity exists, then attaches the
        /// header component and the <see cref="EnemyTarget"/> buffer if missing. Call from the owner
        /// system's OnCreate()/OnStartRunning()/OnLoadRestore().
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            // Co-locate the mirror-city header + target buffer on the EnemyState singleton entity so
            // the derived axes and the model they derive from share one entity (and MirrorCitySystem
            // persists header + buffer as one blob). EnsurePaired keeps this query-first, dedup-aware
            // and shape-aware (CIVIC432) instead of a hand-rolled query.
            var policy = new EnsurePairedPolicy<EnemyState, MirrorCityState>
            {
                EnsureShape = static (m, e) =>
                {
                    if (!m.HasBuffer<EnemyTarget>(e))
                        m.AddBuffer<EnemyTarget>(e);
                },
            };
            CivicSingleton.EnsurePaired(em, EnemyState.Default, Default, policy);
        }

        /// <summary>Reset to domain defaults (un-generated city).</summary>
        public void SetDefaults()
        {
            this = Default;
        }
    }
}
