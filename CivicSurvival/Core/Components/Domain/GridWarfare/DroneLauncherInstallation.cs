using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    internal static class DroneLauncherInstallationLog
    {
        public static readonly LogContext Log = new("DroneLauncherInstall");
    }

    /// <summary>
    /// A placed drone launcher — a field emplacement that outbound counter-strike drones
    /// launch from instead of the map centre. Created on a separate mod entity (mirrors
    /// <c>AirDefenseInstallation</c> / <c>PropagandaCenterInstallation</c>) by
    /// <c>DroneLauncherPlacementHandler.Commit</c> via the generic building-placement commit
    /// system; <see cref="Building"/> links the placed object so a demolished launcher drops
    /// out of the live set. Multi-instance: any number can be built, no single-HQ guard.
    ///
    /// Serialized so a built launcher survives save/load. The outbound-launch origin
    /// (<c>ThreatSpawnSystem.Launch</c>) reads the live set of these installations directly —
    /// a persisted install-component gate, pause-safe (Axiom 14), never a derived singleton.
    /// </summary>
    public struct DroneLauncherInstallation : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>The placed launcher object (typed Index+Version, Axiom 11 — no Entity field).</summary>
        public BuildingRef Building;

        /// <summary>
        /// Shadow Cash charged at placement. Recorded for parity with the other installations
        /// (a future demolition-refund path); enemy destruction refunds nothing.
        /// 0 = free/legacy placement (no refund).
        /// </summary>
        public int PaidShadow;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "paid", PaidShadow);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DroneLauncherInstallation)))
            {
                DroneLauncherInstallationLog.Log.Warn($"Deserialize: TryBeginBlock FAILED (expected v{SAVE_VERSION}) -> SetDefaults");
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
                            case "paid": PaidShadow = KeyedSerializer.ReadBoundedInt(reader, tag, "paid", 0, int.MaxValue, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                DroneLauncherInstallationLog.Log.Error($"Deserialize {nameof(DroneLauncherInstallation)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
