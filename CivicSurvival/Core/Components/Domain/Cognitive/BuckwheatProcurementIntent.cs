using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Typed link from a retained ShadowOps budget request to a pending buckwheat procurement.
    /// Buckwheat reserve is credited only after BudgetResolutionSystem writes a successful result.
    /// </summary>
    public struct BuckwheatProcurementIntent : IComponentData, ISerializable
    {
        public float Tons;
        public float ProcurementHour;
        public long Cost;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "tons", Tons);
                KeyedSerializer.WriteField(writer, "hour", ProcurementHour);
                KeyedSerializer.WriteField(writer, "cost", Cost);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BuckwheatProcurementIntent)))
            { SetDefaults(); return; }
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
                            case "tons": Tons = KeyedSerializer.ReadSafeFloat(reader, tag, "tons", 0f, 1_000_000f, 0f); break;
                            case "hour": ProcurementHour = KeyedSerializer.ReadSafeFloat(reader, tag, "hour", 0f, 1_000_000f, 0f); break;
                            case "cost": Cost = KeyedSerializer.ReadBoundedLong(reader, tag, "cost", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(BuckwheatProcurementIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
