using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    internal static class PropagandaCenterInstallationLog
    {
        public static readonly LogContext Log = new("PropagandaCenterInstall");
    }

    /// <summary>
    /// A placed Propaganda Center — the unique information-war HQ that unlocks the cognitive
    /// defence layer and owns the global broadcast-capacity budget. Created on a
    /// separate mod entity (mirrors <c>AirDefenseInstallation</c>) by
    /// <c>PropagandaCenterPlacementHandler.Commit</c> via the generic building-placement
    /// commit system; <see cref="Building"/> links the placed object for lifecycle tracking.
    ///
    /// <see cref="UpgradeTier"/> is the persisted growth lever: broadcast capacity grows by
    /// upgrading the Center (not by placing copies — copies would be a set-and-forget spam,
    /// forbidden by the balance rule). Serialized so a built + upgraded Center survives
    /// save/load; the derived <c>PropagandaCenterState</c> singleton is rebuilt from these
    /// installations each tick (not serialized).
    /// </summary>
    public struct PropagandaCenterInstallation : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>The placed Center object (typed Index+Version, Axiom 11 — no Entity field).</summary>
        public BuildingRef Building;

        /// <summary>Broadcast-capacity upgrade tier (0 = base). Persisted growth lever.</summary>
        public int UpgradeTier;

        /// <summary>
        /// Player broadcast-coverage allocation weights across strata (Poor/Middle/Wealthy), normalized
        /// to sum 1. Persisted allocation lever alongside <see cref="UpgradeTier"/>. All-zero = never
        /// allocated → the derived <c>PropagandaCenterState</c> auto-splits the coverage ceiling by
        /// population until the player sets a split (Phase 13A).
        /// </summary>
        public float AllocWeightPoor;
        public float AllocWeightMiddle;
        public float AllocWeightWealthy;

        /// <summary>
        /// Base budget charged at placement. On PLAYER demolition (bulldoze) this amount is
        /// refunded in full by <c>PropagandaCenterDemolitionSystem</c>; upgrade spend is NOT
        /// accumulated here and is not refunded. Enemy destruction refunds nothing.
        /// 0 = free/legacy placement (no refund).
        /// </summary>
        public int PaidBudget;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;
        private const int MAX_TIER = 100;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 6);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "tier", UpgradeTier);
                KeyedSerializer.WriteField(writer, "paid", PaidBudget);
                KeyedSerializer.WriteField(writer, "awP", AllocWeightPoor);
                KeyedSerializer.WriteField(writer, "awM", AllocWeightMiddle);
                KeyedSerializer.WriteField(writer, "awW", AllocWeightWealthy);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PropagandaCenterInstallation)))
            {
                PropagandaCenterInstallationLog.Log.Warn($"Deserialize: TryBeginBlock FAILED (expected v{SAVE_VERSION}) -> SetDefaults");
                SetDefaults();
                return;
            }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "tier": UpgradeTier = KeyedSerializer.ReadBoundedInt(reader, tag, "tier", 0, MAX_TIER, 0); break;
                            case "paid": PaidBudget = KeyedSerializer.ReadBoundedInt(reader, tag, "paid", 0, int.MaxValue, 0); break;
                            case "awP": AllocWeightPoor = KeyedSerializer.ReadSafeFloat(reader, tag, "awP", 0f, 1f, 0f); break;
                            case "awM": AllocWeightMiddle = KeyedSerializer.ReadSafeFloat(reader, tag, "awM", 0f, 1f, 0f); break;
                            case "awW": AllocWeightWealthy = KeyedSerializer.ReadSafeFloat(reader, tag, "awW", 0f, 1f, 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PropagandaCenterInstallationLog.Log.Error($"Deserialize {nameof(PropagandaCenterInstallation)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
