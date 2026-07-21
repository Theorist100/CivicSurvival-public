using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Domains.PowerGrid.Systems
{
    /// <summary>
    /// AutoDispatchSystem - Save/Load serialization.
    /// Persists cooldown and stability timer so auto-dispatch
    /// resumes correctly after save/load without spurious shed or restore actions.
    /// </summary>
    #pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class AutoDispatchSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetBootDefaultsFields();

    #pragma warning restore CIVIC223
        [System.NonSerialized] private AutoDispatchData m_RestoredAutoDispatchData;
        [System.NonSerialized] private bool m_HasRestoredAutoDispatchData;

        private void ResetState()
        {
            EnsureAutoDispatchSingleton();
            ResetBootDefaultsFields();
        }

        private void ResetBootDefaultsFields()
        {
            m_HasRestoredAutoDispatchData = false;
            m_RestoreAutoSheddedAfterLoad = false;
            m_StabilitySeconds = 0f;
            m_NextAllowedDispatchSecond = 0.0;
            m_ServiceWarningLogged = false;
            m_CurAutoShedded.Clear();
            m_PrevAutoShedded.Clear();
            // Bump revision so the post-reset Empty publish carries a fresh
            // version stamp even if a prior reset already left the view at
            // Empty(0, 0). Without the bump, cursored consumers would miss
            // the boundary.
#pragma warning disable CIVIC226 // Monotonic version stamp — overflow wraps, equality-only consumer.
            unchecked { m_AutoDispatchRevision++; }
#pragma warning restore CIVIC226
            m_AutoDispatchOwnershipView.Publish(
                new AutoDispatchOwnershipSnapshot(false, 0, false, m_AutoDispatchRevision));
            m_ShedCandidates.Clear();
            m_RestoreCandidates.Clear();
            m_PowerLookup.Clear();
        }

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                if (!m_AutoDispatchQuery.TryGetSingleton<AutoDispatchData>(out var data))
                    data = AutoDispatchData.CreateDefault();

                var state = new AutoDispatchPersistState(
                    m_StabilitySeconds,
                    m_NextAllowedDispatchSecond,
                    data.Enabled,
                    data.AutoSheddedCount,
                    data.IsBlockedByVip);
                AutoDispatchCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(AutoDispatchSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                AutoDispatchCodec.Read(reader, out var state);
                m_NextAllowedDispatchSecond = state.NextAllowedDispatchSecond;
                // Reset stability accumulator — prevents premature restore on first post-load tick
                // when m_StabilitySeconds was near the 20s threshold at save time
                m_StabilitySeconds = 0f;
                m_ServiceWarningLogged = false;
                m_RestoredAutoDispatchData = new AutoDispatchData
                {
                    Enabled = state.Enabled,
                    AutoSheddedCount = state.Enabled ? state.AutoSheddedCount : 0,
                    IsBlockedByVip = state.Enabled && state.IsBlockedByVip
                };
                m_RestoreAutoSheddedAfterLoad = !state.Enabled;
                m_HasRestoredAutoDispatchData = true;
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

        public void OnLoadRestore(EntityManager entityManager)
        {
            EnsureAutoDispatchSingleton();
            if (m_AutoDispatchQuery.TryGetSingletonEntity<AutoDispatchData>(out var entity))
            {
                var restoredData = m_HasRestoredAutoDispatchData
                    ? m_RestoredAutoDispatchData
                    : AutoDispatchData.CreateDefault();
                entityManager.SetComponentData(entity, restoredData);
                ObserveAutoDispatchOwnedVersion(restoredData);
                if (!restoredData.Enabled)
                    m_RestoreAutoSheddedAfterLoad = true;
            }
            m_HasRestoredAutoDispatchData = false;
        }
    }
}
