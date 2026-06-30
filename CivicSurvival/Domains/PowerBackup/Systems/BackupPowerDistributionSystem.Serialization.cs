using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.PowerBackup.Systems
{
    /// <summary>
    /// BackupPowerDistributionSystem - Save/Load serialization (IDefaultSerializable).
    /// No persistent state — seed computed from frameCount.
    /// </summary>
    public partial class BackupPowerDistributionSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_MigrationComplete = false;
            m_LayerTagMigrationComplete = false;
            m_WasDisabledBySettings = false;
            m_NoBackupBuildingKeys.Clear();
            m_LinkMap?.Clear();
            // Transient per-scan buffer; cleared here only to satisfy the IResettable contract (CIVIC278).
            m_AssignedBackupScratch.Clear();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                BackupPowerDistributionCodec.Write(
                    new BackupPowerDistributionState(
                        m_MigrationComplete,
                        m_LayerTagMigrationComplete,
                        BuildNoBackupBuildingRefsForSave()),
                    writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(BackupPowerDistributionSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(BackupPowerDistributionSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                BackupPowerDistributionCodec.Read(reader, out var state);
                m_MigrationComplete = state.MigrationComplete;
                m_LayerTagMigrationComplete = state.LayerTagMigrationComplete;
                m_NoBackupBuildingKeys.Clear();
                for (int i = 0; i < state.NoBackupBuildings.Count; i++)
                {
                    var building = state.NoBackupBuildings[i];
                    if (!building.IsNull)
                        m_NoBackupBuildingKeys.Add(building.Packed);
                }
                // Hospital/school migration remains resumable for buildings added after a save.
                // The building → backup link map is a transient post-load projection, never saved;
                // ValidateAfterLoad (IPostLoadValidation) rebuilds it from the BackupPower entities.
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
