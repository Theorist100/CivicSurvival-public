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
        public void ResetToBootDefaults(ResetReason reason)
        {
            m_DistrictAidExpiry.Clear();
            m_LastDistributionTime.Clear();
            m_ExpiredDistricts.Clear();
            m_Gate = CreateGate();
            m_ForceInitialActiveTick = false;
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
                    ToDistrictFloatEntries(m_LastDistributionTime));
                BuckwheatCodec.Write(state, writer);

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

                BuckwheatCodec.Read(reader, out var state);
                ApplyDistrictFloatEntries(m_DistrictAidExpiry, state.AidExpiry);
                ApplyDistrictFloatEntries(m_LastDistributionTime, state.LastDistributionTime);

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
            m_Gate = CreateGate();
            m_ForceInitialActiveTick = false;
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
