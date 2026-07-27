using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// AirDefensePolicySystem - Save/Load serialization.
    /// Persists defense policy.
    /// </summary>
    public partial class AirDefensePolicySystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                AirDefensePolicyCodec.Write(new AirDefensePolicyState(m_CurrentPolicy), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(AirDefensePolicySystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            // Policy A: m_DeserializeSucceeded removed — there is no competing persisted
            // policy copy to reconcile against (AirDefenseCreditsSingleton no longer
            // persists policy). This codec is the single persisted policy source.
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(AirDefensePolicySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                AirDefensePolicyCodec.Read(reader, out var state);
                m_CurrentPolicy = state.Policy;
                // Sentinel uplift on deserialize: a save written from a prior
                // ResetState path before the explicit default uplift landed, a
                // corrupted save, or a bit-flip, can persist
                // DefensePolicy.Unavailable. Owner internal state never holds
                // the sentinel — uplift here closes the success-path equivalent
                // of the ResetState failure-path uplift in AirDefensePolicySystem.
                if (m_CurrentPolicy == DefensePolicy.Unavailable)
                {
                    Log.Warn("Deserialized DefensePolicy.Unavailable — uplifting to HumanitarianShield (sentinel must not persist in owner).");
                    m_CurrentPolicy = DefensePolicy.HumanitarianShield;
                }
                Log.Info($"Deserialized v{version}: Policy={m_CurrentPolicy}");
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
