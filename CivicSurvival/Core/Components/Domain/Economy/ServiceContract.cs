using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Economy
{
    internal static class PendingProcurementLog
    {
        public static readonly LogContext Log = new("PendingProcurement");
    }

    /// <summary>
    /// Type of city service for procurement contracts.
    /// Phase 1: Electricity only. Future: Roads, Water, Healthcare, etc.
    /// </summary>
    public enum CityService : byte
    {
        Electricity = 0,  // Power plants, substations
        Roads = 1,        // Road maintenance (future)
        Water = 2,        // Water treatment (future)
        Healthcare = 3,   // Hospitals (future)
        Fire = 4,         // Fire stations (future)
        Education = 5,    // Schools (future)
        Garbage = 6       // Waste management (future)
    }

    /// <summary>
    /// Type of contract - determines which game system reads Quality.
    /// </summary>
    public enum ContractType : byte
    {
        /// <summary>
        /// Maintenance contract: Quality affects disaster/failure chance.
        /// Low quality (0.7) → 1.3x disaster chance.
        /// </summary>
        Maintenance = 0,

        /// <summary>
        /// Supply contract: Quality affects efficiency/output.
        /// Low quality (0.7) → 85% efficiency (wet coal, bad fuel).
        /// </summary>
        Supply = 1
    }

    public enum PendingProcurementLifecycle : byte
    {
        Unknown = 0,
        Active = 1,
        Consumed = 2
    }

    /// <summary>
    /// Tag component: building currently has pending procurement choice.
    /// Enableable for fast state changes without structural changes.
    /// NOTE: Entity stored as Index+Version to avoid vanilla orphan detection (homeless spike bug).
    /// </summary>
    public struct PendingProcurement : IComponentData, IEnableableComponent, ISerializable
    {
        private const float DEFAULT_OFFICIAL_QUALITY = 0.98f;
        private const float DEFAULT_SHADY_QUALITY = 0.70f;

        /// <summary>Service type for this procurement offer</summary>
        public CityService Service;

        /// <summary>Contract type: Maintenance or Supply</summary>
        public ContractType Type;

        /// <summary>Target building reference (typed Index+Version).</summary>
        public BuildingRef TargetBuilding;

        /// <summary>Reconstructs TargetBuilding Entity from typed ref.</summary>
        public Entity GetTargetBuilding() => TargetBuilding.ToEntity();

        /// <summary>Official vendor price</summary>
        public int OfficialPrice;

        /// <summary>Shady vendor price (always lower)</summary>
        public int ShadyPrice;

        /// <summary>Kickback amount if player accepts shady offer</summary>
        public int KickbackOffer;

        /// <summary>Official vendor quality (typically 0.98)</summary>
        public float OfficialQuality;

        /// <summary>Shady vendor quality (typically 0.70)</summary>
        public float ShadyQuality;

        /// <summary>Hash of official vendor name for UI</summary>
        public int OfficialVendorHash;

        /// <summary>Hash of shady vendor name for UI</summary>
        public int ShadyVendorHash;

        /// <summary>Durable lifecycle discriminator; only Active offers are recoverable after load.</summary>
        public PendingProcurementLifecycle Lifecycle;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 11);
                KeyedSerializer.WriteEnumByteField(writer, "svc", (byte)Service);
                KeyedSerializer.WriteEnumByteField(writer, "cType", (byte)Type);
                KeyedSerializer.WriteEntityField(writer, "bldg", TargetBuilding.ToEntity());
                KeyedSerializer.WriteField(writer, "oP", OfficialPrice);
                KeyedSerializer.WriteField(writer, "sP", ShadyPrice);
                KeyedSerializer.WriteField(writer, "kick", KickbackOffer);
                KeyedSerializer.WriteField(writer, "oQ", OfficialQuality);
                KeyedSerializer.WriteField(writer, "sQ", ShadyQuality);
                KeyedSerializer.WriteField(writer, "oVH", OfficialVendorHash);
                KeyedSerializer.WriteField(writer, "sVH", ShadyVendorHash);
                KeyedSerializer.WriteEnumByteField(writer, "state", (byte)Lifecycle);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PendingProcurement)))
            { SetDefaults(); return; }
            try
            {
                SetDefaults();
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "svc": Service = KeyedSerializer.ReadEnumByte<TReader, CityService>(reader, tag, "svc", CityService.Electricity); break;
                            case "cType": Type = KeyedSerializer.ReadEnumByte<TReader, ContractType>(reader, tag, "cType", ContractType.Maintenance); break;
                            case "bldg": TargetBuilding = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "oP": OfficialPrice = KeyedSerializer.ReadBoundedInt(reader, tag, "oP", 0, int.MaxValue, 0); break;
                            case "sP": ShadyPrice = KeyedSerializer.ReadBoundedInt(reader, tag, "sP", 0, int.MaxValue, 0); break;
                            case "kick": KickbackOffer = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "oQ": OfficialQuality = KeyedSerializer.ReadSafeFloat(reader, tag, "oQ", 0f, 1f, DEFAULT_OFFICIAL_QUALITY); break;
                            case "sQ": ShadyQuality = KeyedSerializer.ReadSafeFloat(reader, tag, "sQ", 0f, 1f, DEFAULT_SHADY_QUALITY); break;
                            case "oVH": OfficialVendorHash = KeyedSerializer.ReadInt(reader, tag, "oVH"); break;
                            case "sVH": ShadyVendorHash = KeyedSerializer.ReadInt(reader, tag, "sVH"); break;
                            case "state": Lifecycle = KeyedSerializer.ReadEnumByte<TReader, PendingProcurementLifecycle>(reader, tag, "state", PendingProcurementLifecycle.Unknown); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
                if (version < 2 && Lifecycle == PendingProcurementLifecycle.Unknown)
                    Lifecycle = PendingProcurementLifecycle.Active;
            }
            catch (System.Exception ex)
            {
                PendingProcurementLog.Log.Error($"Deserialize {nameof(PendingProcurement)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults()
        {
            this = default;
            OfficialQuality = DEFAULT_OFFICIAL_QUALITY;
            ShadyQuality = DEFAULT_SHADY_QUALITY;
        }
    }

    /// <summary>
    /// Snapshot of an accepted procurement offer waiting for retained budget resolution.
    /// Lives on the same entity as BudgetDeductRequest/BudgetDeductResult.
    /// </summary>
    public struct ContractPaymentIntent : IComponentData, ISerializable
    {
        public BuildingRef Building;
        public CityService Service;
        public ContractType Type;
        public bool IsShady;
        public long Price;
        public int KickbackAmount;
        public float Quality;
        public int VendorNameHash;
        public int RequestId;
        public double RequestCreatedTime;
        public uint RequestCreatedFrame;
        public bool RefundQueued;
        public bool RefundResolved;
        public bool RefundSucceeded;
        public bool RefundCleanupQueued;
        public FixedString128Bytes RefundOperationKey;

        private const byte SAVE_VERSION = 5;

        public Entity GetBuilding() => Building.ToEntity();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 15);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "svc", (byte)Service);
                KeyedSerializer.WriteEnumByteField(writer, "typ", (byte)Type);
                KeyedSerializer.WriteField(writer, "shady", IsShady);
                KeyedSerializer.WriteField(writer, "price", Price);
                KeyedSerializer.WriteField(writer, "kick", KickbackAmount);
                KeyedSerializer.WriteField(writer, "qual", Quality);
                KeyedSerializer.WriteField(writer, "vendor", VendorNameHash);
                KeyedSerializer.WriteField(writer, "req", RequestId);
                KeyedSerializer.WriteField(writer, "reqT", RequestCreatedTime);
                KeyedSerializer.WriteField(writer, "reqF", (long)RequestCreatedFrame);
                KeyedSerializer.WriteField(writer, "rQ", RefundQueued);
                KeyedSerializer.WriteField(writer, "rRes", RefundResolved);
                KeyedSerializer.WriteField(writer, "rOk", RefundSucceeded);
                KeyedSerializer.WriteField(writer, "rOp", RefundOperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ContractPaymentIntent)))
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
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "svc": Service = KeyedSerializer.ReadEnumByte<TReader, CityService>(reader, tag, "svc", CityService.Electricity); break;
                            case "typ": Type = KeyedSerializer.ReadEnumByte<TReader, ContractType>(reader, tag, "typ", ContractType.Maintenance); break;
                            case "shady": IsShady = KeyedSerializer.ReadBool(reader, tag, "shady"); break;
                            case "price": Price = KeyedSerializer.ReadBoundedLong(reader, tag, "price", 0, long.MaxValue); break;
                            case "kick": KickbackAmount = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "qual": Quality = KeyedSerializer.ReadSafeFloat(reader, tag, "qual", 0f, 1f, 0f); break;
                            case "vendor": VendorNameHash = KeyedSerializer.ReadInt(reader, tag, "vendor"); break;
                            case "req": RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "req", 0, int.MaxValue, 0); break;
                            case "reqT": RequestCreatedTime = KeyedSerializer.ReadDouble(reader, tag, "reqT"); break;
                            case "reqF": RequestCreatedFrame = (uint)KeyedSerializer.ReadBoundedLong(reader, tag, "reqF", 0, uint.MaxValue); break;
                            case "rQ": RefundQueued = KeyedSerializer.ReadBool(reader, tag, "rQ"); break;
                            case "rRes": RefundResolved = KeyedSerializer.ReadBool(reader, tag, "rRes"); break;
                            case "rOk": RefundSucceeded = KeyedSerializer.ReadBool(reader, tag, "rOk"); break;
                            case "rOp": { string s = KeyedSerializer.ReadString(reader, tag, "rOp"); RefundOperationKey = new FixedString128Bytes(s ?? ""); break; }
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PendingProcurementLog.Log.Error($"Deserialize {nameof(ContractPaymentIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults() => this = default;
    }
}


