using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class DebriefingInfraStatsLog
    {
        public static readonly LogContext Log = new("DebriefingInfraStats");
    }

    /// <summary>
    /// Debriefing: infrastructure damage cost this wave.
    /// Single Writer: DamageAccountingSystem.
    /// Split from WaveDebriefingData to prevent ECB full-struct stomp.
    /// </summary>
    public struct DebriefingInfraStats : IComponentData, ISerializable
    {
        /// <summary>
        /// Estimated infrastructure repair cost from operational/explosion/fire damage this wave.
        /// Display only (debriefing UI). NOT a charge — real payment via PlantRepairService.
        /// Do NOT add to WarDamageDebtSystem (double-charge). See Debt_System.md "Cost Separation".
        /// </summary>
        public long InfrastructureDamageCost;

        public void Reset() => SetDefaults();

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                DebriefingStatsCodec.WriteInfra(this, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DebriefingInfraStats)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    DebriefingStatsCodec.ReadInfra(reader, this, out var stats);
                    this = stats;
                }
            }
            catch (System.Exception ex)
            {
                DebriefingInfraStatsLog.Log.Error($"Deserialize {nameof(DebriefingInfraStats)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


