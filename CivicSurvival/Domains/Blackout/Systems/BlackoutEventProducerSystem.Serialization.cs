using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Domains.Blackout.Systems
{
    public partial class BlackoutEventProducerSystem : IDefaultSerializable, IBootDefaultsReset
    {
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new BlackoutEventProducerState(
                    ToDistrictBoolEntries(m_PreviousBlackoutState),
                    ToDistrictFloatEntries(m_BlackoutStartHours),
                    ToArray(m_LongBlackoutFired),
                    ToArray(m_VIPVisibleFired),
                    m_LastNonVipBlackoutHour);
                BlackoutEventProducerCodec.Write(state, writer);

                Log.Info($"Serialize: {m_PreviousBlackoutState.Count} prev, {m_BlackoutStartHours.Count} starts, {m_LongBlackoutFired.Count} longFired, {m_VIPVisibleFired.Count} vipFired");
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(BlackoutEventProducerSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                m_PreviousBlackoutState.Clear();
                m_BlackoutStartHours.Clear();
                m_LongBlackoutFired.Clear();
                m_VIPVisibleFired.Clear();

                BlackoutEventProducerCodec.Read(reader, out var state);
                Apply(m_PreviousBlackoutState, state.PreviousBlackoutState);
                Apply(m_BlackoutStartHours, state.BlackoutStartHours);
                Apply(m_LongBlackoutFired, state.LongBlackoutFired);
                Apply(m_VIPVisibleFired, state.VipVisibleFired);
                m_LastNonVipBlackoutHour = state.LastNonVipBlackoutHour;

                m_SyncFirstTick = true;
                Log.Info($"Deserialize: {m_PreviousBlackoutState.Count} prev, {m_BlackoutStartHours.Count} starts, {m_LongBlackoutFired.Count} longFired, {m_VIPVisibleFired.Count} vipFired");
            }
            catch (Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults(Context context) => ResetState();

        private static DistrictBoolEntry[] ToDistrictBoolEntries(IReadOnlyDictionary<int, bool> map)
        {
            var entries = new DistrictBoolEntry[map.Count];
            int index = 0;
            foreach (var kvp in map)
            {
                entries[index] = new DistrictBoolEntry(kvp.Key, kvp.Value);
                index++;
            }
            return entries;
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

        private static int[] ToArray(IReadOnlyCollection<int> set)
        {
            var entries = new int[set.Count];
            int index = 0;
            foreach (int value in set)
            {
                entries[index] = value;
                index++;
            }
            return entries;
        }

        private static void Apply(Dictionary<int, bool> map, IReadOnlyList<DistrictBoolEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                map[entries[i].DistrictIndex] = entries[i].Value;
        }

        private static void Apply(Dictionary<int, float> map, IReadOnlyList<DistrictFloatEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                map[entries[i].DistrictIndex] = entries[i].Value;
        }

        private static void Apply(HashSet<int> set, IReadOnlyList<int> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                set.Add(entries[i]);
        }
    }
}
