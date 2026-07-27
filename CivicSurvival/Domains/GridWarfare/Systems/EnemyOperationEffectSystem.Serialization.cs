using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// EnemyOperationEffectSystem — Save/Load serialization (IDefaultSerializable).
    /// Persists the pending-launch ledger: every committed outbound launch whose projectile may not
    /// have materialised yet (the spawn intent it writes is transient by design, so a save taken in
    /// the producer→consumer gap would otherwise silently lose a strike whose munition and Shadow
    /// Cash are already spent). On load the system re-launches any entry with no matching in-flight
    /// projectile — resources are NOT re-spent, only the spawn intent is re-recorded with the same
    /// launch-frozen seed/target.
    /// </summary>
    public partial class EnemyOperationEffectSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        /// <summary>Upper bound on persisted ledger entries (guards corrupt saves; the live list is tiny).</summary>
        private const int MaxPendingLaunches = 64;

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        /// <summary>Called when starting a new game (not loading a save).</summary>
        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            m_PendingEffects.Clear();
            m_PendingLaunches.Clear();
            m_InFlightSeeds.Clear();
            m_ReconcilePendingLaunches = false;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                int count = System.Math.Min(m_PendingLaunches.Count, MaxPendingLaunches);
                // Overflow keeps the TAIL (newest entries): launches append to the end, and the
                // newest are exactly the ones most likely still inside the transient launch→spawn
                // gap the ledger exists to bridge — dropping them would lose the paid strike the
                // whole mechanism is meant to save. The oldest entries have in-flight projectiles
                // whose payloads persist on their own.
                int start = m_PendingLaunches.Count - count;
                if (start > 0)
                    Log.Warn($"Pending-launch ledger over cap: persisting newest {count} of {m_PendingLaunches.Count}, dropping {start} oldest");
                KeyedSerializer.WriteBufferHeader(writer, "pendingLaunches", count);
                for (int i = start; i < m_PendingLaunches.Count; i++)
                {
                    var p = m_PendingLaunches[i];
                    KeyedSerializer.WriteBlockHeader(writer, 6);
                    KeyedSerializer.WriteEnumByteField(writer, "kind", (byte)p.Kind);
                    KeyedSerializer.WriteEnumByteField(writer, "axis", (byte)p.Axis);
                    KeyedSerializer.WriteField(writer, "damage", p.Damage);
                    // uint seed stored bit-preserving through the int codec (mirror of OutboundStrikePayload).
                    KeyedSerializer.WriteField(writer, "seed", unchecked((int)p.Seed));
                    KeyedSerializer.WriteField(writer, "targetId", (int)p.TargetId);
                    KeyedSerializer.WriteField(writer, "launchedAt", p.LaunchedAtHours);
                }
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(EnemyOperationEffectSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            m_PendingLaunches.Clear();
            m_ReconcilePendingLaunches = false;

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(EnemyOperationEffectSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int i = 0; i < fieldCount; i++)
                {
                    var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "pendingLaunches": ReadPendingLaunches(reader, tag); break;
                        default: KeyedSerializer.Skip(reader, tag); break;
                    }
                }

                m_ReconcilePendingLaunches = m_PendingLaunches.Count > 0;
                Log.Info($"Deserialized v{version}: pendingLaunches={m_PendingLaunches.Count}");
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

        private void ReadPendingLaunches<TReader>(TReader reader, TypeTag tag) where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "pendingLaunches", MaxPendingLaunches);
            for (int i = 0; i < count; i++)
            {
                var kind = ArsenalKind.Drone;
                var axis = AttackCategory.Kinetic;
                float damage = 0f;
                uint seed = 0u;
                ushort targetId = EnemyTarget.NoTargetId;
                float launchedAt = 0f; // 0 = pre-TTL save without a stamp (reconcile skips the TTL)

                int fields = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fields; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "kind": kind = KeyedSerializer.ReadEnumByte<TReader, ArsenalKind>(reader, fieldTag, "kind", ArsenalKind.Drone); break;
                        case "axis": axis = KeyedSerializer.ReadEnumByte<TReader, AttackCategory>(reader, fieldTag, "axis", AttackCategory.Kinetic); break;
                        case "damage": damage = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "damage", 0f, 100f, 0f); break;
                        case "seed": seed = unchecked((uint)KeyedSerializer.ReadInt(reader, fieldTag, "seed", 0)); break;
                        case "targetId": targetId = (ushort)KeyedSerializer.ReadBoundedInt(reader, fieldTag, "targetId", 0, ushort.MaxValue, EnemyTarget.NoTargetId); break;
                        case "launchedAt": launchedAt = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "launchedAt", 0f, float.MaxValue, 0f); break;
                        default: KeyedSerializer.Skip(reader, fieldTag); break;
                    }
                }

                // A zero seed can never come from a real launch (FreezeStrikeSeed forces a set bit) —
                // treat it as corruption and drop the entry rather than re-launching garbage.
                if (seed != 0u)
                {
                    m_PendingLaunches.Add(new PendingOutboundLaunch
                    {
                        Kind = kind,
                        Axis = axis,
                        Damage = damage,
                        Seed = seed,
                        TargetId = targetId,
                        LaunchedAtHours = launchedAt
                    });
                }
            }
        }
    }
}
