using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Tutorial.Systems
{
    public partial class CrisisTutorialSystem : IBootDefaultsReset
    {
        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CrisisTutorialPersistState(
                    m_FirstStrikeShown,
                    m_ExodusWarningShown,
                    m_GridTabOpenedInCrisis,
                    m_ShadowTabOpenedInCrisis,
                    m_GridTabOpenedPreCrisis,
                    m_ShadowTabOpenedPreCrisis,
                    m_CrisisActActive,
                    m_CrisisStartDay,
                    m_PopulationAtCrisisStart,
                    m_FirstWaveEnded,
                    m_FirstWaveCausedDamage);
                CrisisTutorialCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CrisisTutorialSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(CrisisTutorialSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                m_NeedBootDefaultCrisisRestore = true;
                return;
            }
            try
            {
                m_NeedBootDefaultCrisisRestore = false;
                CrisisTutorialCodec.Read(reader, out var state);
                m_FirstStrikeShown = state.FirstStrikeShown;
                m_ExodusWarningShown = state.ExodusWarningShown;
                m_GridTabOpenedInCrisis = state.GridTabOpenedInCrisis;
                m_ShadowTabOpenedInCrisis = state.ShadowTabOpenedInCrisis;
                m_GridTabOpenedPreCrisis = state.GridTabOpenedPreCrisis;
                m_ShadowTabOpenedPreCrisis = state.ShadowTabOpenedPreCrisis;
                m_CrisisActActive = state.CrisisActActive;
                m_CrisisStartDay = state.CrisisStartDay;
                m_PopulationAtCrisisStart = state.PopulationAtCrisisStart;
                m_FirstWaveEnded = state.FirstWaveEnded;
                m_FirstWaveCausedDamage = state.FirstWaveCausedDamage;

                Log.Info($"[CrisisTutorial] Deserialized v{version}: CrisisActive={m_CrisisActActive}, GridTabOpened={m_GridTabOpenedInCrisis}, ShadowTabOpened={m_ShadowTabOpenedInCrisis}, FirstStrikeShown={m_FirstStrikeShown}, Pop={m_PopulationAtCrisisStart}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
                m_NeedBootDefaultCrisisRestore = true;
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
