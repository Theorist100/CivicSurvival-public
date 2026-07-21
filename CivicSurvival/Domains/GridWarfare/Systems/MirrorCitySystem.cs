using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// Owns the mirror enemy's spatial model: the <see cref="MirrorCityState"/> header and the
    /// <c>DynamicBuffer&lt;EnemyTarget&gt;</c> (co-located on the <see cref="EnemyState"/> singleton
    /// entity). Two responsibilities:
    ///
    /// 1. <see cref="RecomputeAxes"/> — the single point that writes <see cref="EnemyState"/>'s three
    ///    axes FROM the target model (design decision 1: targets authoritative, axes a derived cache).
    ///    Called after any change to the target buffer (generation, repair, rebuild).
    /// 2. Repair / rebuild tick (<see cref="OnUpdateImpl"/>, on the game-time clock, same pattern as
    ///    <c>EnemySimulationSystem.m_LastAxisRegenGameTimeHours</c>): damaged targets repair, destroyed
    ///    Regular targets re-appear as construction sites (RebuildProgress 0→1), Key targets flagged
    ///    <c>DestroyedForever</c> never return, Reserve targets are invulnerable.
    ///
    /// State-bearing singleton → persisted through <see cref="Core.Serialization.MirrorCityCodec"/> in
    /// the sibling <c>MirrorCitySystem.Serialization.cs</c> (NOT IEmptySerializable — dropping the buffer
    /// on load would lose the whole city). Restore follows the EnemySimulationSystem owner pattern
    /// (Deserialize → buffer; OnLoadRestore recreates the singleton + buffer and applies the payload).
    ///
    /// TRANSITIONAL (phase A/B skeleton): <c>EnemySimulationSystem</c> still owns and regenerates the
    /// axes the old way; this system's <see cref="RecomputeAxes"/> only fires when the target buffer
    /// actually changes, so with an un-damaged city (phase C has not yet routed strike damage onto
    /// targets) it does not fight the existing regen. Phase B consolidates axis ownership here and
    /// reworks EnemySimulationSystem's regen into this repair tick — see the phase-B seam in
    /// <see cref="RecomputeAxes"/>.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(MirrorCityState))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class MirrorCitySystem : CivicSystemBase, ICivicSingletonOwner<MirrorCityState>
    {
        private static readonly LogContext Log = new("MirrorCitySystem");

        // Balance — resolved live from BalanceConfig.Current.GridWarfare (remote-config tunable).
        private static GridWarfareConfig Cfg => BalanceConfig.Current.GridWarfare;

        // Life-cycle + strike-resolution tuning — read live from the balance contract's MirrorCity
        // section (Docs/Contracts/balance.contract.yaml → GridWarfareConfig, regenerated), so these
        // stay remote-tunable without a rebuild. Field names mirror the contract paths
        // (GridWarfare.MirrorCity* → Cfg.MirrorCity*).
        private static float RepairRatePerHour => Cfg.MirrorCityRepairRatePerHour;             // hp/game-hour for damaged-but-standing targets
        private static float RebuildRatePerHour => Cfg.MirrorCityRebuildRatePerHour;           // construction progress/game-hour (0..1)
        private static float ConstructionHpFraction => Cfg.MirrorCityConstructionHpFraction;   // a construction site sits at this fraction of MaxHp (cheap to re-hit)
        private static float RebuildDelayHours => Cfg.MirrorCityRebuildDelayHours;             // delay before a destroyed Regular begins re-constructing (decision 4)
        private static float HpDamagePerAxisPoint => Cfg.MirrorCityHpDamagePerAxisPoint;       // axis-damage% → target hp conversion
        private static float PerAaSiteInterceptChance => Cfg.MirrorCityPerAaSiteInterceptChance;// per covering AA site, combined as a probability union
        private static float DroneInterceptMultiplier => Cfg.MirrorCityDroneInterceptMultiplier;// drones fly fully exposed; a ballistic strike would lower this
        private static float KeyTargetCapCutFraction => Cfg.MirrorCityKeyTargetCapCutFraction; // per key kill, cut this fraction of PressureCap off the axis cap (decision 4: 10–15%)

        private EntityQuery m_MirrorQuery;
        private EntityQuery m_CurrentActQuery;

        // Axis-owner seam: resolved lazily (registry population order is not guaranteed at OnCreate).
        private EnemySimulationSystem? m_EnemySimulation;

        // Baked-map catalog (I/O boundary of the pure generator): process-wide shared cache, so the
        // OnStartRunning pre-warm below also covers the UI system's contour builds.
        private readonly Core.Services.MirrorCityMapCatalog m_MapCatalog = Core.Services.MirrorCityMapCatalog.Shared;

        [System.NonSerialized] private float m_LastRepairGameTimeHours;
        [System.NonSerialized] private bool m_RepairClockInitialized;

        /// <summary>
        /// Old-save migration gate (decision 11): when the first generation of this city finds the
        /// enemy axes already beaten below what a full-hp city would produce, seed the freshly placed
        /// Regular targets damaged so the derived axes ≈ the pre-existing values ("received a recon
        /// map", not a hard reset). Default true so a genuine new game (axes healthy → self-gating
        /// no-op) and a pre-mirror old save both flow through; set FALSE only for a genVersion-mismatch
        /// regeneration (decision 12: bump = reset to defaults, NO damage carry-over — the city
        /// regenerates at full hp regardless of the stale derived axes).
        /// </summary>
        [System.NonSerialized] private bool m_SeedDamageFromAxes = true;

        /// <summary>
        /// Tuning-only regeneration gate: set by Deserialize when the persisted city must regenerate
        /// because of a TuningHash mismatch (balance retune) — the next <see cref="TryGenerateCity"/>
        /// then KEEPS the restored <c>VariantId</c> and <c>CapCut*</c> instead of re-rolling the
        /// variant and zeroing the permanent progress. One-shot; never set for a genVersion bump
        /// (decision 12: format bump = full reset).
        /// </summary>
        [System.NonSerialized] private bool m_PreserveProgressOnRegen;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization: ensures MirrorCityState + EnemyTarget buffer on the
            // EnemyState singleton entity.
            MirrorCityState.EnsureExists(EntityManager);

            m_MirrorQuery = GetEntityQuery(ComponentType.ReadWrite<MirrorCityState>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            RequireForUpdate(m_MirrorQuery);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            MirrorCityState.EnsureExists(EntityManager);

            // Rule 15: feature resolution belongs in OnStartRunning. The ??= copies inside the
            // externally-reachable seams (RecomputeAxes/ApplyStrikeToTarget) stay as a backstop for
            // a call arriving before the first start tick.
            m_EnemySimulation ??= FeatureRegistry.Instance.Require<EnemySimulationSystem>();

            // Warm the baked-map parse cache off the hot path: the synchronous disk reads of the
            // land-mask resources happen here (load/start screen), so neither the first generation
            // tick nor the first War Room STRIKE publish stalls the main thread on I/O.
            // PERF-LOCK: warm EVERY pooled map, not just the current variant's — on a new game the
            // variant is rolled LATER (TryGenerateCity), so warming by state.VariantId (default 0)
            // would leave the rolled map cold and re-introduce a mid-gameplay disk read.
            foreach (var mapId in MirrorCityVariantCatalog.MapPool)
                m_MapCatalog.GetLandMask(mapId);
        }

        protected override void OnUpdateImpl()
        {
            if (m_MirrorQuery.IsEmptyIgnoreFilter)
                return;

            // Freeze until the war starts: no city activity before the counterattack unlocks.
            // Mirrors EnemySimulationSystem's act gate.
#pragma warning disable CIVIC070 // Act guard — CurrentActSingleton changes at act transitions only
            if (!m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton) || actSingleton.CurrentAct < Act.Crisis)
#pragma warning restore CIVIC070
            {
                return;
            }

            if (!GameTimeSystem.TryGetGameHours(out var gameTimeHours))
                return;
            if (!SystemAPI.TryGetSingletonRW<MirrorCityState>(out var stateRW))
                return;

            var buffer = SystemAPI.GetSingletonBuffer<EnemyTarget>();

            // Resolve the axis owner up-front: the one-shot generation seam needs the CURRENT enemy
            // axes to migrate an old save's progress onto the fresh city (decision 11).
            m_EnemySimulation ??= FeatureRegistry.Instance.Require<EnemySimulationSystem>();
            var enemyState = m_EnemySimulation.GetState();

            bool changed = false;

            // One-shot generation seam. If the city has not been generated (new game / genVersion
            // mismatch reset), build it from the deterministic generator with a placeholder land-mask.
            // Real baked-contour integration arrives from the generator agent.
            if (!stateRW.ValueRO.Generated)
            {
                if (TryGenerateCity(ref stateRW.ValueRW, buffer, in enemyState))
                    changed = true;
            }

            if (!m_RepairClockInitialized)
            {
                m_LastRepairGameTimeHours = gameTimeHours;
                m_RepairClockInitialized = true;
            }

            float deltaHours = math.max(0f, gameTimeHours - m_LastRepairGameTimeHours);
            m_LastRepairGameTimeHours = gameTimeHours;

            // Repair/rebuild honours each axis's respite window: while an axis is "regrouping" (floored
            // by a strike), its targets pause repair so the axis stays suppressed until the window
            // expires — the same suppression semantics EnemyOperationEffectSystem arms on arrival.

            if (TickRepairs(buffer, deltaHours, in enemyState, gameTimeHours))
                changed = true;

            // Event-driven recompute: only when the target model actually moved this tick, so the
            // derived axes are not needlessly overwritten every frame while the old regen path still runs.
            if (changed)
                RecomputeAxes();
        }

        /// <summary>
        /// The single point that writes <see cref="EnemyState"/>'s three axes from the target model.
        /// Folds the live <see cref="EnemyTarget"/> buffer into per-axis values via
        /// <see cref="MirrorCityMath.RecomputeAxis"/> (floor from the reserve target, cap reduced by the
        /// accumulated key-target cuts) and writes them onto the enemy singleton.
        ///
        /// Ownership: <see cref="EnemyState"/> stays single-writer-owned by
        /// <c>EnemySimulationSystem</c> — this method computes the values and hands them through
        /// its <c>ApplyTargetDerivedAxes</c> seam instead of writing the singleton directly, so
        /// the one-owner rule (CIVIC175) holds. Phase B may consolidate ownership here when the
        /// old regen retires into the repair tick.
        /// </summary>
        public void RecomputeAxes()
        {
            // Cached query + EntityManager (not SystemAPI) — externally reachable, must not bind to
            // another system's SystemAPI context (CIVIC281).
            if (!m_MirrorQuery.TryGetSingletonEntity<MirrorCityState>(out var entity))
                return;
            if (!EntityManager.HasBuffer<EnemyTarget>(entity))
                return;

            var state = EntityManager.GetComponentData<MirrorCityState>(entity);
            var buffer = EntityManager.GetBuffer<EnemyTarget>(entity, isReadOnly: true);
            var targets = buffer.AsNativeArray();

            float baseCap = Cfg.PressureCap;
            float physical = MirrorCityMath.RecomputeAxis(targets, AttackCategory.Kinetic, baseCap, state.CapCutPhysical);
            float digital = MirrorCityMath.RecomputeAxis(targets, AttackCategory.Cyber, baseCap, state.CapCutDigital);
            float social = MirrorCityMath.RecomputeAxis(targets, AttackCategory.Psyops, baseCap, state.CapCutSocial);

            m_EnemySimulation ??= FeatureRegistry.Instance.Require<EnemySimulationSystem>();
            m_EnemySimulation.ApplyTargetDerivedAxes(physical, digital, social);
        }

        /// <summary>
        /// True once this city's target buffer has been generated — the strike-arrival owner
        /// (<c>EnemyOperationEffectSystem</c>) uses it to choose the target-resolution path over the legacy
        /// axis path (which stays the fallback until a city exists).
        /// </summary>
        public bool IsGenerated =>
            m_MirrorQuery.TryGetSingleton<MirrorCityState>(out var state) && state.Generated;

        /// <summary>
        /// Resolve one arriving counter-strike against a concrete target (phase C — the strike-to-target
        /// seam). Converts <paramref name="axisDamage"/> to hp damage (draft constant), computes the
        /// positional AA intercept chance for the resolved target, and calls
        /// <see cref="StrikeResolver.ResolveTargetStrike"/> with the launch-frozen <paramref name="seed"/>
        /// so the verdict is deterministic across save/load and on a server. On a landed hit it applies the
        /// hp damage and the per-tier death rules:
        ///   Regular — on death, schedule a rebuild at <c>now + RebuildDelayHours</c>;
        ///   Key     — on death, permanent (Hp 0, no rebuild) and cut this axis's cap (<see cref="MirrorCityState"/>);
        ///   AaSite  — on death, suppressed (repairs immediately via the repair tick, no rebuild delay);
        ///   Reserve — invulnerable: 0 damage, but the outcome is reported honestly.
        /// Then recomputes the derived axes. Target selection is the shared
        /// <see cref="MirrorTargetSelection"/> rule (the same one Execute's auto-select uses): the explicit
        /// <paramref name="targetId"/> is honoured while it still names a live target of
        /// <paramref name="category"/> (including a deliberate SEAD pick on an AA site); otherwise the
        /// deterministic fallback picks the live contributing target of that axis with the highest hp, or
        /// the axis's reserve target if none are live.
        /// </summary>
        public MirrorStrikeResult ApplyStrikeToTarget(ushort targetId, AttackCategory category, float axisDamage, uint seed)
        {
            var result = new MirrorStrikeResult
            {
                Axis = category,
                ResolvedTargetId = EnemyTarget.NoTargetId
            };

            // Cached query + EntityManager (not SystemAPI) — externally reachable, must not bind to
            // another system's SystemAPI context (CIVIC281).
            if (!m_MirrorQuery.TryGetSingletonEntity<MirrorCityState>(out var entity))
                return result;
            var state = EntityManager.GetComponentData<MirrorCityState>(entity);
            if (!state.Generated)
                return result; // no generated city — caller falls back to the axis path
            if (!EntityManager.HasBuffer<EnemyTarget>(entity))
                return result;

            var buffer = EntityManager.GetBuffer<EnemyTarget>(entity);
            if (buffer.Length == 0)
                return result;

            int index = MirrorTargetSelection.ResolveStrikeIndex(buffer, targetId, category);
            if (index < 0)
                return result; // no target of this axis at all (a generated city always has a reserve)

            var target = buffer[index];
            result.ResolvedTargetId = target.Id;
            result.Tier = target.Tier;

            float oldAxis = ReadAxis(category);
            result.OldAxis = oldAxis;
            result.NewAxis = oldAxis;

            // The effective floor of this axis: the model's structural floor (reserve contribution,
            // the same clamp RecomputeAxis applies) combined with the balance PressureFloor clamp
            // the axis owner applies in ApplyTargetDerivedAxes. Carried on the result so the arrival
            // owner arms the respite window against the axis's REAL bottom instead of duplicating
            // (and silently un-syncing) the floor definition.
            var targets = buffer.AsNativeArray();
            result.AxisFloor = math.max(
                MirrorCityMath.AxisFloor(targets, category),
                EnemySimulationSystem.AxisFloor);

            // AA intercept applies only to the physical (kinetic) drone strike flying over enemy air
            // defence; cyber/psyops carriers are not physical projectiles, so nothing intercepts them.
            float interceptChance = category == AttackCategory.Kinetic
                ? MirrorCityMath.EffectiveInterceptChance(targets, index, PerAaSiteInterceptChance, DroneInterceptMultiplier)
                : 0f;

            // Reserve targets are invulnerable — force 0 hp damage but still resolve the verdict honestly.
            bool invulnerable = target.Tier == MirrorTargetTier.Reserve;
            float hpDamage = invulnerable ? 0f : axisDamage * HpDamagePerAxisPoint;

            var strike = StrikeResolver.ResolveTargetStrike(hpDamage, target.Hp, interceptChance, seed);
            result.Intercepted = strike.Intercepted;
            result.Applied = true;

            if (strike.Intercepted)
                return result; // intercepted — axes unchanged

            if (strike.AppliedDamage <= 0f)
            {
                // Landed on an invulnerable reserve (the axis is stripped bare) — an honest no-op,
                // reported distinctly so the arrival owner can tell the player instead of publishing
                // a misleading "-0.0%" axis change.
                result.NoEffect = true;
                return result;
            }

            target.Hp = strike.NewHp;
            if (target.Hp <= 0f)
                ApplyTargetDeath(ref target, ref state, category, ref result);

            buffer[index] = target;
            // Commit the header (cap cuts) before RecomputeAxes reads it back through EntityManager.
            EntityManager.SetComponentData(entity, state);
            RecomputeAxes();
            result.NewAxis = ReadAxis(category);
            return result;
        }

        /// <summary>Read the current derived value of one axis from the enemy singleton (owner: EnemySimulationSystem).</summary>
        private float ReadAxis(AttackCategory category)
        {
            m_EnemySimulation ??= FeatureRegistry.Instance.Require<EnemySimulationSystem>();
            return m_EnemySimulation.GetState().GetAxis(category);
        }

        /// <summary>
        /// Apply the per-tier consequences of a target reaching 0 hp. Mutates the target (and, for a key
        /// target, the city header's axis cap cut) in place; records the death on <paramref name="result"/>.
        /// </summary>
        private static void ApplyTargetDeath(ref EnemyTarget target, ref MirrorCityState state, AttackCategory category, ref MirrorStrikeResult result)
        {
            result.TargetDestroyed = true;
            switch (target.Tier)
            {
                case MirrorTargetTier.Key:
                    // Permanent kill: never repairs/rebuilds, and its cap cut stays applied (decision 4).
                    target.DestroyedForever = true;
                    target.RebuildProgress = 0f;
                    target.RebuildAtHours = 0f;
                    float cut = KeyTargetCapCutFraction * Cfg.PressureCap;
                    float totalCut;
                    switch (category)
                    {
                        case AttackCategory.Kinetic: state.CapCutPhysical += cut; totalCut = state.CapCutPhysical; break;
                        case AttackCategory.Cyber: state.CapCutDigital += cut; totalCut = state.CapCutDigital; break;
                        case AttackCategory.Psyops: state.CapCutSocial += cut; totalCut = state.CapCutSocial; break;
                        default: totalCut = 0f; break;
                    }
                    result.KeyPermanentKill = true;
                    Log.Info($"KEY target {target.Id} destroyed forever on {category} axis — cap cut -{cut:F1}% (axis cap now {math.max(0f, Cfg.PressureCap - totalCut):F1}%)");
                    break;

                case MirrorTargetTier.Regular:
                    // Becomes a construction site after the rebuild delay (repair tick ramps it 0→1).
                    target.RebuildProgress = 0f;
                    target.RebuildAtHours = NowHours() + RebuildDelayHours;
                    break;

                case MirrorTargetTier.AaSite:
                    // SEAD suppression: repairs immediately (repair tick), no rebuild-delay stamp.
                    target.RebuildAtHours = 0f;
                    break;

                default:
                    break;
            }
        }

        private static float NowHours() => GameTimeSystem.TryGetGameHours(out var gh) ? gh : 0f;

        /// <summary>
        /// Roll the catalog variant for a fresh city: uniform over
        /// [0, <see cref="MirrorCityVariantCatalog.Count"/>). One-shot process entropy avalanched
        /// through a splitmix-style mix (a raw millisecond tick modulo 100 would bias toward the
        /// boot-time neighbourhood); the result is persisted immediately, so determinism of the
        /// model is untouched — the roll happens once per city, everything else derives from the
        /// stored id.
        /// </summary>
        private static int RollVariantId()
        {
            uint s = (uint)System.Environment.TickCount;
            s ^= s >> 16;
            s *= 0x7FEB352Du;
            s ^= s >> 15;
            s *= 0x846CA68Bu;
            s ^= s >> 16;
            return (int)(s % (uint)MirrorCityVariantCatalog.Count);
        }

        /// <summary>
        /// Generation seam: build the target buffer from the deterministic generator and map each
        /// <see cref="GeneratedTarget"/> to one <see cref="EnemyTarget"/> (Id = index, full hp,
        /// RebuildProgress = 1). Returns true when a city was placed.
        ///
        /// Old-save migration (decision 11): if <see cref="m_SeedDamageFromAxes"/> is set and the
        /// current enemy axes (<paramref name="currentEnemy"/>) are already beaten below what a full-hp
        /// city would produce, the freshly placed Regular targets of each axis are damaged
        /// proportionally so the derived axes recompute to ≈ the pre-existing values — the enemy looks
        /// like the player already made progress against it, not like a fresh reset. Deterministic (no
        /// RNG): a single per-axis keep-fraction applied uniformly to that axis's Regular targets. Key
        /// and Reserve targets are never touched (decision 11), so the achievable minimum is their
        /// combined contribution — deeper pre-existing suppression is best-effort. A genVersion-mismatch
        /// regeneration clears the gate first (decision 12), so it always places a full-hp city.
        /// </summary>
        private bool TryGenerateCity(ref MirrorCityState state, DynamicBuffer<EnemyTarget> buffer, in EnemyState currentEnemy)
        {
            // Every generation is a fresh city (new game, or a genVersion reset with no carry-over),
            // so the catalog variant is rolled here, once, and persisted. The roll is the only
            // non-deterministic input of the whole model — everything downstream re-derives from the
            // stored id, so a save/load reproduces the identical city. Exception: a tuning-only
            // regeneration (Deserialize set the preserve gate) keeps the restored VariantId and the
            // permanent CapCut* — the player's map and irreversible progress survive a balance retune.
            bool preserveProgress = m_PreserveProgressOnRegen;
            m_PreserveProgressOnRegen = false;
            if (!preserveProgress)
                state.VariantId = RollVariantId();

            // Geography seam: resolve the variant's vanilla-map contour through the baked-map catalog.
            // The catalog's MapPool only lists maps whose baked resource actually ships, so a null here
            // means a damaged/deleted resource — fall back to an explicit all-land mask through the
            // SAME generator entry point (one code path; the mask is the only thing that differs) so
            // generation never blocks on the content pipeline.
            var variant = MirrorCityVariantCatalog.Get(state.VariantId);
            MirrorCityLandMask? landMask = m_MapCatalog.GetLandMask(variant.MapId);
            if (landMask == null)
            {
                Log.Warn($"Baked map '{variant.MapId}' failed to load — generating variant {state.VariantId} on the all-land fallback mask");
                landMask = MirrorCityLandMask.AllLandDefaultTile;
            }
            GeneratedCity city = MirrorCityGenerator.Generate(variant.Seed, variant.GenVersion, landMask, out int tuningHash);

            buffer.Clear();
            if (!preserveProgress)
            {
                state.CapCutPhysical = 0f;
                state.CapCutDigital = 0f;
                state.CapCutSocial = 0f;
            }

            int reserveCount = 0, keyCount = 0, regularCount = 0, aaCount = 0;
            if (city?.Targets != null)
            {
                int count = math.min(city.Targets.Count, MirrorCityCodec.MaxTargets);
                for (int i = 0; i < count; i++)
                {
                    var g = city.Targets[i];
                    buffer.Add(new EnemyTarget
                    {
                        Id = (ushort)i,
                        Axis = g.Axis,
                        Tier = g.Tier,
                        X = g.X,
                        Z = g.Z,
                        Contrib = g.Contrib,
                        Hp = g.MaxHp,
                        MaxHp = g.MaxHp,
                        RebuildProgress = 1f,
                        AaRange = g.AaRange,
                        DestroyedForever = false
                    });
                    switch (g.Tier)
                    {
                        case MirrorTargetTier.Reserve: reserveCount++; break;
                        case MirrorTargetTier.Key: keyCount++; break;
                        case MirrorTargetTier.Regular: regularCount++; break;
                        case MirrorTargetTier.AaSite: aaCount++; break;
                        default: break; // new tiers are placed but not tallied by name
                    }
                }
            }

            state.Generated = true;
            state.GenVersion = MirrorCityState.CurrentGenVersion;
            // Stamp the catalog-affecting balance tuning this city was generated under; a later load
            // whose config no longer matches regenerates (same policy as a genVersion bump), so a
            // remote balance retune can never silently leave a city that no other input can reproduce.
            // The hash comes OUT of Generate — computed from the exact config snapshot the city was
            // built with, so a remote swap mid-generation can never stamp a hash describing a
            // different config than the buffer's.
            state.TuningHash = tuningHash;
            Log.Info($"Generated mirror city variant={state.VariantId} targets={buffer.Length} " +
                     $"(reserve={reserveCount} key={keyCount} regular={regularCount} aa={aaCount}) genVersion={state.GenVersion} tuningHash={state.TuningHash:X8}");

            // Old-save damage migration (decision 11) — off for a genVersion-mismatch fresh reset (decision 12).
            if (m_SeedDamageFromAxes)
                SeedDamageFromAxes(buffer, in currentEnemy, state.VariantId);
            // One-shot: subsequent generations (there are none once Generated) never re-migrate.
            m_SeedDamageFromAxes = false;

            return true;
        }

        /// <summary>
        /// Decision 11: distribute an old save's per-axis progress onto the freshly generated Regular
        /// targets so <see cref="RecomputeAxes"/> reproduces ≈ the current enemy axes. For each axis:
        /// fixed = Reserve + Key contribution (untouched), regularTotal = Σ Regular contribution,
        /// fullSum = fixed + regularTotal. If the current axis ≥ fullSum the enemy is at least as
        /// healthy as a fresh city — leave the axis at full hp (this is why a new game, whose axes sit
        /// at the cap, is a no-op). Otherwise every Regular of the axis keeps the same fraction
        /// <c>keep = clamp((axis − fixed) / regularTotal, 0, 1)</c> of its contribution: RebuildProgress
        /// = keep and, when damaged, hp drops to the construction-site level so it reads as a partially
        /// standing / rebuilding target and heals back over time via the repair tick. Pure and
        /// deterministic — no RNG, one uniform fraction per axis.
        /// </summary>
        private void SeedDamageFromAxes(DynamicBuffer<EnemyTarget> buffer, in EnemyState currentEnemy, int variantId)
        {
            bool anySeeded = false;
            for (int a = 0; a < AxisOrder.Length; a++)
            {
                AttackCategory axis = AxisOrder[a];

                float fixedContrib = 0f;
                float regularTotal = 0f;
                for (int i = 0; i < buffer.Length; i++)
                {
                    var t = buffer[i];
                    if (t.Axis != axis)
                        continue;
                    switch (t.Tier)
                    {
                        case MirrorTargetTier.Reserve:
                        case MirrorTargetTier.Key:
                            fixedContrib += t.Contrib;
                            break;
                        case MirrorTargetTier.Regular:
                            regularTotal += t.Contrib;
                            break;
                        case MirrorTargetTier.AaSite:
                        default:
                            break; // zero contribution, positional only (and any future tier)
                    }
                }

                if (regularTotal <= 0f)
                    continue; // no rebuildable capacity to damage on this axis

                float targetAxis = currentEnemy.GetAxis(axis);
                float fullSum = fixedContrib + regularTotal;
                if (targetAxis >= fullSum)
                    continue; // enemy already ≥ a fresh city → keep full hp (new-game / healthy path)

                float keep = math.saturate((targetAxis - fixedContrib) / regularTotal);
                float constructionHp = ConstructionHpFraction;

                for (int i = 0; i < buffer.Length; i++)
                {
                    var t = buffer[i];
                    if (t.Axis != axis || t.Tier != MirrorTargetTier.Regular)
                        continue;

                    t.RebuildProgress = keep;
                    if (keep <= 0f)
                    {
                        // Fully knocked out: destroyed, rebuilds from zero immediately (no delay stamp).
                        t.Hp = 0f;
                        t.RebuildAtHours = 0f;
                    }
                    else if (keep < 1f)
                    {
                        // Partially standing construction site: reduced hp, continues ramping to full.
                        t.Hp = constructionHp * t.MaxHp;
                        t.RebuildAtHours = 0f;
                    }
                    buffer[i] = t;
                }

                anySeeded = true;
                Log.Info($"Old-save migration: variant={variantId} axis={axis} seeded to ≈{targetAxis:F1}% " +
                         $"(fresh full={fullSum:F1}%, regular keep={keep:F2})");
            }

            if (!anySeeded)
                Log.Info($"Old-save migration: variant={variantId} — enemy at/above fresh-city strength on all axes, no damage seeded");
        }

        /// <summary>Fixed axis iteration order (Physical/Digital/Social) for deterministic per-axis passes.</summary>
        private static readonly AttackCategory[] AxisOrder =
        {
            AttackCategory.Kinetic,
            AttackCategory.Cyber,
            AttackCategory.Psyops,
        };

        /// <summary>
        /// Advance repair and rebuild by <paramref name="deltaHours"/> of game time. Returns true when
        /// any target changed (so the caller recomputes the derived axes). Per-tier rules:
        ///   Reserve  — invulnerable, nothing to do;
        ///   AaSite   — always repairs toward MaxHp (SEAD suppression is temporary, never a free sky);
        ///   Key      — repairs while standing; if <c>DestroyedForever</c> it never returns;
        ///   Regular  — repairs while standing; once destroyed it waits out its <c>RebuildAtHours</c> stamp,
        ///              then ramps a construction site 0→1 (design decision 4's rebuild delay).
        ///
        /// A target whose axis is in an active respite window (<see cref="EnemyState.IsRespiteActive"/>)
        /// pauses all repair/rebuild — the axis stays suppressed until the window expires, matching the
        /// "enemy regroups" semantics the arrival owner arms when a strike floors an axis.
        /// </summary>
        private static bool TickRepairs(DynamicBuffer<EnemyTarget> buffer, float deltaHours, in EnemyState enemyState, float nowHours)
        {
            if (deltaHours <= 0f)
                return false;

            float repair = RepairRatePerHour * deltaHours;
            float rebuildStep = RebuildRatePerHour * deltaHours;
            bool changed = false;

            for (int i = 0; i < buffer.Length; i++)
            {
                var t = buffer[i];

                // Respite pause: while this target's axis is regrouping, freeze its repair/rebuild.
                if (enemyState.IsRespiteActive(t.Axis, nowHours))
                    continue;

                switch (t.Tier)
                {
                    case MirrorTargetTier.Reserve:
                        continue; // indestructible — never damaged, nothing to restore

                    case MirrorTargetTier.AaSite:
                        if (t.Hp < t.MaxHp)
                        {
                            t.Hp = math.min(t.MaxHp, math.max(0f, t.Hp) + repair);
                            t.RebuildProgress = 1f;
                            buffer[i] = t;
                            changed = true;
                        }
                        continue;

                    case MirrorTargetTier.Key:
                        if (t.DestroyedForever)
                            continue; // permanent kill — never repairs or rebuilds
                        if (t.Hp > 0f && t.Hp < t.MaxHp)
                        {
                            t.Hp = math.min(t.MaxHp, t.Hp + repair);
                            buffer[i] = t;
                            changed = true;
                        }
                        continue;

                    default: // Regular (and any future tier): repair while standing, else rebuild
                        if (t.RebuildProgress >= 1f && t.Hp > 0f)
                        {
                            // Operational (or lightly damaged): repair toward MaxHp.
                            if (t.Hp < t.MaxHp)
                            {
                                t.Hp = math.min(t.MaxHp, t.Hp + repair);
                                buffer[i] = t;
                                changed = true;
                            }
                        }
                        else
                        {
                            // Destroyed: hold at zero progress until the rebuild delay elapses, then ramp a
                            // construction site ("REBUILDING N%") — reduced hp until finished, then restore
                            // to full, mark operational, and clear the rebuild stamp.
                            if (t.RebuildAtHours > 0f && nowHours < t.RebuildAtHours)
                                continue; // waiting out the rebuild delay — no construction yet

                            float prevProgress = math.max(0f, t.RebuildProgress);
                            t.RebuildProgress = math.saturate(prevProgress + rebuildStep);
                            if (prevProgress <= 0f && t.RebuildProgress > 0f)
                                Log.Info($"Construction started: {t.Axis} target {t.Id} rebuilding (0% → {(t.RebuildProgress * 100f):F0}%)");
                            if (t.RebuildProgress >= 1f)
                            {
                                t.Hp = t.MaxHp;
                                t.RebuildAtHours = 0f;
                                Log.Info($"Construction complete: {t.Axis} target {t.Id} back to full contribution");
                            }
                            else
                            {
                                t.Hp = ConstructionHpFraction * t.MaxHp;
                            }
                            buffer[i] = t;
                            changed = true;
                        }
                        continue;
                }
            }

            return changed;
        }
    }

    /// <summary>
    /// Result of <see cref="MirrorCitySystem.ApplyStrikeToTarget"/> — enough for the arrival owner
    /// (<c>EnemyOperationEffectSystem</c>) to publish the strike/axis events and log the outcome without
    /// re-reading the model. Pure data.
    /// </summary>
    public struct MirrorStrikeResult
    {
        /// <summary>False when there was no generated city / no target to resolve — the caller drops the signal.</summary>
        public bool Applied;

        /// <summary>True when the enemy's positional air defence intercepted the strike (no hp/axis change).</summary>
        public bool Intercepted;

        /// <summary>
        /// True when the strike landed but changed nothing — it resolved onto the axis's invulnerable
        /// reserve (the axis is stripped bare). Reported distinctly so the caller can surface "no
        /// effective target" instead of a misleading zero-delta axis change.
        /// </summary>
        public bool NoEffect;

        /// <summary>
        /// The effective bottom of the struck axis: the mirror model's structural floor (reserve
        /// contribution) combined with the balance <c>PressureFloor</c> clamp — the value the stored
        /// axis physically cannot drop below. The arrival owner compares <see cref="NewAxis"/>
        /// against THIS to decide a floor-touch (respite arming), so the floor has one definition.
        /// </summary>
        public float AxisFloor;

        /// <summary>The target the strike actually landed on (after fallback resolution), or <see cref="EnemyTarget.NoTargetId"/>.</summary>
        public ushort ResolvedTargetId;

        /// <summary>The resolved target's tier.</summary>
        public MirrorTargetTier Tier;

        /// <summary>Which axis the strike targeted.</summary>
        public AttackCategory Axis;

        /// <summary>Derived axis value before the strike.</summary>
        public float OldAxis;

        /// <summary>Derived axis value after the strike (== <see cref="OldAxis"/> if intercepted or no-op).</summary>
        public float NewAxis;

        /// <summary>True when the resolved target reached 0 hp on this strike.</summary>
        public bool TargetDestroyed;

        /// <summary>True when a key target was permanently destroyed (axis cap cut applied).</summary>
        public bool KeyPermanentKill;
    }
}
