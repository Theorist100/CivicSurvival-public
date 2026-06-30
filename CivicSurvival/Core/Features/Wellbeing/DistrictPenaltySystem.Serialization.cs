using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Features.Wellbeing
{
    public partial class DistrictPenaltySystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetBootDefaultsFields();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_StateWriter?.ClearPenalties();
            ResetBootDefaultsFields();
            Log.Info("SetDefaults: Reset to fresh state");
        }

        private void ResetBootDefaultsFields()
        {
            m_IsProcessingPenaltyRequests = false;
            m_LoggedMissingStateServices = false;
            IsWinterActive = false;
            IsInfraCollapsed = false;
            m_InfraCollapseHoursRemaining = 0;
            m_LastInfraCheckHour = -1f;
            // R4-S8-09 FIX: Reset extended fields
            IsConscriptionActive = false;
            IsMourningActive = false;
            m_MourningHoursRemaining = 0;
            m_MourningHappinessPenalty = 0;
            m_LastMourningCheckHour = -1f;
            IsPreWarTensionActive = false;
            m_PreWarHappinessPenalty = 0;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                DistrictPenaltyCodec.Write(
                    new DistrictPenaltyState(
                        IsInfraCollapsed,
                        m_InfraCollapseHoursRemaining,
                        m_LastInfraCheckHour,
                        IsMourningActive,
                        m_MourningHoursRemaining,
                        m_MourningHappinessPenalty,
                        m_LastMourningCheckHour,
                        IsPreWarTensionActive,
                        m_PreWarHappinessPenalty),
                    writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(DistrictPenaltySystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(DistrictPenaltySystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                DistrictPenaltyCodec.Read(reader, out var state);
                IsInfraCollapsed = state.InfraCollapsed;
                m_InfraCollapseHoursRemaining = state.InfraCollapseHoursRemaining;
                m_LastInfraCheckHour = state.LastInfraCheckHour;
                IsMourningActive = state.MourningActive;
                m_MourningHoursRemaining = state.MourningHoursRemaining;
                m_MourningHappinessPenalty = state.MourningHappinessPenalty;
                m_LastMourningCheckHour = state.LastMourningCheckHour;
                IsPreWarTensionActive = state.PreWarTensionActive;
                m_PreWarHappinessPenalty = state.PreWarHappinessPenalty;

                ResetDerivedSingletonFlagsForLoad();
                NormalizeLoadedTimedPenaltyState();

                Log.Info($"Deserialized v{version}: Winter=(synced), InfraCollapsed={IsInfraCollapsed}, " +
                    $"HoursRemaining={m_InfraCollapseHoursRemaining:F1}, Conscription=(synced), " +
                    $"Mourning={IsMourningActive}({m_MourningHoursRemaining:F1}h), PreWar={IsPreWarTensionActive}");
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

        private void ResetDerivedSingletonFlagsForLoad()
        {
            // LOAD-INVARIANT: missing foreign singletons mean neutral derived global flags, not prior-city state.
            IsWinterActive = false;
            IsConscriptionActive = false;
        }

        private void NormalizeLoadedTimedPenaltyState()
        {
            // Deserialize fires before OnGameLoaded; GameTimeSystem may be null here.
            // When unavailable, skip clamping — values are already bounded by Codec
            // validation, and they'll self-correct on the first OnUpdateImpl tick.
            if (GameTimeSystem.TryGetGameHours(out var currentHour))
            {
                if (IsInfraCollapsed && m_LastInfraCheckHour > currentHour)
                    m_LastInfraCheckHour = currentHour;
                if (IsMourningActive && m_LastMourningCheckHour > currentHour)
                    m_LastMourningCheckHour = currentHour;
            }

            if (SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton) && actSingleton.CurrentAct != Act.PreWar)
            {
                IsPreWarTensionActive = false;
                m_PreWarHappinessPenalty = 0f;
            }

            SyncWinterFromSingleton();
            SyncConscriptionFromSingleton();
        }
    }
}
