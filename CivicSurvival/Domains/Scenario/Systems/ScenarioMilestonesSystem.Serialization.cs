using Colossal.Serialization.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.Scenario.Systems
{
    public partial class ScenarioMilestonesSystem : IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new ScenarioMilestonesPersistState(
                    m_WarFatigueShown,
                    m_WarFatigueDismissed,
                    m_VictoryShown,
                    m_VictoryTargetDay,
                    m_VictoryDismissed,
                    m_OneMoreYearCount);
                ScenarioMilestonesCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(ScenarioMilestonesSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                m_NeedOneMoreYearSelfHeal = true;
                return;
            }
            try
            {
                ScenarioMilestonesCodec.Read(reader, out var state);
                m_WarFatigueShown = state.WarFatigueShown;
                m_WarFatigueDismissed = state.WarFatigueDismissed;
                m_VictoryShown = state.VictoryShown;
                m_VictoryTargetDay = state.VictoryTargetDay;
                m_OneMoreYearCount = ClampOneMoreYearCount(state.OneMoreYearCount);
                // Legacy/truncated saves yield VictoryTargetDay=0 (codec default). A WarDayChangedEvent
                // buffered during load is drained synchronously at MarkEventHandlersReady (OnStartRunning),
                // before ValidateAfterLoad's reconcile fixes the target — with target=0 the
                // `warDay >= m_VictoryTargetDay` check passes prematurely and fires Victory. Seed a safe
                // lower bound, including OneMoreYear extensions already read above, so the window can
                // never see 0; reconcile refines it on the post-load pass.
                if (m_VictoryTargetDay <= 0)
                    m_VictoryTargetDay = BalanceConfig.Current.Scenario.VictoryDays + m_OneMoreYearCount * VICTORY_YEAR_DAYS;
                m_VictoryDismissed = state.VictoryDismissed;

                m_NeedOneMoreYearSelfHeal = true;

                Log.Info($"Deserialized: WarFatigue={m_WarFatigueShown} (dismissed={m_WarFatigueDismissed}), Victory={m_VictoryShown}, TargetDay={m_VictoryTargetDay}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
                m_NeedOneMoreYearSelfHeal = true;
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void ResetState()
        {
            m_WarFatigueShown = false;
            m_WarFatigueDismissed = false;
            m_VictoryShown = false;
            m_VictoryTargetDay = BalanceConfig.Current.Scenario.VictoryDays;
            m_VictoryDismissed = false;
            m_OneMoreYearCount = 0;
            m_NeedOneMoreYearSelfHeal = false;
        }

        public void SetDefaults(Context context) => ResetState();

        private static int ClampOneMoreYearCount(int count)
        {
            if (count <= 0)
                return 0;

            int configDays = BalanceConfig.Current.Scenario.VictoryDays;
            long maxCount = ((long)int.MaxValue - configDays) / VICTORY_YEAR_DAYS;
            if (maxCount < 0)
                return 0;

            // maxCount is bounded by ((int.MaxValue - configDays) / VICTORY_YEAR_DAYS) above, so it
            // always fits in int; checked makes the impossible overflow throw instead of truncating.
            return count > maxCount ? checked((int)maxCount) : count;
        }
    }
}
