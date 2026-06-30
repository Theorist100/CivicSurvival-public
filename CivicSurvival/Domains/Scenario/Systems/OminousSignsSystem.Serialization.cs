using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// OminousSignsSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists milestone-driven pre-war state for the Village scenario.
    /// </summary>
    public partial class OminousSignsSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new OminousSignsPersistState(
                    m_Active,
                    m_SignsTriggered,
                    m_WarStarted);
                OminousSignsCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(OminousSignsSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(OminousSignsSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                OminousSignsCodec.Read(reader, out var state);
                m_Active = state.Active;
                m_SignsTriggered = state.SignsTriggered;
                m_WarStarted = state.WarStarted;
                ResetTransientRuntimeStateAfterLoad();

                // Legacy saves (day-countdown or population-radar models) carried extra fields and a
                // trailing RNG block. The keyed codec skips the unknown fields, and the length-prefixed
                // block skips any trailing bytes, so an active pre-war simply resumes under the
                // milestone-driven model — war fires once the achieved milestone reaches the war
                // milestone, regardless of how the save was produced.

                Log.Info($"Deserialized v{version}: Active={m_Active}, WarStarted={m_WarStarted}");
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

        private void ResetTransientRuntimeStateAfterLoad()
        {
            m_IsCatchingUp = false;
            m_SoundPositionCached = false;
            m_MilestoneXpCached = false;
            m_PrewarXpReq = 0;
            m_WarXpReq = 0;
        }
    }
}
