using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Domains.Refugees.Services;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// RefugeeSpawnSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists refugee wave state for Village scenario.
    /// </summary>
    public partial class RefugeeSpawnSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new RefugeeSpawnPersistState(
                    m_Active,
                    m_HoursElapsed,
                    m_TotalRefugeesAdded,
                    m_SpawnCounter,
                    m_OriginalPopulation,
                    m_RefugeesAtBorder,
                    m_RefugeeParkBuiltSent,
                    m_LastNagGameHour,
                    m_LastUpdateTime,
                    m_ShownRefugeeModal,
                    m_ShownCollapseModal,
                    m_PendingRefugeeUnits);
                RefugeeSpawnCodec.Write(state, writer);

                // Manual keyed block (order-independent — safe to add fields later)
                bool hasSpawnService = m_SpawnService != null;
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteField(writer, "hasSpawnService", hasSpawnService);
                KeyedSerializer.WriteField(writer, "parkAnchored", m_ParkAnchored);
                KeyedSerializer.WriteField(writer, "targetRefugees", m_TargetRefugees);
                KeyedSerializer.WriteField(writer, "anchorGameHour", m_AnchorGameHour);
                if (hasSpawnService)
                {
                    m_SpawnService!.Serialize(writer);
                }

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(RefugeeSpawnSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(RefugeeSpawnSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                RefugeeSpawnCodec.Read(reader, out var state);
                m_Active = state.Active;
                m_HoursElapsed = state.HoursElapsed;
                m_TotalRefugeesAdded = state.TotalRefugeesAdded;
                m_SpawnCounter = state.SpawnCounter;
                m_OriginalPopulation = state.OriginalPopulation;
                m_RefugeesAtBorder = state.RefugeesAtBorder;
                m_RefugeeParkBuiltSent = state.RefugeeParkBuiltSent;
                m_LastNagGameHour = state.LastNagGameHour;
                m_LastUpdateTime = state.LastUpdateTime;
                m_ShownRefugeeModal = state.ShownRefugeeModal;
                m_ShownCollapseModal = state.ShownCollapseModal;
                m_PendingRefugeeUnits = state.PendingRefugeeUnits;
                m_CachedCitizenCount = 0f;
                m_LastRateRecomputeGameHours = double.NegativeInfinity;

                // Manual keyed block (order-independent)
                bool hasSpawnService = false;
                m_ParkAnchored = false;
                m_TargetRefugees = 0;
                m_AnchorGameHour = 0.0;
                int __fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int __i = 0; __i < __fc; __i++)
                {
                    var __tag = KeyedSerializer.ReadFieldHeader(reader, out var __key);
                    switch (__key)
                    {
                        case "hasSpawnService":
                            hasSpawnService = KeyedSerializer.ReadBool(reader, __tag, "hasSpawnService");
                            break;
                        case "parkAnchored":
                            m_ParkAnchored = KeyedSerializer.ReadBool(reader, __tag, "parkAnchored");
                            break;
                        case "targetRefugees":
                            m_TargetRefugees = KeyedSerializer.ReadInt(reader, __tag, "targetRefugees");
                            break;
                        case "anchorGameHour":
                            m_AnchorGameHour = KeyedSerializer.ReadDouble(reader, __tag, "anchorGameHour");
                            break;
                        default: KeyedSerializer.Skip(reader, __tag); break;
                    }
                }
                m_HasRestoredSpawnServiceRandomState = false;
                m_RestoredSpawnServiceRandomState = 0u;
                if (hasSpawnService)
                {
                    RefugeeSpawnServiceCodec.Read(reader, out var serviceState);
                    if (m_SpawnService != null)
                    {
                        m_SpawnService.RestoreRandomState(serviceState.RandomState);
                    }
                    else
                    {
                        m_RestoredSpawnServiceRandomState = serviceState.RandomState;
                        m_HasRestoredSpawnServiceRandomState = true;
                    }
                }

                // S15-1 FIX: Resync timer on first frame to prevent catch-up burst
                if (m_Active) m_NeedsTimeResync = true;

                Log.Info($"Deserialized v{version}: Active={m_Active}, Refugees={m_TotalRefugeesAdded}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
                World.GetExistingSystemManaged<RefugeeInfluxCoordinator>()?.ClearInfluxActivated();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private void ResetState()
        {
            m_Active = false;
            m_HoursElapsed = 0;
            m_TotalRefugeesAdded = 0;
            m_SpawnCounter = 0;
            m_OriginalPopulation = 0;
            m_RefugeesAtBorder = 0;
            m_RefugeeParkBuiltSent = false;
            m_LastNagGameHour = 0.0;
            m_LastUpdateTime = -1.0;
            m_ShownRefugeeModal = false;
            m_ShownCollapseModal = false;
            m_PendingRefugeeUnits = 0;
            m_ParkAnchored = false;
            m_TargetRefugees = 0;
            m_AnchorGameHour = 0.0;
            m_CachedCitizenCount = 0f;
            m_LastRateRecomputeGameHours = double.NegativeInfinity;
            m_NeedsTimeResync = false;
            m_PostedRefugeeMessageThisUpdate = false;
            m_HasRestoredSpawnServiceRandomState = false;
            m_RestoredSpawnServiceRandomState = 0u;
            m_HouseholdPrefabChoices.Clear();

            Log.Info("State reset");
        }
    }
}
