using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Config;
using CivicSurvival.Domains.Attention.Data;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// WorldShockSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists shock level, tier, weekly statistics, totals, and 7-day ring buffer.
    /// </summary>
    public partial class WorldShockSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        // Local copy for serialization (entity component not available during serialize/deserialize)
        private WorldShockState m_SerializedState;
        private bool m_HasSerializedState;

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                WorldShockState state = default;
                var stateEntity = m_State.Entity;
                if (EntityManager.Exists(stateEntity))
                {
                    state = EntityManager.GetComponentData<WorldShockState>(stateEntity);
                }

                var persistState = new WorldShockPersistState(
                    state.ShockLevel,
                    state.CurrentTier,
                    state.LastUpdateTime,
                    state.DecayPerDay,
                    state.CasualtiesThisWeek,
                    state.BuildingsDestroyedThisWeek,
                    state.CriticalHitsThisWeek,
                    state.LastTragedyTime,
                    m_LastTier,
                    state.TotalCasualties,
                    state.TotalBuildingsDestroyed,
                    state.TotalCivilianBuildingsDestroyed,
                    state.TotalCriticalHits,
                    m_RingIndex,
                    m_DailyCasualties,
                    m_DailyBuildings,
                    m_DailyCritical,
                    m_PrevTotalCasualties,
                    m_PrevTotalBuildings,
                    m_PrevTotalCritical,
                    m_DayChanged);
                WorldShockCodec.Write(persistState, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(WorldShockSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(WorldShockSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetState();

                WorldShockCodec.Read(reader, BalanceConfig.Current.Attention.DecayPerDay, out var persistState);
                m_LastTier = persistState.LastTier;
                m_RingIndex = persistState.RingIndex;
                CopyRingBuffer(persistState.DailyCasualties, m_DailyCasualties);
                CopyRingBuffer(persistState.DailyBuildings, m_DailyBuildings);
                CopyRingBuffer(persistState.DailyCritical, m_DailyCritical);
                m_PrevTotalCasualties = persistState.PrevTotalCasualties;
                m_PrevTotalBuildings = persistState.PrevTotalBuildings;
                m_PrevTotalCritical = persistState.PrevTotalCritical;
                m_DayChanged = persistState.DayChanged;

                // Store for restoration in OnUpdate (entity may not exist yet after load)
                m_SerializedState = new WorldShockState
                {
                    ShockLevel = persistState.ShockLevel,
                    CurrentTier = persistState.CurrentTier,
                    LastUpdateTime = persistState.LastUpdateTime,
                    DecayPerDay = persistState.DecayPerDay,
                    CasualtiesThisWeek = persistState.CasualtiesThisWeek,
                    BuildingsDestroyedThisWeek = persistState.BuildingsDestroyedThisWeek,
                    CriticalHitsThisWeek = persistState.CriticalHitsThisWeek,
                    LastTragedyTime = persistState.LastTragedyTime,
                    TotalCasualties = persistState.TotalCasualties,
                    TotalBuildingsDestroyed = persistState.TotalBuildingsDestroyed,
                    TotalCivilianBuildingsDestroyed = persistState.TotalCivilianBuildingsDestroyed,
                    TotalCriticalHits = persistState.TotalCriticalHits
                };
                m_HasSerializedState = true;

                var stateEntity = m_State.Entity;
                if (EntityManager.Exists(stateEntity))
                {
                    EntityManager.SetComponentData(stateEntity, m_SerializedState);
                    m_LastTier = m_SerializedState.CurrentTier;
                    m_HasSerializedState = false;
                    m_ShockSingletonLookup.Update(this);
                    UpdateShockSingleton(m_SerializedState);
                }

                Log.Info($"Deserialized v{version}: Shock={persistState.ShockLevel:F1}%, Tier={persistState.CurrentTier}, " +
                    $"Total: C={persistState.TotalCasualties} B={persistState.TotalBuildingsDestroyed} Cr={persistState.TotalCriticalHits}");
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
        private static void CopyRingBuffer(System.Collections.Generic.IReadOnlyList<int> source, int[] target)
        {
            for (int i = 0; i < target.Length; i++)
                target[i] = i < source.Count ? source[i] : 0;
        }
    }
}
