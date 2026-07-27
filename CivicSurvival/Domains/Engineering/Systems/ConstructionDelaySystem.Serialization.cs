using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Engineering.Systems
{
    public partial class ConstructionDelaySystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        /// <summary>
        /// Reset serializable state to defaults.
        /// Called on new game and when save version is incompatible.
        /// </summary>
        public void ResetState()
        {
            m_CurrentGameDay = 0f;
            m_InitialBatchRecorded = false;
            m_InitialBatchZeroScanSeen = false;
            m_InitialBatchZeroScanDay = 0f;
            if (m_PlantRegistry.IsCreated)
                m_PlantRegistry.Clear();
            if (m_ConstructionByBuilding.IsCreated)
                m_ConstructionByBuilding.Clear();
            // Transient upgrade-delta last-seen map. CS2 reuses the system instance across an
            // in-session load, so stale Index→cap entries from a prior city would mis-baseline
            // upgrades — clear it (re-seeded post-load on the first scan, seed-without-acting).
            m_LastKnownNameplate?.Clear();
            Log.Info($"[{nameof(ConstructionDelaySystem)}] ResetState: Reset to fresh state");
        }

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                ConstructionDelayCodec.Write(writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(ConstructionDelaySystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(ConstructionDelaySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ConstructionDelayCodec.Read(reader);

                // FIX S4-04: Clear stale registry entries from previous session — forces fresh scan after load
                if (m_PlantRegistry.IsCreated)
                    m_PlantRegistry.Clear();
                // Clear the transient upgrade-delta last-seen map for the same reason the registry is
                // cleared: CS2 reuses the system instance across an in-session load, so stale entries
                // would mis-baseline upgrades. Re-seeded on the first post-load scan (seed-without-acting),
                // so a surviving mid-window sidecar keeps ramping and is not re-detected as a fresh upgrade.
                m_LastKnownNameplate?.Clear();
                m_InitialBatchRecorded = false;
                m_InitialBatchZeroScanSeen = false;
                m_InitialBatchZeroScanDay = 0f;

                Log.Info($"[{nameof(ConstructionDelaySystem)}] Deserialized v{version}");
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
    }
}
