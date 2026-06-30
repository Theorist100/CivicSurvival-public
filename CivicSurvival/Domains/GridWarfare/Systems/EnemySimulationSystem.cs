using Game;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.GridWarfare.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// Simulates the mirror enemy: regenerates the three axes (physical/digital/social)
    /// that counter-strikes lower. Player attack damage is applied by
    /// EnemyOperationEffectSystem in ModificationEnd.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(EnemyState))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class EnemySimulationSystem : CivicSystemBase, ICivicSingletonOwner<EnemyState>
#if DEBUG
        , IEnemyDebugMutator
#endif
    {
        private static readonly LogContext Log = new("EnemySimulationSystem");

        // Balance — resolved live from BalanceConfig.Current.GridWarfare (remote-config tunable).
        private static GridWarfareConfig Cfg => BalanceConfig.Current.GridWarfare;

        /// <summary>Lower clamp on each enemy axis (shared with EnemyOperationEffectSystem).</summary>
        internal static float AxisFloor => Cfg.PressureFloor;
        private static float AxisCap => Cfg.PressureCap;

        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_CurrentActQuery;
        [System.NonSerialized] private float m_LastAxisRegenGameTimeHours;
        [System.NonSerialized] private bool m_RegenClockInitialized;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            EnemyState.EnsureExists(EntityManager);

            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadWrite<EnemyState>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            RequireForUpdate(m_EnemyStateQuery);
            WireServices();
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IEnemyDebugMutator>(this);
#endif
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            EnemyState.EnsureExists(EntityManager);
        }

        private void WireServices() => Log.Info("Services wired");

        protected override void OnUpdateImpl()
        {
            if (m_EnemyStateQuery.IsEmptyIgnoreFilter) return;

            // Freeze until Adaptation: no axis regen before the counterattack unlocks.
            // ECS-Pure: Check phase via CurrentActSingleton
#pragma warning disable CIVIC070 // Act guard — CurrentActSingleton changes at act transitions only
            if (!m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton) || actSingleton.CurrentAct < Act.Adaptation)
#pragma warning restore CIVIC070
            {
                return;
            }

            if (!TryGetGameTimeHours(out var gameTimeHours))
                return;
            // HIGH FIX: Use TryGetSingletonRW to avoid crash if singleton missing
            if (!SystemAPI.TryGetSingletonRW<EnemyState>(out var enemyState))
                return;

            if (!m_RegenClockInitialized)
            {
                m_LastAxisRegenGameTimeHours = gameTimeHours;
                m_RegenClockInitialized = true;
            }

            RegenerateAxes(ref enemyState.ValueRW, gameTimeHours);
        }

        private static bool TryGetGameTimeHours(out float gameTimeHours)
        {
            // FIX F7: Work in hours to avoid float precision loss at long sessions.
            // LOAD-INVARIANT: OnUpdate can run before GameTime activation on the first loaded frame.
            return GameTimeSystem.TryGetGameHours(out gameTimeHours);
        }

        private void RegenerateAxes(ref EnemyState state, float gameTimeHours)
        {
            float deltaHours = math.max(0f, gameTimeHours - m_LastAxisRegenGameTimeHours);
            m_LastAxisRegenGameTimeHours = gameTimeHours;
            if (deltaHours <= 0f) return;

            float cap = AxisCap;
            float regen = state.RegenRatePerHour * deltaHours;
            state.PhysicalAxis = math.min(cap, state.PhysicalAxis + regen);
            state.DigitalAxis = math.min(cap, state.DigitalAxis + regen);
            state.SocialAxis = math.min(cap, state.SocialAxis + regen);

            // Re-arm the act-objective loot once regen has lifted the enemy back out of the
            // all-axes-suppressed state: as soon as any axis climbs above the objective threshold,
            // the previous collapse is over and the next genuine collapse may pay again. The
            // suppression-side latch lives in EnemyOperationEffectSystem; this is its only reset.
            if (state.ObjectiveClaimed && state.AnyAxisAbove(Cfg.ObjectiveAxisThreshold))
                state.ObjectiveClaimed = false;
        }

        /// <summary>
        /// Resolve the damage a counter-strike applies to the targeted axis.
        /// In the mirror-enemy model there are no stance block/vulnerable multipliers —
        /// the axis "defence" is its real value (you hit a weak axis for full effect).
        /// Kept as a public seam because PlayerAttackSystem records the figure on the slot.
        /// </summary>
        public float CalculateDamage(AttackCategory category, float baseDamage)
        {
            return baseDamage;
        }

        // S3400: the constant returns below are not stubs — the mirror-enemy axis model
        // genuinely has no RPS block and no vulnerable window. They are kept as a public
        // seam only so PlayerAttackSystem (which records the flags on its operation slot)
        // compiles unchanged; the flags are dropped together with that slot state in a
        // later phase.
#pragma warning disable S3400
        /// <summary>
        /// Mirror-enemy model has no RPS block — always false.
        /// Retained because PlayerAttackSystem stores the flag on the operation slot.
        /// </summary>
        public bool IsAttackBlocked(AttackCategory category) => false;

        /// <summary>
        /// Mirror-enemy model has no vulnerable window — always false.
        /// Retained because PlayerAttackSystem stores the flag on the operation slot.
        /// </summary>
        public bool IsVulnerableWindow() => false;
#pragma warning restore S3400

        /// <summary>
        /// Get current enemy state for UI bindings.
        /// </summary>
        public EnemyState GetState()
        {
            if (!m_EnemyStateQuery.TryGetSingleton<EnemyState>(out var state))
                return EnemyState.Default;
            return state;
        }

#if DEBUG
        public void DebugSetPressure(float value, string source)
        {
            if (!m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var entity))
                return;
            var state = EntityManager.GetComponentData<EnemyState>(entity);
            float clamped = math.clamp(value, AxisFloor, AxisCap);
            float oldMean = (state.PhysicalAxis + state.DigitalAxis + state.SocialAxis) / 3f;
            state.PhysicalAxis = clamped;
            state.DigitalAxis = clamped;
            state.SocialAxis = clamped;
            EntityManager.SetComponentData(entity, state);
            EventBus?.SafePublish(new EnemyAxisChangedEvent(oldMean, clamped, AttackCategory.Kinetic, source), "EnemySimulationSystem.DebugSetPressure");
            Log.Info($"[DEBUG] {source}: enemy axes -> {clamped:F1}");
        }

        public void DebugResetEnemy(string source)
        {
            if (!m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var entity))
                return;
            EntityManager.SetComponentData(entity, EnemyState.Default);
            m_RegenClockInitialized = false;
            Log.Info($"[DEBUG] {source}: enemy state reset");
        }
#endif

        protected override void OnDestroy()
        {
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IEnemyDebugMutator>(this);
#endif
            base.OnDestroy();
        }
    }
}
