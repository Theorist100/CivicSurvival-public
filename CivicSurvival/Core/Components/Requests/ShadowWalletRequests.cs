using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to deduct from Shadow Wallet.
    /// Ephemeral entity pattern - created by any domain, processed by ShadowWalletSystem.
    ///
    /// All shadow wallet deductions go through this ECB request.
    /// Use IShadowWalletService.CanAffordWithPending() before creating to prevent overspend.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ShadowWalletDeductRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>
        /// Amount to deduct (positive integer, sanctions markup already applied).
        /// Populate via IShadowWalletService.CanAffordWithPending().EffectiveCost.
        /// </summary>
        public long Amount;

        /// <summary>Reason for deduction (for logging/tracking).</summary>
        public FixedString64Bytes Reason;

        /// <summary>
        /// Pending affordability reservation owned by this request.
        /// Zero means the request was loaded from an older save or did not reserve.
        /// </summary>
        public long ReservationAmount;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "rsn", Reason.ToString());
                KeyedSerializer.WriteField(writer, "res", ReservationAmount);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ShadowWalletDeductRequest)))
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
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "rsn": { string r = KeyedSerializer.ReadString(reader, tag, "rsn"); Reason = new FixedString64Bytes(r ?? ""); } break;
                            case "res": ReservationAmount = KeyedSerializer.ReadBoundedLong(reader, tag, "res", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Type of wallet control operation.
    /// </summary>
    public enum ShadowWalletControlType : byte
    {
        Freeze = 0,
        Unfreeze = 1,
        Confiscate = 2
    }

    /// <summary>
    /// Request to control Shadow Wallet state (freeze/unfreeze/confiscate).
    /// Ephemeral entity pattern - created by any domain, processed by ShadowWalletSystem.
    ///
    /// This decouples domains from ShadowEconomy:
    /// - CountermeasuresUpdateSystem creates requests for freeze/unfreeze/confiscate
    /// - ShadowWalletSystem processes requests in its OnUpdate
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ShadowWalletControlRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Type of control operation.</summary>
        public ShadowWalletControlType Type;

        /// <summary>Freeze reason (only used for Freeze operation).</summary>
        public FreezeReason FreezeReason;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteEnumByteField(writer, "type", (byte)Type);
                KeyedSerializer.WriteField(writer, "frzR", (int)(byte)FreezeReason);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ShadowWalletControlRequest)))
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
                            case "type": Type = KeyedSerializer.ReadEnumByte<TReader, ShadowWalletControlType>(reader, tag, "type", default); break;
#pragma warning disable CIVIC140 // FreezeReason is [Flags] — bitmask validation, not IsDefined
                            case "frzR": FreezeReason = (FreezeReason)((byte)KeyedSerializer.ReadBoundedInt(reader, tag, "frzR", 0, 255, 0) & (byte)FreezeReason.AllFlags); break;
#pragma warning restore CIVIC140
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
