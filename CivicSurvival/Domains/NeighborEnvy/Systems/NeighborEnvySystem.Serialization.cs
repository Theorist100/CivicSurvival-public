using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.NeighborEnvy.Systems
{
    public partial class NeighborEnvySystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason)
        {
            FeatureEnabled = true;
            AffectedCount = 0;
            ProcessedCount = 0;
            m_ThrottleCounter = 0;
            m_EnvyData.NeedsFullRebuild = true;
            m_EnvyData.ClearDirtyDistricts();
            m_EnvyData.ClearAll();
            m_PendingBootDefaultRebuildCleanup = true;
        }

        public void ResetState()
        {
            FeatureEnabled = true;
            AffectedCount = 0;
            ProcessedCount = 0;
            m_ThrottleCounter = 0;
            m_EnvyData.NeedsFullRebuild = true;
            m_EnvyData.ClearDirtyDistricts();
            m_EnvyData.ClearAll();
            if (m_PendingRebuild.IsValid)
            {
                m_PendingRebuild.FinalJobHandle.Complete();
                m_PendingRebuild.DisposeBuffers();
            }
            m_PendingRebuild = default;
        }

        public void SetDefaults(Context context) => ResetState();

        public void ValidateAfterLoad()
        {
            if (!m_PendingBootDefaultRebuildCleanup)
                return;

            if (m_PendingRebuild.IsValid)
            {
                m_PendingRebuild.FinalJobHandle.Complete();
                m_PendingRebuild.DisposeBuffers();
            }
            m_PendingRebuild = default;
            m_PendingBootDefaultRebuildCleanup = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                NeighborEnvyCodec.Write(new NeighborEnvyState(FeatureEnabled), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(NeighborEnvySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                NeighborEnvyCodec.Read(reader, out var state);
                FeatureEnabled = state.FeatureEnabled;

                // FIX H67: Dispose in-flight rebuild from previous session to prevent NativeContainer leak.
                // ResetState/OnBecameDisabled/OnDestroy all dispose, but Deserialize success path did not.
                if (m_PendingRebuild.IsValid)
                {
                    m_PendingRebuild.FinalJobHandle.Complete();
                    m_PendingRebuild.DisposeBuffers();
                    m_PendingRebuild = default;
                }

                // Always force rebuild after load (entity indices unstable)
                m_EnvyData.NeedsFullRebuild = true;
                m_EnvyData.ClearDirtyDistricts();
                m_EnvyData.ClearAll();
                m_ThrottleCounter = 0;
                AffectedCount = 0;
                ProcessedCount = 0;

                if (Log.IsDebugEnabled) Log.Debug($"[NeighborEnvy] Deserialized: FeatureEnabled={FeatureEnabled}");
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
