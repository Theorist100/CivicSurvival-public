using Unity.Entities;
using Unity.Collections;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Metadata attached to UI command request entities.
    /// Request payload components stay pure; terminal state is emitted separately as RequestResultEvent.
    /// </summary>
    public struct RequestMeta : IComponentData, ISerializable
    {
        public int RequestId;
        public double CreatedTime;
        public uint CreatedFrame;
        public FixedString32Bytes DiscriminatorKind;
        public FixedString64Bytes DiscriminatorValue;

        private const byte SAVE_VERSION = 2;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "rid", RequestId);
                KeyedSerializer.WriteField(writer, "ct", CreatedTime);
                KeyedSerializer.WriteField(writer, "cf", (long)CreatedFrame);
                KeyedSerializer.WriteField(writer, "dk", DiscriminatorKind.ToString());
                KeyedSerializer.WriteField(writer, "dv", DiscriminatorValue.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(RequestMeta)))
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
                            case "rid":
                                RequestId = KeyedSerializer.ReadInt(reader, tag, "rid");
                                break;
                            case "ct":
                                CreatedTime = KeyedSerializer.ReadDouble(reader, tag, "ct");
                                break;
                            case "cf":
                                CreatedFrame = (uint)KeyedSerializer.ReadBoundedLong(reader, tag, "cf", 0, uint.MaxValue);
                                break;
                            case "dk":
                                DiscriminatorKind = new FixedString32Bytes(KeyedSerializer.ReadString(reader, tag, "dk"));
                                break;
                            case "dv":
                                DiscriminatorValue = new FixedString64Bytes(KeyedSerializer.ReadString(reader, tag, "dv"));
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
                RequestDeserializeLog.Log.Error($"Deserialize {nameof(RequestMeta)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
