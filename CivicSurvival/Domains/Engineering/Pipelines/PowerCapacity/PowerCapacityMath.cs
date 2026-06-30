using System;
using Game.Buildings;
using Game.Net;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    internal static class PowerCapacityMath
    {
        internal const int KW_ROUND_HALF = 500;

        /// <summary>
        /// Fraction of a plant's construction delta that is served from groundbreaking, before the
        /// ramp adds the rest. A plant under construction therefore starts at this share of its
        /// target output instead of zero, so a newly-placed plant gives the city some power
        /// immediately rather than leaving it blacked out for the whole build window. The remaining
        /// (1 − fraction) is delivered linearly over the build window and reaches the full nameplate
        /// exactly at completion. Applies to the delta only: an upgrade keeps its already-served base
        /// and starts the +delta at this fraction.
        /// </summary>
        internal const float ConstructionMinOnlineFraction = 0.2f;

        /// <summary>
        /// Outside-connection capacity = nameplate clamped to the active import cap.
        /// Outside connections carry no structural state (collapse / construction /
        /// repair) and no damage percentages, so the result is simply
        /// <paramref name="originalCapacity"/> bounded by the published runtime import
        /// cap (or the per-connection <c>ImportCapLimitKW</c> fallback before the first
        /// publication). Grid-plant construction / collapse / repair / damage knockouts
        /// are handled separately by <see cref="ComputeEffectiveFactor"/>.
        /// The published cap arrives as explicit parameters because the caller is the
        /// Burst-compiled <c>PlantResolveJob</c> — <c>ImportCapRuntimeState</c> is a managed
        /// static, mirrored into the job input at schedule time.
        /// </summary>
        public static int CalculateOutsideConnectionCapacity(
            int originalCapacity,
            in CapacityModifierState state,
            bool hasPublishedImportCap,
            int publishedImportCapKW)
        {
            int capacity = originalCapacity;

            bool shouldApplyImportCap = hasPublishedImportCap;
            int importCapKW = shouldApplyImportCap ? publishedImportCapKW : 0;
            if (!shouldApplyImportCap && state.HasImportCapLimit)
            {
                importCapKW = state.ImportCapLimitKW;
                shouldApplyImportCap = true;
            }

            if (shouldApplyImportCap && capacity > importCapKW)
                capacity = importCapKW;

            return math.max(0, capacity);
        }

        /// <summary>
        /// Damage multiplier applied on top of vanilla's runtime-adjusted producer
        /// capacity (efficiency × weather × resource × upgrades). Returns 1.0 when no
        /// damage; 0.0 when fully damaged. Structural knockouts (collapsed,
        /// under-construction, under-repair) are handled separately by the caller —
        /// this method only folds the three percentage-based damage sources.
        /// </summary>
        public static float GetDamageMultiplier(CapacityModifierState state)
        {
            return math.max(0f, 1f
                - state.OperationalDamagePercent
                - state.DisasterDamagePercent
                - state.ExplosionDamagePercent);
        }

        /// <summary>
        /// A standing ruin: knocked out by grid collapse, an active repair window, or
        /// accumulated damage (explosion + missiles + disaster) that already zeroes the
        /// damage multiplier. Knocked-out nameplate is EXCLUDED from the built-surplus
        /// aggregates (Фаза-1 ratio, Фаза-7 strike axis, N+1 unit buffer): a ruin is not a
        /// hidden over-build, and counting it would make every successful enemy strike RAISE
        /// the survivors' surplus surcharge. Degradation (SaturationModifier) does NOT make a
        /// plant knocked-out — that cut is reversible and self-inflicted, and ignoring it is
        /// exactly the anti-hiding invariant of the surplus base.
        /// </summary>
        public static bool IsKnockedOut(in CapacityModifierState state)
            => state.IsCollapsed
               || state.IsUnderRepair
               || GetDamageMultiplier(state) <= 0f;

        /// <summary>
        /// The full effective-efficiency fold for ONE grid plant: the four-multiplier chain
        /// <c>damage × construction × saturation × fuel</c>, clamped to [0,1]. A collapsed or
        /// under-repair plant short-circuits to 0 (a standing ruin produces nothing). The forecast
        /// layer (<c>PowerForecast.EffectiveProduction</c>) is a DELIBERATE subset of this chain —
        /// it passes damage = construction = 1 and folds only saturation × fuel — so the runtime
        /// and forecast share the same two leaf curves while this single source remains the only
        /// place the structural (damage/construction/knockout) terms are applied.
        ///
        /// Vanilla folds this into producer capacity via the Efficiency buffer, so we no longer
        /// multiply a captured baseline — the factor is the lever. The three percentage damage
        /// sources fold in subtractively (1 − operational − disaster − explosion, clamped ≥ 0) via
        /// <see cref="GetDamageMultiplier"/>, then the construction ramp multiplies that result.
        ///
        /// During an upgrade-delta window only the added capacity ramps: the pre-upgrade base
        /// (<see cref="CapacityModifierState.BaseConstructionCapacityKW"/>) produces full MW the
        /// whole time. The construction term is therefore expressed as an effective factor against
        /// the full nameplate, (base + delta·progress) / nameplate, so the existing single-scalar
        /// Efficiency-fold and snapshot math stay intact. When base = 0 (new plant / legacy save)
        /// this collapses to plain progress.
        /// </summary>
        public static float ComputeEffectiveFactor(CapacityModifierState state, int originalCapacity)
        {
            if (state.IsCollapsed || state.IsUnderRepair)
                return 0f;

            float damageMultiplier = GetDamageMultiplier(state);
            float constructionMultiplier;
            if (state.IsUnderConstruction)
            {
                // The served capacity is built from the sidecar (base + delta·progress), where the
                // delta runs up to the sidecar's OWN target nameplate — not PlantBaseCapacity. The
                // index system re-publishes PlantBaseCapacity on its own tick, so during the first
                // ticks of an upgrade the two nameplates disagree; deriving the delta from the lagged
                // PlantBaseCapacity would mis-scale the ramp. Fall back to PlantBaseCapacity only when
                // the sidecar carried no target (legacy save / not-yet-mirrored). The factor is then
                // served / originalCapacity so the downstream round(originalCapacity × factor) lands
                // exactly on the absolute served kW regardless of the target/PlantBaseCapacity skew.
                int target = state.ConstructionTargetNameplateKW > 0
                    ? state.ConstructionTargetNameplateKW
                    : originalCapacity;
                int baseKW = state.BaseConstructionCapacityKW;
                int deltaKW = math.max(0, target - baseKW);
                float progress = math.clamp(state.ConstructionProgress, 0f, 1f);
                // The delta is online at ConstructionMinOnlineFraction from groundbreaking and ramps
                // the rest linearly to the full nameplate at completion — see the const's docstring.
                // ConstructionProgress itself stays the honest 0→1 build indicator for the UI; only
                // the served power carries the floor.
                float deltaFraction = ConstructionMinOnlineFraction
                    + (1f - ConstructionMinOnlineFraction) * progress;
                float served = baseKW + deltaKW * deltaFraction;
                constructionMultiplier = originalCapacity > 0 ? served / originalCapacity : 1f;
            }
            else
            {
                constructionMultiplier = 1f;
            }
            // Third multiplier: surplus saturation (effective factor with asymmetric inertia,
            // hydrated per-plant by ApplySaturationInertia). 1 when the component is absent /
            // feature off, so this is a no-op until the saturation pass runs.
            // Fourth multiplier: fuel-stockpile sigmoid (Фаза 2) for thermal plants — read on the
            // fly inside PlantResolveJob from ResourceConsumer.m_ResourceAvailability (see
            // CapacityModifierState.WithFuel). 1 for non-thermal / no ResourceConsumer /
            // feature off (no-op).
            return math.clamp(
                damageMultiplier * constructionMultiplier * state.SaturationFactor * state.FuelFactor,
                0f, 1f);
        }

        /// <summary>
        /// Linear construction ramp: 0 at start, 1 at completion. <c>TotalDays &lt;= 0</c> (e.g. a
        /// legacy save written before the field existed) is treated as complete so a plant never
        /// sticks at zero capacity. Forecast omits construction entirely (see the
        /// <c>// FORECAST-APPROX:</c> note in <c>PowerForecast.EffectiveProduction</c>), so this
        /// ramp is currently a runtime-only term, lifted to Core ahead of any second consumer.
        /// </summary>
        public static float ComputeConstructionProgress(in UnderConstruction uc, float currentDay)
        {
            if (uc.TotalDays <= 0)
                return 1f;
            float progress = 1f - ((uc.CompletionDay - currentDay) / uc.TotalDays);
            return math.clamp(progress, 0f, 1f);
        }

        public static bool IsVariableGeneration(ref PowerCapacityPipelineContext ctx, Entity entity)
        {
            if (!ctx.PrefabRefLookup.TryGetComponent(entity, out var prefabRef))
                return false;
            Entity prefab = prefabRef.m_Prefab;
            return ctx.WindPoweredDataLookup.HasComponent(prefab) || ctx.SolarPoweredDataLookup.HasComponent(prefab);
        }

        public static FlowEdgeUpdateResult TryUpdateFlowEdgeViaEcb(
            ref PowerCapacityPipelineContext ctx,
            Entity plantEntity,
            int capacity)
        {
            if (!ctx.ConnectionLookup.TryGetComponent(plantEntity, out var connection))
                return FlowEdgeUpdateResult.Unresolved;
            if (connection.m_ProducerEdge == Entity.Null)
                return FlowEdgeUpdateResult.Unresolved;
            if (!ctx.FlowEdgeLookup.TryGetComponent(connection.m_ProducerEdge, out var edge))
                return FlowEdgeUpdateResult.Unresolved;
            if (edge.m_Capacity == capacity)
                return FlowEdgeUpdateResult.AlreadyCurrent;

            edge.m_Capacity = capacity;
            ctx.Ecb.SetComponent(connection.m_ProducerEdge, edge);
            return FlowEdgeUpdateResult.Updated;
        }

        /// <summary>
        /// ExportCap: caps the capacity of the EXPORT trade edges (tradeNode → sinkNode).
        /// <paramref name="ownerEntity"/> is the owner of a
        /// <c>Game.Objects.ElectricityOutsideConnection</c> marker (a net entity carrying
        /// <c>ElectricityNodeConnection</c>) — the vanilla lookup route, mirroring
        /// <c>ElectricityOutsideConnectionGraphSystem</c>. Vanilla creates the trade edges
        /// once on Created with kMaxEdgeCapacity and never rewrites their capacity, so the
        /// <c>m_Capacity == cap</c> comparison makes this call idempotent and catches a
        /// recreated edge. ALL matching edges of the node are capped, not just the first:
        /// vanilla does not deduplicate edge pairs — a marker recreated while its node
        /// survives leaves a second trade edge at max capacity in the buffer, and an early
        /// return would leave it as an unlimited export channel.
        /// </summary>
        public static FlowEdgeUpdateResult TryUpdateExportEdgeViaEcb(
            ref PowerCapacityPipelineContext ctx,
            Entity ownerEntity,
            int exportCapKW)
        {
            if (ctx.ElectricitySinkNode == Entity.Null)
                return FlowEdgeUpdateResult.Unresolved;
            if (!ctx.NodeConnectionLookup.TryGetComponent(ownerEntity, out var nodeConnection))
                return FlowEdgeUpdateResult.Unresolved;
            if (!ctx.FlowConnectionLookup.TryGetBuffer(nodeConnection.m_ElectricityNode, out var connectedEdges))
                return FlowEdgeUpdateResult.Unresolved;

            var result = FlowEdgeUpdateResult.Unresolved;
            for (int i = 0; i < connectedEdges.Length; i++)
            {
                Entity edgeEntity = connectedEdges[i].m_Edge;
                if (!ctx.FlowEdgeLookup.TryGetComponent(edgeEntity, out var edge))
                    continue;
                if (edge.m_Start != nodeConnection.m_ElectricityNode
                    || edge.m_End != ctx.ElectricitySinkNode)
                    continue;

                if (edge.m_Capacity == exportCapKW)
                {
                    // Updated dominates AlreadyCurrent in the aggregate result.
                    if (result == FlowEdgeUpdateResult.Unresolved)
                        result = FlowEdgeUpdateResult.AlreadyCurrent;
                    continue;
                }

                edge.m_Capacity = exportCapKW;
                ctx.Ecb.SetComponent(edgeEntity, edge);
                result = FlowEdgeUpdateResult.Updated;
            }

            return result;
        }

        /// <summary>
        /// Reconstruct an <see cref="Entity"/> handle from a packed
        /// <c>BuildingIdentityKey</c> long. The returned handle carries only
        /// <c>Index</c>/<c>Version</c> — chunk pointer / archetype metadata
        /// are not part of <c>Entity</c>'s public surface and are not needed
        /// for the sole consumer, <see cref="EntityStorageInfoLookup.Exists"/>,
        /// which validates liveness by Index+Version against the
        /// <c>EntityComponentStore</c>. Do not pass the reconstructed handle
        /// to <c>ComponentLookup&lt;T&gt;</c> readers without going through
        /// <c>Exists</c> first; once <c>Exists == true</c> the handle is
        /// equivalent to one obtained from a query for all ECS purposes.
        /// </summary>
        public static Entity UnpackBuildingIdentityKey(long key)
            => new()
            {
                Index = (int)(key >> 32),
                Version = unchecked((int)(key & uint.MaxValue))
            };
    }
}
