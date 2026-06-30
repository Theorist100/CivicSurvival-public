using Game.Buildings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Logic;
using CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity;
#if ENABLE_BURST
using Unity.Burst;
#endif

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// PowerCapacityResolverSystem — the scheduled per-plant resolve (frame N → N+1, Axiom 9).
    ///
    /// The job is the ONLY place the resolver touches the three vanilla components the old
    /// main-thread path had to drain fences for (<c>ElectricityProducer</c> RW,
    /// <c>Efficiency</c> buffer RW, <c>ResourceConsumer</c> RO). Ordering against vanilla
    /// readers/writers comes from scheduling on <c>Dependency</c> instead of
    /// <c>CompleteDependencyBefore*</c> on the main thread — the 2026-06-12 SP:PCR.* split
    /// measured those drains at 78% of the system's 13 ms/throttled-tick cost
    /// (FenceEfficiency alone 7.99 ms/call).
    ///
    /// Everything mod-owned (modifier sidecars, prefab classification, config) is snapshotted
    /// on the main thread into <see cref="PlantWork"/> BEFORE scheduling, so the job never
    /// races mod main-thread writers and never needs managed state (PrefabSystem,
    /// BalanceConfig, ImportCapRuntimeState, LogContext).
    /// </summary>
    public partial class PowerCapacityResolverSystem
    {
        /// <summary>Per-plant input snapshot, built main-thread at schedule time.</summary>
        internal struct PlantWork
        {
            public Entity Entity;
            public CapacityChannel Channel;
            public int OriginalCapacityKW;
            public PlantKind Kind;
            /// <summary>Mod-modifier slice; fuel pair left at defaults (1,1) — the job fills it
            /// from the vanilla <c>ResourceConsumer</c> read.</summary>
            public CapacityModifierState State;
            /// <summary>Prefab carries WindPoweredData/SolarPoweredData — the renewables
            /// dispatchable clamp applies (precomputed: prefab data is static).</summary>
            public bool IsVariableGeneration;
            /// <summary>1 &lt;&lt; (int)PlantType for Wind/Solar, 0 otherwise. Precomputed
            /// main-thread (PlantType classification needs the managed PrefabSystem).</summary>
            public int IntermittentTypeBit;
        }

        /// <summary>
        /// Flow-edge decision produced by the job for the OutsideConnection / no-buffer
        /// fallback plants, applied via ECB on the NEXT throttled tick's main-thread consume
        /// (the ECB is deliberately not filled from the job — registering the job with
        /// GameSimulationEndBarrier would make the barrier force-complete the vanilla
        /// Efficiency chain). <see cref="PowerCapacityMath.TryUpdateFlowEdgeViaEcb"/> re-checks
        /// the live edge at apply time, so the one-tick-old target stays idempotent.
        /// </summary>
        internal struct PendingFlowEdgeWrite
        {
            public Entity Plant;
            public int CapacityKW;
            public bool CapacityChanged;
        }

        /// <summary>Scalar outputs of one resolve pass, consumed on the next throttled tick.</summary>
        internal struct ResolveAggregates
        {
            public long DispatchableSumKW;
            public long CityDispatchableSumMW;
            public long NameplateSumKW;
            public int LargestPlantKW;
            public int IntermittentTypeMask;
            public int ResolvedCount;
            public int SnapshotCount;
            /// <summary>Fleet target captured at schedule time so the published snapshot stays
            /// internally consistent with the per-plant RecoveryHours computed from it.</summary>
            public float FleetTargetFactor;
            public bool CollectedLossBreakdown;
            /// <summary>Never-fires detector relocated from the old main-thread loop (no managed
            /// logging inside Burst): first grid producer that hit the direct-write fallback
            /// without an Efficiency buffer; Entity.Null when the dead path stayed dead.</summary>
            public Entity NoBufferWarnEntity;
            // Loss-breakdown aggregates (debug-gated; see [CapacityLoss] log line).
            public long KnockedOutKW;
            public long DamageLossKW;
            public long SatLossKW;
            public long FuelLossKW;
            public long AllowedKW;
            public long DeliveredKW;
            public long HealthyKW;
            public double BoostWeighted;
        }

        /// <summary>BalanceConfig.FuelCurve unmanaged mirror (BalanceConfig is managed).</summary>
        internal struct FuelCurveJobParams
        {
            public bool Enabled;
            public float BufferThreshold;
            public float MinOutputAtZero;
            public float AnchorFrac;
            public float AnchorOutput;
            public float SteepnessLow;
            public float SteepnessHigh;
        }

        /// <summary>BalanceConfig.GenerationSaturation slice for EstimateRecoveryHours.</summary>
        internal struct SaturationJobParams
        {
            public bool Enabled;
            public float Hysteresis;
            public float TauUpHours;
        }

        /// <summary>ImportCapRuntimeState unmanaged mirror (the runtime state is a managed static).</summary>
        internal struct ImportCapJobParams
        {
            public bool HasPublishedImportCap;
            public int CurrentImportCapKW;
        }

#if ENABLE_BURST
        [BurstCompile]
#endif
        private struct PlantResolveJob : IJob
        {
            [ReadOnly] public NativeList<PlantWork> Work;

            // The exact component set the retired VanillaWriteBarrier masks guarded — now
            // ordered by the job graph instead of main-thread fence drains.
            public ComponentLookup<ElectricityProducer> ProducerLookup;
            public BufferLookup<Efficiency> EfficiencyLookup;
            [ReadOnly] public ComponentLookup<Game.Buildings.ResourceConsumer> ResourceConsumerLookup;

            public bool AfterLoad;
            public bool IsSafeFrame;
            public bool CollectLossBreakdown;
            public float FleetTargetFactor;
            public FuelCurveJobParams FuelCurve;
            public SaturationJobParams Saturation;
            public ImportCapJobParams ImportCap;

            public NativeList<PowerCapacityPlantSnapshot> Snapshots;
            public NativeList<PendingFlowEdgeWrite> PendingEdgeWrites;
            public NativeReference<ResolveAggregates> Aggregates;

            public void Execute()
            {
                var agg = new ResolveAggregates
                {
                    FleetTargetFactor = FleetTargetFactor,
                    CollectedLossBreakdown = CollectLossBreakdown,
                    NoBufferWarnEntity = Entity.Null,
                };

                for (int i = 0; i < Work.Length; i++)
                {
                    PlantWork w = Work[i];
                    // Snapshot-to-execution liveness guard: structural changes are main-thread
                    // sync points that complete this job first, so a missing producer means the
                    // plant was destroyed between the work-list build and the schedule.
                    if (!ProducerLookup.TryGetComponent(w.Entity, out var producer))
                        continue;

                    CapacityModifierState state = w.State;
                    // Сырьё-сигмоида (Фаза 2): топливный множитель применяется ТОЛЬКО к Thermal-
                    // станциям с компонентом ResourceConsumer (Coal/Gas/Generic-thermal). У
                    // возобновляемых/гидро/гео компонента нет → fuelFraction=1 → fuelFactor=1.
                    // Сырьё неинерционно: читается на лету, не persisted (мгновенная физика котла).
                    // Гонка с CityServiceUpkeepSystem.UpkeepJob закрыта job-графом (RO lookup).
                    if (w.Kind == PlantKind.Thermal
                        && ResourceConsumerLookup.TryGetComponent(w.Entity, out var resourceConsumer))
                    {
                        float fuelFraction = resourceConsumer.m_ResourceAvailability / FuelCurveLogic.ResourceAvailabilityMax;
                        float fuelFactor = FuelCurveLogic.ComputeFuelFactor(
                            fuelFraction, FuelCurve.Enabled,
                            FuelCurve.BufferThreshold, FuelCurve.MinOutputAtZero,
                            FuelCurve.AnchorFrac, FuelCurve.AnchorOutput,
                            FuelCurve.SteepnessLow, FuelCurve.SteepnessHigh);
                        state = state.WithFuel(fuelFactor, fuelFraction);
                    }

                    bool isGridProducer = w.Channel == CapacityChannel.GridProducer;

                    // Built-and-in-grid nameplate only: a plant still under construction-delay serves
                    // only a fraction of its nameplate (the ramp starts at ConstructionMinOnlineFraction
                    // and climbs to full at completion), so its FULL nameplate must NOT inflate the
                    // surplus ratio or the Фаза-7 "built surplus in grid" aggregate. Knocked-out
                    // plants are ruins, not hidden over-build (PowerCapacityMath.IsKnockedOut).
                    if (isGridProducer
                        && !state.IsUnderConstruction
                        && !PowerCapacityMath.IsKnockedOut(state))
                    {
                        agg.NameplateSumKW += w.OriginalCapacityKW;
                        agg.LargestPlantKW = math.max(agg.LargestPlantKW, w.OriginalCapacityKW);
                        agg.IntermittentTypeMask |= w.IntermittentTypeBit;
                    }

                    if (CollectLossBreakdown && isGridProducer && !state.IsUnderConstruction)
                        CollectLossBreakdownEntry(w, producer, state, ref agg);

                    ResolvePlantEntry(w, producer, state, ref agg);
                }

                agg.SnapshotCount = Snapshots.Length;
                Aggregates.Value = agg;
            }

            /// <summary>
            /// Sequential decomposition of the same multiplier chain PowerCapacityMath.ComputeEffectiveFactor
            /// applies (damage × saturation × fuel): each term's loss is taken from what the
            /// previous terms left, so the four buckets sum exactly to the nameplate.
            /// </summary>
            private void CollectLossBreakdownEntry(
                in PlantWork w,
                in ElectricityProducer producer,
                in CapacityModifierState state,
                ref ResolveAggregates agg)
            {
                if (PowerCapacityMath.IsKnockedOut(state))
                {
                    agg.KnockedOutKW += w.OriginalCapacityKW;
                }
                else
                {
                    float dmg = PowerCapacityMath.GetDamageMultiplier(state);
                    float sat = state.SaturationFactor;
                    float fuel = state.FuelFactor;
                    long np = w.OriginalCapacityKW;
                    agg.DamageLossKW += (long)math.round(np * (1f - dmg));
                    agg.SatLossKW += (long)math.round(np * dmg * (1f - sat));
                    agg.FuelLossKW += (long)math.round(np * dmg * sat * (1f - fuel));
                    agg.AllowedKW += (long)math.round(np * dmg * sat * fuel);
                    // Nameplate-weighted foreign-boost aggregate over the healthy fleet —
                    // surfaces in the [CapacityLoss] line how much the slot-26 compensation
                    // is dividing away (1.00 = no foreign boost anywhere).
                    agg.HealthyKW += np;
                    agg.BoostWeighted += np * (double)(EfficiencyLookup.HasBuffer(w.Entity)
                        ? ComputeForeignEfficiencyBoost(EfficiencyLookup[w.Entity])  // ≥ 1 by construction
                        : 1f);
                }
                // Delivered flow over ALL grid producers (ruins deliver ~0). allowed − delivered
                // is what the grid did not take: severed network sections, shed load, weather
                // cuts on intermittents (vanilla folds weather after our Efficiency factor).
                agg.DeliveredKW += producer.m_LastProduction;
            }

            private void ResolvePlantEntry(
                in PlantWork w,
                ElectricityProducer producer,
                in CapacityModifierState state,
                ref ResolveAggregates agg)
            {
                bool isOutsideConnection = w.Channel == CapacityChannel.OutsideConnection;
                bool isEmergencyBattery = w.Channel == CapacityChannel.EmergencyBattery;
                bool isGridProducer = w.Channel == CapacityChannel.GridProducer;

                // factor ∈ [0,1] folds damage and construction (collapse/repair ⇒ 0).
                // effectiveCapacityKW = round(OriginalCapacity × factor) is the synchronous
                // knockout indicator for the snapshot (disaster/wear gates read it).
                // OutsideConnection has no structural/damage state: the kW is the nameplate
                // clamped to the import cap, factor stays 1.
                float factor;
                int effectiveCapacity;
                if (isEmergencyBattery)
                {
                    factor = 1f;
                    effectiveCapacity = producer.m_Capacity;
                }
                else if (isOutsideConnection)
                {
                    factor = 1f;
                    effectiveCapacity = PowerCapacityMath.CalculateOutsideConnectionCapacity(
                        w.OriginalCapacityKW, state, ImportCap.HasPublishedImportCap, ImportCap.CurrentImportCapKW);
                }
                else
                {
                    factor = PowerCapacityMath.ComputeEffectiveFactor(state, w.OriginalCapacityKW);
                    effectiveCapacity = math.max(0, (int)math.round(w.OriginalCapacityKW * factor));
                }

                // Renewables clamp to the last vanilla production on safe frames only; the
                // afterLoad pass uses effectiveCapacity (m_LastProduction may be stale there).
                agg.DispatchableSumKW += AfterLoad || isEmergencyBattery || !IsSafeFrame || !w.IsVariableGeneration
                    ? effectiveCapacity
                    : math.min(effectiveCapacity, producer.m_LastProduction);

                // Weather-accurate current output for UI = the vanilla-written producer capacity.
                // For grid producers the factor path leaves vanilla as the sole m_Capacity writer,
                // so this already reflects damage × weather once vanilla folds in slot 26.
                int currentOutputKW = producer.m_Capacity;

                if (isGridProducer && EfficiencyLookup.HasBuffer(w.Entity))
                {
                    // PERF-LOCK: grid producers with an Efficiency buffer do NOT write
                    // ElectricityProducer.m_Capacity or the producer flow edge directly. The mod
                    // factor is folded into (EfficiencyFactor)26 and vanilla PowerPlantAISystem
                    // computes the reduced capacity itself (single writer). Restoring a direct
                    // m_Capacity / flow-edge write here reintroduces the producer-capacity
                    // oscillation against vanilla (perpetual ProceduralUpload re-dirty).
                    // The write is race-free because this job declares the Efficiency buffer RW
                    // and is scheduled on Dependency — the job graph orders it against every
                    // vanilla Efficiency reader/writer (replaces the retired
                    // CompleteDependencyBeforeRW<Efficiency> main-thread drain).
                    bool factorChanged = WriteEfficiencyFactor(w.Entity, factor);
                    if (factorChanged)
                        agg.ResolvedCount++;
                }
                else if (!isEmergencyBattery)
                {
                    // INVARIANT — DO NOT REMOVE this branch: OutsideConnection has no Efficiency
                    // buffer and is the LIVE consumer of this direct producer.m_Capacity + flow-edge
                    // write (currentOutputKW below depends on it). A grid producer reaches here only
                    // when HasBuffer<Efficiency> is false — impossible for any vanilla plant
                    // (CityServiceBuilding carries the buffer), so the isGridProducer arm is a
                    // deliberate never-fires detector, NOT accidental dead code (surfaced via
                    // NoBufferWarnEntity at consume time — no managed logging inside Burst).
                    bool capacityChanged = producer.m_Capacity != effectiveCapacity;
                    if (capacityChanged)
                    {
                        if (isGridProducer && agg.NoBufferWarnEntity == Entity.Null)
                            agg.NoBufferWarnEntity = w.Entity;

                        producer.m_Capacity = effectiveCapacity;
#pragma warning disable CIVIC035 // Producer came from TryGetComponent at the top of Execute.
                        ProducerLookup[w.Entity] = producer;
#pragma warning restore CIVIC035
                        currentOutputKW = effectiveCapacity;
                        agg.ResolvedCount++;
                    }

                    // Edge decision only — the ECB write happens on the next tick's main-thread
                    // consume (see PendingFlowEdgeWrite); the apply step owns the safe-frame gate
                    // and the m_FlowEdgeDirty retry latch.
                    PendingEdgeWrites.Add(new PendingFlowEdgeWrite
                    {
                        Plant = w.Entity,
                        CapacityKW = effectiveCapacity,
                        CapacityChanged = capacityChanged,
                    });
                }

                // Inertia up-ramp ETA for the Фаза-4 tooltip: hours for this plant's factor to
                // climb to the fleet target. Only meaningful on an up-ramp; 0 otherwise. Grid
                // producers only (others force factor=1, no ramp).
                float recoveryHours = isGridProducer
                    ? EstimateRecoveryHours(state.SaturationFactor, FleetTargetFactor, Saturation)
                    : 0f;

                // City dispatchable potential for the SURPLUS row: GridProducer only; integer
                // division per plant — exactly like INFRA OUTPUT
                // (EquipmentUISystem.ResolveCurrentOutputMW). Ruins contribute 0 on their own
                // (effectiveCapacity = 0), construction — a partial delta.
                if (isGridProducer)
                    agg.CityDispatchableSumMW += math.min(currentOutputKW, effectiveCapacity) / 1000;

                Snapshots.Add(new PowerCapacityPlantSnapshot(
                    w.Entity,
                    w.Kind,
                    w.Channel,
                    w.OriginalCapacityKW,
                    effectiveCapacity,
                    currentOutputKW,
                    state.IsCollapsed,
                    state.IsUnderConstruction,
                    state.IsUnderRepair,
                    state.ExplosionDamagePercent,
                    state.OperationalDamagePercent,
                    state.DisasterDamagePercent,
                    state.SaturationFactor,
                    recoveryHours,
                    state.FuelAvailability,
                    state.FuelFactor));
            }

            /// <summary>
            /// Writes the compensated target value into vanilla <c>(EfficiencyFactor)26</c>
            /// (CityModifierHospitalEfficiency — unused by vanilla on power plants).
            /// Compensates foreign BOOSTS: vanilla multiplies ALL slots, so the target value is
            /// ourFactor / Π max(1, slot_i) — boosts cancel out while foreign PENALTIES (&lt; 1)
            /// stay out of the divisor and keep stacking UNDER allowed. The drifting
            /// EmployeeHappiness boost means a rewrite every 500 ms tick is normal, not an
            /// anomaly. <c>SetEfficiencyFactor</c> is foreign-preserving and idempotent
            /// (target ≈ 1 removes the slot). Returns true when the buffer was mutated so the
            /// resolved counter only reflects real changes.
            /// </summary>
            private bool WriteEfficiencyFactor(Entity entity, float factor)
            {
                if (!EfficiencyLookup.HasBuffer(entity))
                    return false;

                DynamicBuffer<Efficiency> buffer = EfficiencyLookup[entity];
                // boost ≥ 1 by construction (product of max(1, slot)); the max() guard makes the
                // division provably safe rather than relying on the helper's contract from afar.
                float boost = math.max(1f, ComputeForeignEfficiencyBoost(buffer));
                float target = factor / boost;
                float existing = ReadEfficiencyFactor(buffer, ModDamageEfficiencyFactor);
                if (math.abs(existing - target) <= EfficiencyEpsilon)
                    return false;

                BuildingUtils.SetEfficiencyFactor(buffer, ModDamageEfficiencyFactor, target);
                return true;
            }
        }

        /// <summary>
        /// Estimated game-hours for the saturation factor to up-ramp from <paramref name="current"/>
        /// to <paramref name="target"/> under the exponential inertia (factor(t) = target −
        /// (target−current)·exp(−t/Tau)). 0 when no up-ramp is pending (target ≤ current).
        /// Config arrives as an unmanaged slice because the caller is a Burst job
        /// (BalanceConfig.Current is managed).
        /// </summary>
        private static float EstimateRecoveryHours(float current, float target, in SaturationJobParams cfg)
        {
            if (!cfg.Enabled)
                return 0f;
            float gap = target - current;
            if (gap <= cfg.Hysteresis)
                return 0f;
            // t = Tau · ln(gap / band), band = the residual we treat as "arrived" (hysteresis).
            float band = math.max(cfg.Hysteresis, 0.001f);
            float t = cfg.TauUpHours * math.log(gap / band);
            return math.max(0f, t);
        }
    }
}
