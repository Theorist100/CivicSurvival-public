using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// Enemy state singleton — mirror of the city's three stability axes.
    /// Each axis is the enemy's "health" along one dimension (floor..cap %); a
    /// successful counter-strike lowers the axis the attack category targets
    /// (Kinetic→Physical, Cyber→Digital, Psyops→Social). Lower axes weaken waves.
    /// The enemy's own defence (InterceptChance) rolls per arriving outbound strike.
    /// </summary>
    public struct EnemyState : IComponentData
    {
        /// <summary>Physical axis "health" (floor..cap %). Lowered by KINETIC strikes.</summary>
        public float PhysicalAxis;

        /// <summary>Digital axis "health" (floor..cap %). Lowered by CYBER strikes.</summary>
        public float DigitalAxis;

        /// <summary>Social axis "health" (floor..cap %). Lowered by PSYOPS strikes.</summary>
        public float SocialAxis;

        /// <summary>Axis regeneration rate per game hour (shared by all three axes).</summary>
        public float RegenRatePerHour;

        /// <summary>
        /// Enemy defence ("their air defence"): probability [0..1] that an arriving
        /// outbound strike is intercepted before it lowers the targeted axis.
        /// </summary>
        public float InterceptChance;

        /// <summary>
        /// Game-hour timestamp at which the Physical-axis respite window expires (0 = no respite).
        /// Set when a counter-strike drops the Physical axis to its floor: the enemy "regroups"
        /// and physical (KINETIC-type) waves weaken until <see cref="GameTimeSystem"/> game time
        /// passes this mark, after which regen restores the axis. Stored as an absolute game-hour
        /// (stable across save/load — unlike ElapsedTime) so the window survives a save/load.
        /// </summary>
        public float RespiteUntilPhysical;

        /// <summary>Game-hour timestamp at which the Digital-axis respite window expires (0 = none).</summary>
        public float RespiteUntilDigital;

        /// <summary>Game-hour timestamp at which the Social-axis respite window expires (0 = none).</summary>
        public float RespiteUntilSocial;

        /// <summary>
        /// Monotonic count of enemy-beachhead collapses already rewarded. Forms the durable,
        /// per-collapse idempotency key (<c>GwObjective:{count}</c>) for the Shadow Cash loot so
        /// the same collapse pays exactly once even across a save/load mid-frame.
        /// </summary>
        public int ObjectiveCollapseCount;

        /// <summary>
        /// Terminal-guard latch: true once the current all-axes-suppressed collapse has been
        /// rewarded. Reset to false the moment regen lifts any axis back above the objective
        /// threshold, so the next genuine collapse can pay again (not every tick).
        /// </summary>
        public bool ObjectiveClaimed;

        public static EnemyState Default
            => FromSnapshot(
                CityAxisSnapshot.SyntheticEnemy,
                BalanceConfig.Current.GridWarfare,
                BalanceConfig.Current.CityStability);

        /// <summary>
        /// Build the enemy's STARTING three axes from a city-shape snapshot, deriving each axis
        /// "health" from the matching <see cref="CityAxisFormulas"/> instability so the enemy is
        /// the same form as a city: a stable city-shape (low instability) becomes a healthy enemy
        /// (axis near the cap), a battered one becomes a weak enemy (axis near the floor).
        ///
        /// Per axis: <c>axis = floor + (1 - instability) · (cap - floor)</c>, where the
        /// instability is the SAME <see cref="CityAxisFormulas"/> rule the runtime city applies,
        /// fed the snapshot's raw counts and the caller's balance weights/caps (read from
        /// <paramref name="stability"/>, exactly as <c>CityStabilitySystem</c> does). With the
        /// synthetic fully-healthy snapshot all three instabilities are 0, so every axis lands on
        /// <c>cap</c> — byte-identical to the prior cap-everywhere default.
        ///
        /// This is initialization only. The returned axes are then the stored, mutable battle
        /// state: <see cref="ReduceAxis"/> lowers them and regen raises them. The snapshot is a
        /// seed and is not consulted again, so <see cref="ReduceAxis"/> / <see cref="GetAxis"/> /
        /// <see cref="AggregatePressure01"/> behave exactly as before.
        ///
        /// PvP (Wave 4) calls this with a snapshot filled from another player's serialized city
        /// instead of the synthetic one — the only thing that changes is the data source.
        /// </summary>
        public static EnemyState FromSnapshot(in CityAxisSnapshot snapshot, GridWarfareConfig gw, CityStabilityConfig stability)
        {
            float floor = gw.PressureFloor;
            float cap = gw.PressureCap;
            float span = math.max(0f, cap - floor);

            float physicalInstability = CityAxisFormulas.PhysicalInstability(
                snapshot.AffectedDistricts, stability.TotalDistricts,
                snapshot.BuildingsDestroyed, stability.MaxDestroyedBuildings,
                snapshot.BuildingsOnFire, stability.MaxFires,
                stability.BlackoutSubWeight, stability.DestroyedSubWeight, stability.FiresSubWeight);

            float digitalInstability = CityAxisFormulas.DigitalInstability(
                snapshot.PowerBalance, snapshot.PowerConsumption, snapshot.GridStatus,
                stability.DeficitSubWeight, stability.StressSubWeight);

            float socialInstability = CityAxisFormulas.SocialInstability(
                snapshot.MaxHappinessPenalty, PenaltyConfig.MAX_HAPPINESS_PENALTY);

            return new EnemyState
            {
                PhysicalAxis = AxisFromInstability(physicalInstability, floor, span),
                DigitalAxis = AxisFromInstability(digitalInstability, floor, span),
                SocialAxis = AxisFromInstability(socialInstability, floor, span),
                RegenRatePerHour = gw.PressureRegenRatePerHour,
                InterceptChance = gw.EnemyInterceptChance,
                RespiteUntilPhysical = 0f,
                RespiteUntilDigital = 0f,
                RespiteUntilSocial = 0f,
                ObjectiveCollapseCount = 0,
                ObjectiveClaimed = false
            };
        }

        /// <summary>
        /// Map a 0..1 instability into axis "health" in <c>[floor, floor+span]</c>:
        /// <c>floor + (1 - clamp(instability, 0, 1)) · span</c>. Zero instability → cap, full
        /// instability → floor.
        /// </summary>
        private static float AxisFromInstability(float instability, float floor, float span)
            => floor + (1f - math.clamp(instability, 0f, 1f)) * span;

        /// <summary>
        /// Default enemy intercept probability until a tunable balance field lands
        /// (Phase 3.0.6 adds GridWarfareConfig.EnemyInterceptChance to balance.contract.yaml).
        /// </summary>
        public const float DefaultInterceptChance = 0.25f;

        /// <summary>
        /// Domain-Driven Initialization: Creates singleton if not exists.
        /// Call from primary writer system's OnCreate().
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        /// <summary>Reset to domain defaults.</summary>
        public void SetDefaults()
        {
            this = Default;
        }

        /// <summary>
        /// Read the axis a given attack category targets
        /// (Kinetic→Physical, Cyber→Digital, Psyops→Social).
        /// </summary>
        public readonly float GetAxis(AttackCategory category) => category switch
        {
            AttackCategory.Kinetic => PhysicalAxis,
            AttackCategory.Cyber => DigitalAxis,
            AttackCategory.Psyops => SocialAxis,
            _ => throw new System.ArgumentOutOfRangeException(nameof(category), category, "Unknown AttackCategory — add case to EnemyState.GetAxis")
        };

        /// <summary>
        /// Lower the axis a counter-strike of <paramref name="category"/> targets by
        /// <paramref name="damage"/>, clamped to <paramref name="floor"/>. Returns the new
        /// axis value. The caller routes Category→axis here so the mapping has one home.
        /// </summary>
        public float ReduceAxis(AttackCategory category, float damage, float floor)
        {
            float reduced;
            switch (category)
            {
                case AttackCategory.Kinetic:
                    PhysicalAxis = reduced = math.max(floor, PhysicalAxis - damage);
                    break;
                case AttackCategory.Cyber:
                    DigitalAxis = reduced = math.max(floor, DigitalAxis - damage);
                    break;
                case AttackCategory.Psyops:
                    SocialAxis = reduced = math.max(floor, SocialAxis - damage);
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(category), category, "Unknown AttackCategory — add case to EnemyState.ReduceAxis");
            }
            return reduced;
        }

        /// <summary>
        /// Aggregate the three axes into a single 0..1 enemy-pressure factor for wave scaling.
        /// Mean of the three axes divided by <paramref name="cap"/>. This is the only seam
        /// where the enemy's axis health feeds back into wave strength.
        /// </summary>
        public readonly float AggregatePressure01(float cap)
        {
            float denom = math.max(cap, 1f);
            float mean = (PhysicalAxis + DigitalAxis + SocialAxis) / 3f;
            return math.clamp(mean / denom, 0f, 1f);
        }

        // ----------------------------------------------------------------------------
        // Respite + act-objective (the suppression → regroup → loot loop, Phase 3.6.3)
        // ----------------------------------------------------------------------------

        /// <summary>
        /// Open the "enemy regroups" window for the axis a counter-strike just floored: waves of
        /// that category weaken (see <c>WaveContextGatherer</c>) until <paramref name="nowHours"/>
        /// game time passes <paramref name="nowHours"/> + <paramref name="windowHours"/>, after which
        /// regen restores the axis. Idempotent on a single floor-touch: re-flooring an axis already
        /// in respite refreshes the window to the later end (never shortens it).
        /// </summary>
        public void BeginRespite(AttackCategory category, float nowHours, float windowHours)
        {
            float until = nowHours + math.max(0f, windowHours);
            switch (category)
            {
                case AttackCategory.Kinetic: RespiteUntilPhysical = math.max(RespiteUntilPhysical, until); break;
                case AttackCategory.Cyber: RespiteUntilDigital = math.max(RespiteUntilDigital, until); break;
                case AttackCategory.Psyops: RespiteUntilSocial = math.max(RespiteUntilSocial, until); break;
                default: throw new System.ArgumentOutOfRangeException(nameof(category), category, "Unknown AttackCategory — add case to EnemyState.BeginRespite");
            }
        }

        /// <summary>
        /// True while the given axis is in its post-floor respite window
        /// (<paramref name="nowHours"/> &lt; the stored expiry). A 0 expiry is never active.
        /// </summary>
        public readonly bool IsRespiteActive(AttackCategory category, float nowHours) => category switch
        {
            AttackCategory.Kinetic => RespiteUntilPhysical > 0f && nowHours < RespiteUntilPhysical,
            AttackCategory.Cyber => RespiteUntilDigital > 0f && nowHours < RespiteUntilDigital,
            AttackCategory.Psyops => RespiteUntilSocial > 0f && nowHours < RespiteUntilSocial,
            _ => throw new System.ArgumentOutOfRangeException(nameof(category), category, "Unknown AttackCategory — add case to EnemyState.IsRespiteActive")
        };

        /// <summary>
        /// Number of axes (0..3) whose respite window is active at <paramref name="nowHours"/>.
        /// Used by <c>WaveContextGatherer</c> to weaken wave strength by one multiplier per
        /// suppressed axis — the single seam where respite feeds back into wave scaling.
        /// </summary>
        public readonly int RespiteActiveAxisCount(float nowHours)
        {
            int n = 0;
            if (RespiteUntilPhysical > 0f && nowHours < RespiteUntilPhysical) n++;
            if (RespiteUntilDigital > 0f && nowHours < RespiteUntilDigital) n++;
            if (RespiteUntilSocial > 0f && nowHours < RespiteUntilSocial) n++;
            return n;
        }

        /// <summary>All three axes at or below <paramref name="threshold"/> — the act-objective condition.</summary>
        public readonly bool AllAxesBelow(float threshold)
            => PhysicalAxis <= threshold && DigitalAxis <= threshold && SocialAxis <= threshold;

        /// <summary>Any axis strictly above <paramref name="threshold"/> — the objective-latch reset condition.</summary>
        public readonly bool AnyAxisAbove(float threshold)
            => PhysicalAxis > threshold || DigitalAxis > threshold || SocialAxis > threshold;
    }
}
