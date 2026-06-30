using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class ThreatPositionLog
    {
        public static readonly LogContext Log = new("ThreatPosition");
    }

    /// <summary>
    /// Position and rotation for threat entities (Shahed, Ballistic).
    ///
    /// CRITICAL: This component replaces Game.Objects.Transform for threats.
    /// Game.Objects.Transform causes crashes because CS2's internal Jobs try to process
    /// our entities as if they were game objects.
    ///
    /// ThreatPosition is invisible to game systems — only our mod reads/writes it.
    /// NOTE: FallingDebris no longer uses ThreatPosition — DebrisSystem uses FallingDebris.FallPosition only.
    ///
    /// PERSISTENT (C1): SSOT of threat position — serialized so a restored drone keeps its
    /// in-flight position. DroneRenderWriteJob mirrors it into vanilla Transform on the first
    /// loaded frame; ThreatLoadRenderReinitSystem preseeds TransformFrame from this on load.
    ///
    /// Written by: ThreatSpawnSystem
    /// Updated by: ThreatMovementSystem, BallisticMovementJobEntity
    /// Read by: ThreatArrivalSystem, ThreatDamageSystem, AirDefense, CameraTrackingSystem
    /// </summary>
    public struct ThreatPosition : IComponentData, ISerializable
    {
        /// <summary>World position of the threat.</summary>
        public float3 Position;

        /// <summary>Rotation quaternion (facing direction).</summary>
        public quaternion Rotation;

        /// <summary>Current velocity (direction * speed). Used by ThreatMovementSystem for TransformFrame writes.</summary>
        public float3 Velocity;

        public ThreatPosition(float3 position, quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
            Velocity = float3.zero;
        }

        private const byte SAVE_VERSION = 1;
        // Below this squared length a deserialized quaternion is treated as degenerate (→ identity).
        private const float MIN_QUATERNION_LENGTH_SQ = 1e-6f;

        public void SetDefaults()
        {
            Position = float3.zero;
            Rotation = quaternion.identity;
            Velocity = float3.zero;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 10);
                KeyedSerializer.WriteField(writer, "pX", Position.x);
                KeyedSerializer.WriteField(writer, "pY", Position.y);
                KeyedSerializer.WriteField(writer, "pZ", Position.z);
                KeyedSerializer.WriteField(writer, "rX", Rotation.value.x);
                KeyedSerializer.WriteField(writer, "rY", Rotation.value.y);
                KeyedSerializer.WriteField(writer, "rZ", Rotation.value.z);
                KeyedSerializer.WriteField(writer, "rW", Rotation.value.w);
                KeyedSerializer.WriteField(writer, "vX", Velocity.x);
                KeyedSerializer.WriteField(writer, "vY", Velocity.y);
                KeyedSerializer.WriteField(writer, "vZ", Velocity.z);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ThreatPosition)))
            { return; }
            try
            {
                if (version >= 1)
                {
                    float4 rot = new float4(0f, 0f, 0f, 1f);
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "pX": Position.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "pX", 0f); break;
                            case "pY": Position.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "pY", 0f); break;
                            case "pZ": Position.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "pZ", 0f); break;
                            case "rX": rot.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "rX", 0f); break;
                            case "rY": rot.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "rY", 0f); break;
                            case "rZ": rot.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "rZ", 0f); break;
                            case "rW": rot.w = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "rW", 1f); break;
                            case "vX": Velocity.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "vX", 0f); break;
                            case "vY": Velocity.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "vY", 0f); break;
                            case "vZ": Velocity.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "vZ", 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                    // Re-normalize: a degenerate (all-zero / non-unit) quaternion would make
                    // every downstream rotation NaN. Fall back to identity if unrecoverable.
                    float lenSq = math.lengthsq(rot);
                    Rotation = lenSq > MIN_QUATERNION_LENGTH_SQ ? math.normalize(new quaternion(rot)) : quaternion.identity;
                }
            }
            catch (System.Exception ex)
            {
                ThreatPositionLog.Log.Error($"Deserialize {nameof(ThreatPosition)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
