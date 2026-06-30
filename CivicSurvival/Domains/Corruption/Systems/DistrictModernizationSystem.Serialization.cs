using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Corruption.Systems.Modernization;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// DistrictModernizationSystem - Save/Load serialization (IDefaultSerializable).
    /// Programs live in the Store, cleanup queues live in the CleanupService, the
    /// cooldown anchor lives in the Processor — this partial assembles them through
    /// the existing <see cref="DistrictModernizationCodec"/> value object and resets
    /// system + sub-service state in lockstep. In-flight modernization terminality
    /// is serialized on DistrictModernizationIntent and ModernizationInstallReceipt
    /// entities, not as a completed program snapshot here.
    /// </summary>
    public partial class DistrictModernizationSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetBootDefaultsFields();
            Log.Info($"[BOOT-RESET] system={nameof(DistrictModernizationSystem)} reason={reason}");
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            ResetBootDefaultsFields();
            m_Store.Publish();
        }

        private void ResetBootDefaultsFields()
        {
            m_Store.Reset();
            m_Cleanup.Reset();
            m_Policy.Reset();
            m_Installer.Reset();
            m_Processor.Reset();
            m_LastBuildingOrderVersion = 0;
            m_KnownDistrictsCache.Clear();
            m_KnownDistrictsCacheVersion = -1;
            m_KnownDistrictsSnapshotSource = System.Array.Empty<int>();
            m_DistrictsDirty = false;
            m_DayDedup.Reset();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var programs = m_Store.SnapshotForSave();
                var cleanupDistricts = m_Cleanup.SnapshotPendingDistricts();

                var state = new DistrictModernizationPersistState(
                    m_Processor.LastProcurementDay,
                    m_DayDedup.LastProcessedDay,
                    programs,
                    cleanupDistricts);
                DistrictModernizationCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(DistrictModernizationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                // LOAD-INVARIANT: processor latch/scratch reset before durable codec state is applied.
                ResetBootDefaultsFields();

                DistrictModernizationCodec.Read(reader, ModernizationProcurementProcessor.INITIAL_PROCUREMENT_DAY, out var state);
                ApplyPersistState(state);
                m_LastBuildingOrderVersion = m_BuildingsWithDistrictQuery.GetCombinedComponentOrderVersion(true);
                m_DistrictsDirty = true;

                // CIVIC248: publishing immediately after Restore is correct here —
                // consumers only observe through VersionedView on the next frame, and
                // we need the snapshot to reflect loaded state before any consumer
                // ticks. Save/load is a single-shot event, not a per-frame mutation.
#pragma warning disable CIVIC248
                m_Store.Publish();
#pragma warning restore CIVIC248
                Log.Info($"Deserialized v{version}: {m_Store.Count} programs, lastProcurement={m_Processor.LastProcurementDay}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private void ApplyPersistState(in DistrictModernizationPersistState state)
        {
            m_Processor.LastProcurementDay = state.LastProcurementDay;
            m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);

            m_Store.RestoreFromSave(state.Programs);
            // Building keys are not restored from save (derived state); they are rebuilt
            // from live components in ValidateAfterLoad after entity remap completes.
            m_Cleanup.RestoreFromSave(state.PendingCleanupDistricts);
        }

        private void ClampLastProcessedDayAfterLoad()
        {
            // ORDER-INVARIANT: sibling Deserialize order does not guarantee that GameTimeSystem
            // has restored its snapshot. Clamp only in PLVS after GameTime activation.
            m_DayDedup = PostLoadDayClamp.ClampDedupToActivatedGameDay(m_DayDedup, Log, nameof(DistrictModernizationSystem));
        }
    }
}
