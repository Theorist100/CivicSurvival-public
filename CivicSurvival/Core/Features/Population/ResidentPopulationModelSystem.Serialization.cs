using System;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Core.Features.Population
{
    /// <summary>
    /// ResidentPopulationModelSystem - Save/Load serialization (IDefaultSerializable).
    ///
    /// Persists only the cheap scalar result (the five population counts) plus the
    /// pending-day cursor through the canonical SerializationGuard block. The
    /// transient selection (eligibility set, live-citizen counts, snapshot buffers)
    /// is never persisted: it is rebuilt after load. CS2 reuses system instances
    /// across in-game load, so every [NonSerialized] transient latch is reset
    /// unconditionally on the load path before scalar is restored — otherwise a
    /// stale m_HasScheduledResult / dependency handle from the previous city would
    /// drive PublishCompletedScheduledResult into Complete() on a dead handle.
    /// </summary>
    public sealed partial class ResidentPopulationModelSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        public void ResetToBootDefaults(ResetReason reason) => ResetTransientLoadState();

        /// <summary>
        /// IResettable reset (new game). Field-only transient reset plus the readiness
        /// baseline. Native containers are reused for the system lifetime (allocated in
        /// OnCreate) and only cleared here, never disposed.
        /// </summary>
        private void ResetState() => ResetTransientLoadState();

        /// <summary>
        /// Unconditional reset of every [NonSerialized] transient latch and derived
        /// container to its boot/empty state. Reachable from ResetToBootDefaults, so it
        /// must stay field-only (no job Complete(), no EntityManager/ECB/service/registry
        /// work): a stale dependency handle from the previous city is dropped by
        /// assigning default, never by completing it.
        /// </summary>
        private void ResetTransientLoadState()
        {
            // Scheduling latches: a reused instance must not believe a job from the
            // previous city is still pending.
            m_HasScheduledResult = false;
            m_WasReset = false;

            // Dependency handles: drop the previous city's handles by assignment. They
            // are NOT completed here — completing a stale handle on the load path is
            // both unsafe and banned in the boot-reset closure.
            m_ModelWriteDependencies = default;
            m_PopulationDataReadDependencies = default;
            m_EligibilityReadDependencies = default;

            // Load-seed attribution flag for the [POP-READY] contour.
            m_InLoadSeed = false;

            // Derived native containers reused for the system lifetime: clear contents
            // (do not dispose) so the rebuild starts from an empty accumulator/selection.
            m_Accumulator.Clear();
            m_EligibilityA.Clear();
            m_EligibilityB.Clear();
            m_LiveCounts.Clear();
            m_PublishA = true;

            // Selection ring: every slot returns to Free and its lists are cleared —
            // field-only (no job Complete; an in-flight flatten from the previous city is
            // already finished by the engine's load purge because its handle sits in
            // Dependency). The ring stays the permanent owner of the pairs; no Dispose
            // here (only OnDestroy).
            for (int i = 0; i < m_SelectionSlots.Length; i++)
            {
                m_SelectionSlots[i].State = SelectionSlotState.Free;
                m_SelectionSlots[i].BorrowVersion = 0;
                if (m_SelectionSlots[i].Households.IsCreated)
                    m_SelectionSlots[i].Households.Clear();
                if (m_SelectionSlots[i].LiveCitizens.IsCreated)
                    m_SelectionSlots[i].LiveCitizens.Clear();
            }
            m_StagingSlot = SELECTION_SLOT_NONE;

            // Derived snapshots BORROW their selection pair from the ring (no ownership,
            // nothing to dispose — the slot reset above already reclaimed every borrow).
            // Resetting to empty is deliberately NOT a publish: it clears the previous
            // city's state; the restore publish (Deserialize) or the post-load seed
            // republishes with a version bump.
#pragma warning disable CIVIC458 // Boot/empty reset of derived snapshots; republished by the restore publish or post-load seed, not here.
            m_HouseholdSnapshot = default;
            m_PreviousHouseholdSnapshot = default;
            m_PopulationSnapshot = ResidentPopulationSnapshot.Empty;
#pragma warning restore CIVIC458

            // Producer version cursors return to the boot baseline so the first publish
            // after load bumps them and observers see a change.
            m_HouseholdVersion = 0;
            m_PopulationVersion = 0;

            // Pending-day backlog is owned solely by the model; assignment (not +=)
            // would already be implied here, but reset is the boot baseline.
            m_PendingDayChanges = 0;

            SetReadiness(ResidentPopulationReadiness.NotReady, "reset");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                // Always write the fixed scalar set regardless of readiness — there is
                // no empty-Serialize (the known save-crash class). A not-ready model
                // simply serializes its current (possibly zero) scalar; readiness is
                // resolved on Deserialize, never by skipping the write.
                var state = new ResidentPopulationPersistState(
                    m_PopulationSnapshot.AliveResidentCitizens,
                    m_PopulationSnapshot.EligibleHouseholdCount,
                    m_PopulationSnapshot.HomelessHouseholdCount,
                    m_PopulationSnapshot.MovedInHouseholdCount,
                    m_PendingDayChanges);
                ResidentPopulationCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ResidentPopulationModelSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ResidentPopulationModelSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                // Unconditionally reset every transient latch first (instance reuse):
                // this also drops the previous city's pending count to zero so the
                // restore below is an assignment, never an accumulation.
                ResetTransientLoadState();

                ResidentPopulationCodec.Read(reader, out var state);

                // Restore the pending-day backlog by assignment (single owner, A7).
                m_PendingDayChanges = state.PendingDayChanges;

                // Publish the restored scalar so consumers reading after load see the
                // real counts, not a cold zero. PublishSnapshots bumps both version
                // cursors and republishes the (empty) selection alongside the scalar;
                // selection is rebuilt by the post-load seed and reaches SelectionReady
                // on its own. PendingDayChanges is read into the household snapshot, so
                // it must be set before this call.
                var restored = new ResidentPopulationData
                {
                    AliveResidentCitizens = state.AliveResidentCitizens,
                    EligibleHouseholdCount = state.EligibleHouseholdCount,
                    HomelessHouseholdCount = state.HomelessHouseholdCount,
                    MovedInHouseholdCount = state.MovedInHouseholdCount,
                };
#pragma warning disable CIVIC459 // PublishSnapshots bumps both explicit version cursors for the restored scalar; the pending-day write feeds the same publish.
                PublishSnapshots(restored, SELECTION_SLOT_NONE);
#pragma warning restore CIVIC459

                // Scalar is valid after load; selection still rebuilds asynchronously
                // (Phase 4) / through the blocking seed (current). Monotonic: never
                // downgrade below ScalarReady here.
                SetReadiness(ResidentPopulationReadiness.ScalarReady, "load");

                Log.Info($"Deserialized v{version}: citizens={m_PopulationSnapshot.AliveResidentCitizens}, eligibleHouseholds={m_PopulationSnapshot.EligibleHouseholdCount}, pendingDays={m_PendingDayChanges}");
            }
            catch (Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
