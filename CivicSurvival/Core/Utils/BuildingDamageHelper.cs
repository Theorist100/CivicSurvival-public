using Unity.Entities;
using Unity.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Objects;
using CivicSurvival.Core.Components.Domain.ThreatDamage;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using Destroy = Game.Objects.Destroy;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Building damage application utilities.
    /// Encapsulates CS2 vanilla integration (destruction, fire).
    ///
    /// SAME-FRAME DEDUP CONTRACT:
    /// All helpers accept a shared <see cref="IFrameMutationDedup"/> instance
    /// (resolved from <c>ServiceRegistry</c>, frame-cleared by
    /// <c>FrameMutationDedupClearSystem</c>). Per-system <c>NativeHashSet&lt;Entity&gt;</c>
    /// guards are no longer used — sibling systems (PowerBackup, Corruption,
    /// PlantWearSimulation/PlantExplosionService, ThreatDamage) all observe each
    /// other's queued Destroy/Ignite intent inside one sim frame.
    ///
    /// Idempotency layers (unchanged from the pre-Phase-8 shape):
    /// 1. <c>HasComponent&lt;Destroyed/Deleted&gt;</c> — cross-frame guard
    ///    (entity-version safe).
    /// 2. <see cref="IFrameMutationDedup"/> — same-frame dedup across systems.
    /// 3. <c>EntityManager.Exists</c> — entity validity.
    /// </summary>
    public static class BuildingDamageHelper
    {
        /// <summary>
        /// Attempt to destroy a building via CS2 vanilla DestroySystem.
        /// Creates a Destroy event that triggers visual destruction and component removal.
        ///
        /// Uses CS2 DestroySystem pattern:
        /// 1. HasComponent&lt;Destroyed&gt; check FIRST (cross-frame, entity-version safe)
        /// 2. HasComponent&lt;Deleted&gt; check (cross-frame, entity-version safe)
        /// 3. <see cref="IFrameMutationDedup"/> Destroy queue (shared cross-system,
        ///    cleared once per frame by <c>FrameMutationDedupClearSystem</c>).
        ///
        /// NOTE: Creates SEPARATE DestroyedBuildingEvent entity instead of AddComponent on vanilla.
        /// This avoids homeless spike cascade (archetype migration triggers ZoneCheckSystem).
        /// </summary>
        /// <param name="ecb">Command buffer for entity operations</param>
        /// <param name="building">Building entity to destroy</param>
        /// <param name="position">World-space destruction point</param>
        /// <param name="dedup">Shared cross-system frame-mutation dedup service</param>
        /// <param name="destroyedLookup">Lookup for Destroyed component check</param>
        /// <param name="deletedLookup">Lookup for Deleted component check</param>
        /// <param name="destroyEventArchetype">Archetype for Destroy event (Event + Destroy)</param>
        /// <param name="isCritical">Whether building is critical infrastructure</param>
        /// <param name="isPowerPlant">Whether building is a power plant (for cumulative stats split)</param>
        /// <returns>True if destruction was applied, false if already destroyed/pending</returns>
        public static bool TryDestroyBuilding(
            EntityCommandBuffer ecb,
            Entity building,
            float3 position,
            IFrameMutationDedup dedup,
            ComponentLookup<Destroyed> destroyedLookup,
            ComponentLookup<Deleted> deletedLookup,
            EntityArchetype destroyEventArchetype,
            bool isCritical,
            bool isPowerPlant)
        {
            // 1. CROSS-FRAME GUARD: Component checks (entity-version safe)
            //    This catches buildings destroyed in previous frames.
            //    Unlike a frame-local map, component lookup respects entity version.
            if (destroyedLookup.HasComponent(building))
                return false;

            if (deletedLookup.HasComponent(building))
                return false;

            // 2. SAME-FRAME GUARD: shared cross-system dedup map.
            //    Catches duplicate destroy requests in the same frame from any
            //    system (before ECB playback makes Destroyed visible).
            if (!dedup.TryQueueDestroy(building.Index))
                return false;

            // 3. Create DestroyedBuildingEvent on SEPARATE entity (NEVER AddComponent on vanilla!)
            // This avoids homeless spike cascade from archetype migration.
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new DestroyedBuildingEvent
            {
                Building = BuildingRef.FromEntity(building),
                IsCritical = isCritical,
                IsPowerPlant = isPowerPlant,
                Position = position
            });

            // 4. Create Destroy EVENT for CS2 vanilla DestroySystem
            // This triggers proper destruction: visual collapse, Destroyed component,
            // and removal of ElectricityConsumer/Producer/WaterConsumer/etc.
            Entity destroyEvent = ecb.CreateEntity(destroyEventArchetype);
            ecb.SetComponent(destroyEvent, new Destroy(building, Entity.Null));

            return true;
        }

        /// <summary>
        /// Apply mod-origin fire directly via <c>OnFire</c> + <c>BatchesUpdated</c>
        /// without routing through vanilla <see cref="Game.Events.Ignite"/>.
        ///
        /// Mod fires have no <c>JournalEvent</c>-bearing event entity to carry, so
        /// constructing <c>Ignite { m_Event = Entity.Null }</c> would propagate that
        /// null through <see cref="OnFire.m_Event"/> on the building and break
        /// downstream vanilla escalation/spread/icon-attribution.
        ///
        /// The fire is event-backed: <c>ModFireApplySystem</c> builds a real
        /// <c>OnFire.m_Event</c> from the vanilla fire <c>FireData</c>-prefab, so vanilla
        /// <c>FireSimulationSystem</c> drives escalation, spread, structural building damage,
        /// fire-rescue and icons off this fire — exactly as a vanilla-ignited fire would.
        ///
        /// This helper mirrors vanilla <c>Game.Events.IgniteSystem.IgniteFireJob</c>
        /// (decompile lines 62-130) minus the journal-data + intensity-merge race
        /// pieces vanilla needs across many parallel Ignite events. Step semantics:
        ///
        /// <list type="number">
        ///   <item>Destroy short-circuit: if <paramref name="dedup"/> already has a Destroy
        ///     queued for this building this frame, return false — never ignite an entity
        ///     headed for the destroy archetype.</item>
        ///   <item>Cross-frame guard: skip if <paramref name="destroyedLookup"/> or
        ///     <paramref name="deletedLookup"/> already has the building.</item>
        ///   <item>Existing-OnFire gate: when <paramref name="onFireLookup"/> already has
        ///     <c>OnFire</c> and <paramref name="allowExistingFire"/> is false (or the new
        ///     intensity is not strictly greater), return false. The consumer
        ///     (<c>ModFireApplySystem.ApplyFire</c>) preserves an existing fire's real
        ///     <c>m_Event</c> on intensity merge so a vanilla fire is not stomped to
        ///     <c>Entity.Null</c>.</item>
        ///   <item>Same-frame cross-system dedup claim via <c>TryQueueIgnite</c>.</item>
        ///   <item>Create the <see cref="ModFireIntent"/> entity; the consumer performs the
        ///     <c>OnFire</c>+<c>BatchesUpdated</c> structural add and the installed-upgrade
        ///     batch walk in ModificationEnd. Never create a vanilla <c>Ignite</c> entity
        ///     (analyzer <c>CIVIC474</c>).</item>
        /// </list>
        ///
        /// <para><b>This method does NOT perform the OnFire/BatchesUpdated structural add.</b>
        /// It is the PRODUCER half of the mod fire producer/consumer split (mirror of vanilla
        /// <c>FireSimulationSystem</c> creating an <c>Ignite</c> event): it creates a
        /// <see cref="ModFireIntent"/> entity on <paramref name="ecb"/>, and
        /// <c>ModFireApplySystem</c> performs the archetype migration in ModificationEnd — the
        /// phase the vanilla render batch pipeline expects. Doing the structural add directly
        /// from a GameSimulation producer can crash a vanilla Burst batch job on a null chunk
        /// pointer (phase inversion: GameSimulation runs in LateUpdate, after MainLoop's
        /// render pass), and a GameSimulation system cannot even create a ModificationEnd
        /// barrier buffer (its usage gate is closed outside ModificationEnd). See
        /// <see cref="ModFireIntent"/>.</para>
        ///
        /// This helper does NOT damage <see cref="BuildingCondition"/> — once the fire carries a
        /// real <c>m_Event</c>, vanilla <c>FireSimulationSystem</c> drives building damage off
        /// the fire prefab's <c>FireData</c>, so a mod-side condition write would double-count.
        ///
        /// The early guards below still run in the producer so a caller gets an immediate
        /// true/false (fire queued / rejected) for its own bookkeeping; the consumer re-checks
        /// them authoritatively before applying.
        /// </summary>
        /// <param name="ecb">Command buffer from the producer's own phase barrier (e.g.
        /// <c>GameSimulationEndBarrier</c>). Carries only the new <see cref="ModFireIntent"/>
        /// entity — creating a new entity is legal from any phase.</param>
        /// <param name="kind">Target entity class. <c>Building</c> (default) for mod building
        /// fires; <c>WildTree</c> for debris-ignited forest fires — the consumer then backs the
        /// <c>OnFire</c> with the vanilla WildTree fire prefab. The early guards and dedup below
        /// are entity-generic and apply unchanged to either kind.</param>
        public static bool TryApplyModFire(
            EntityCommandBuffer ecb,
            Entity building,
            IFrameMutationDedup dedup,
            ComponentLookup<OnFire> onFireLookup,
            ComponentLookup<Destroyed> destroyedLookup,
            ComponentLookup<Deleted> deletedLookup,
            float intensity = 0.5f,
            bool allowExistingFire = false,
            FireTargetKind kind = FireTargetKind.Building)
        {
            // Step 1 — Destroy-already-queued short-circuit. dedup.TryQueueIgnite below also
            // rejects when Destroy is queued, but checking up-front avoids the wasted
            // Destroyed/Deleted lookups + OnFire query and makes the contract obvious for all
            // callers: never ignite a building headed for the destroy archetype this frame.
            if ((dedup.GetQueuedKind(building.Index) & FrameMutationKind.Destroy) != 0)
                return false;

            // Step 2 — cross-frame guard. Components are entity-version safe and catch
            // buildings destroyed/deleted in prior frames.
            if (destroyedLookup.HasComponent(building))
                return false;
            if (deletedLookup.HasComponent(building))
                return false;

            // Step 3 — existing-OnFire gate. Reject (allowExistingFire off, or intensity not
            // stronger) before claiming the dedup slot so a sibling arriving same-frame with a
            // strictly stronger intensity can still queue. The consumer re-checks OnFire
            // authoritatively; rejecting here just avoids creating a no-op intent.
            if (onFireLookup.HasComponent(building))
            {
                if (!allowExistingFire)
                    return false;
                if (intensity <= onFireLookup[building].m_Intensity)
                    return false;
            }

            // Step 4 — same-frame cross-system dedup claim. Also short-circuits if Destroy is
            // queued for this target this frame. Stays in the producer because the Destroy
            // claim is made synchronously the same GameSimulation frame; by consumer time
            // (frame N+1) the frame-local map is already cleared.
            if (!dedup.TryQueueIgnite(building.Index))
                return false;

            // Step 5 — create the fire intent. ModFireApplySystem builds the real
            // OnFire.m_Event from the vanilla fire prefab and applies OnFire + BatchesUpdated +
            // upgrade walk in ModificationEnd, in phase with the render pass.
            var intentEntity = ecb.CreateEntity();
            ecb.AddComponent(intentEntity, new ModFireIntent
            {
                Target = BuildingRef.FromEntity(building),
                Kind = kind,
                Intensity = intensity,
                AllowExistingFire = allowExistingFire
            });
            return true;
        }

        /// <summary>
        /// Get building position with fallback.
        /// </summary>
        public static float3 GetBuildingPosition(
            Entity building,
            float3 fallbackPosition,
            ComponentLookup<Transform> transformLookup,
            RenderWriteTicket renderTicket)
        {
            EnsureRenderTicket(renderTicket, RenderWriteComponentMask.BuildingTransform);

            if (transformLookup.TryGetComponent(building, out var transform))
                return transform.m_Position;
            return fallbackPosition;
        }

        private static void EnsureRenderTicket(RenderWriteTicket renderTicket, RenderWriteComponentMask requiredMask)
        {
            if (!renderTicket.Covers(requiredMask))
                throw new System.InvalidOperationException($"Render write ticket does not cover {requiredMask}");
        }
    }
}
