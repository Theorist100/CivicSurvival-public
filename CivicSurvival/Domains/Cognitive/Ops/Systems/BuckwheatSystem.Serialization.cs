using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using System.Collections.Generic;
using Unity.Entities;

using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.Cognitive.Ops.Systems
{
    /// <summary>
    /// BuckwheatSystem - Save/Load serialization.
    /// Scalar state is in BuckwheatSingleton.
    /// Dictionary state (district effects, cooldowns) is local.
    /// </summary>
    public partial class BuckwheatSystem : IBootDefaultsReset
    {
#pragma warning disable CIVIC324 // Ephemeral boot-reset gate; armed by ResetToBootDefaults and consumed in the next ValidateAfterLoad pass.
        [System.NonSerialized] private bool m_PendingBootDefaultRuntimeRestore;
#pragma warning restore CIVIC324

        /// <summary>
        /// Transient/latch reset — data-only, no structural ECS. The single list of non-persisted
        /// state, called from every reset leg so the boot and fresh legs can't drift apart.
        /// </summary>
        private void ClearTransientState()
        {
            m_ExpiredDistricts.Clear();
            m_Gate = CreateGate();
            m_ForceInitialActiveTick = false;
        }

        /// <summary>
        /// Persisted-field reset to defaults — data-only (managed dictionaries only, no EntityManager;
        /// Invariant 5b / CIVIC462 forbid ECS work on the boot leg). Clears the district dictionaries so a
        /// corrupt/version-mismatch boot leg cannot leak the previous city's aid schedule. The ECS payload
        /// (BuckwheatSingleton/BuckwheatConfig) is reset in the deferred ValidateAfterLoad pass instead.
        /// m_PendingExpiryPenalties is persisted state (restored by Deserialize), so it resets here — NOT
        /// in ClearTransientState, which ValidateAfterLoad runs after a successful load and which would
        /// wipe the restored queue.
        /// </summary>
        private void ResetPersistedFieldsToDefaults()
        {
            m_DistrictAidExpiry.Clear();
            m_LastDistributionTime.Clear();
            m_PendingExpiryPenalties.Clear();
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            ResetPersistedFieldsToDefaults();
            ClearTransientState();
            // Defer the ECS singleton/config reset: ResetToBootDefaults runs mid-Deserialize where
            // EntityManager work is unsafe (Invariant 5b). ValidateAfterLoad applies the defaults.
            m_PendingBootDefaultRuntimeRestore = true;
        }

        public void SetDefaults(Context context)
        {
            ResetState();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var singleton = ReadSingleton();
                var config = ReadConfig();
                var state = new BuckwheatPersistState(
                    singleton.BuckwheatTons,
                    config.ProcurementLevel,
                    singleton.LastProcurementHour,
                    ToDistrictFloatEntries(m_DistrictAidExpiry),
                    ToDistrictFloatEntries(m_LastDistributionTime),
                    m_PendingExpiryPenalties.ToArray());
                var map = DistrictKeyCodec.BuildLiveMap(EntityManager);
                BuckwheatCodec.Write(state, writer, map);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(BuckwheatSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(BuckwheatSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                m_DistrictAidExpiry.Clear();
                m_LastDistributionTime.Clear();
                m_PendingExpiryPenalties.Clear();

                BuckwheatCodec.Read(reader, out var state);
                ApplyDistrictFloatEntries(m_DistrictAidExpiry, state.AidExpiry);
                ApplyDistrictFloatEntries(m_LastDistributionTime, state.LastDistributionTime);
                m_PendingExpiryPenalties.AddRange(state.PendingExpiryPenalties);

                // Write directly to singleton + config (entities created in OnCreate)
                if (m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var entity))
                {
                    EntityManager.SetComponentData(entity, new BuckwheatSingleton
                    {
                        BuckwheatTons = state.BuckwheatTons,
                        LastProcurementHour = state.LastProcurementHour
                    });
                }
                if (m_ConfigQuery.TryGetSingletonEntity<BuckwheatConfig>(out var configEntity))
                {
                    EntityManager.SetComponentData(configEntity, new BuckwheatConfig
                    {
                        ProcurementLevel = state.ProcurementLevel
                    });
                }

                Log.Info($"Deserialized v{version}: Buckwheat={state.BuckwheatTons:F1}t");
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

        public void ValidateAfterLoad()
        {
            // Consume the boot-restore latch up front so an exception below can't leave it armed
            // (which would let the next load be clobbered by boot defaults).
            bool restoreBootDefaults = m_PendingBootDefaultRuntimeRestore;
            m_PendingBootDefaultRuntimeRestore = false;

            ClearTransientState();

            // A corrupt/version-mismatch boot deferred its ECS reset to here (EntityManager is safe
            // post-load): return the singleton/config payload to Default so the previous city's reserve
            // or procurement level cannot leak in.
            if (restoreBootDefaults)
            {
                if (m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var singletonEntity))
                    EntityManager.SetComponentData(singletonEntity, BuckwheatSingleton.Default);
                if (m_ConfigQuery.TryGetSingletonEntity<BuckwheatConfig>(out var configEntity))
                    EntityManager.SetComponentData(configEntity, BuckwheatConfig.Default);
            }

            // The buckwheat gate is open from PreWar onward, so restored food aid is valid in
            // every act — there is no below-threshold act that could contradict it. Rebuild the
            // aided-districts ECS projection from the restored m_DistrictAidExpiry so the cognitive
            // resolver sees restored food-aid on its first post-load tick (closes the "aid without
            // infection effect" window). Cached m_AidedBufferLookup is safe outside the SystemAPI context.
            PublishAidedDistricts();
        }

        private static DistrictFloatEntry[] ToDistrictFloatEntries(IReadOnlyDictionary<int, float> map)
        {
            var entries = new DistrictFloatEntry[map.Count];
            int index = 0;
            foreach (var kvp in map)
            {
                entries[index] = new DistrictFloatEntry(kvp.Key, kvp.Value);
                index++;
            }
            return entries;
        }

        private static void ApplyDistrictFloatEntries(Dictionary<int, float> map, IReadOnlyList<DistrictFloatEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                map[entries[i].DistrictIndex] = entries[i].Value;
        }
    }
}
