using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    internal static class SaturationModifierLog
    {
        public static readonly LogContext Log = new("SaturationModifier");
    }

    /// <summary>
    /// Per-plant surplus-saturation state. SaturationFactor ∈ [SaturationFloor, 1]
    /// is the EFFECTIVE factor (after asymmetric inertia), folded into the capacity
    /// Efficiency factor by PowerCapacityResolverSystem. Written exclusively by
    /// PowerCapacityResolverSystem.ApplySaturationInertia; added once by
    /// PowerCapacityIndexSystem.WriteGridModifiers. Present on grid producers
    /// (NOT OutsideConnection / EmergencyBattery — those force factor=1).
    ///
    /// PERSISTED (ISerializable): the inertia state must survive save/load, else a
    /// reloaded spam city resets to factor=1 and surplus-immunity returns. Mirrors
    /// PlantBaseCapacity serialization (KeyedSerializer + SerializationGuard).
    /// LastUpdateGameHours is RECONCILED in ValidateAfterLoad (not used raw post-load)
    /// so a large Δh does not instantly up-ramp the factor.
    /// </summary>
    public struct SaturationModifier : IComponentData, ISerializable
    {
        private const byte SAVE_VERSION = 1;

        /// <summary>Effective saturation factor with inertia; 1 = no penalty, SaturationFloor = max penalty.</summary>
        public float SaturationFactor;

        /// <summary>Persisted timestamp (GameTimeSystem.TotalGameHours), NOT ElapsedTime.</summary>
        public double LastUpdateGameHours;

        public void SetDefaults()
        {
            SaturationFactor = 1f;
            LastUpdateGameHours = 0.0;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "sat", SaturationFactor);
                KeyedSerializer.WriteField(writer, "luh", LastUpdateGameHours);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(SaturationModifier)))
            {
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
                            case "sat":
                                SaturationFactor = KeyedSerializer.ReadFloat(reader, tag, "sat", 1f);
                                break;
                            case "luh":
                                LastUpdateGameHours = KeyedSerializer.ReadDouble(reader, tag, "luh", 0.0);
                                break;
                            default:
                                KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                SaturationModifierLog.Log.Error($"Deserialize {nameof(SaturationModifier)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
