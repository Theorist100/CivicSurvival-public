using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Economy
{
    internal static class ContractDataLog
    {
        public static readonly LogContext Log = new("ContractData");
    }

    /// <summary>
    /// Service contract on separate mod entity.
    /// References vanilla building via Index+Version (NEVER Entity field!).
    ///
    /// IMPORTANT: This is on a SEPARATE entity from the vanilla building.
    /// We NEVER AddComponent to vanilla buildings (causes homeless spike cascade).
    ///
    /// Pattern: Same as AirDefenseInstallation (ThreatData.cs:317-405).
    /// </summary>
    public struct ContractData : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>Building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Which service this contract is for</summary>
        public CityService Service;

        /// <summary>Type of contract: Maintenance (disasters) or Supply (efficiency)</summary>
        public ContractType Type;

        /// <summary>
        /// Contract quality: 0.0-1.0 where 1.0 = official quality (98%), 0.7 = shady (70%)
        /// Lower quality = higher disaster/failure chance.
        /// </summary>
        public float Quality;

        /// <summary>Kickback money player received (0 for official contracts)</summary>
        public int KickbackAmount;

        /// <summary>Game day when contract was signed</summary>
        public int ContractStartDay;

        /// <summary>Contract duration in game days (default: 365 = 1 year)</summary>
        public int ContractDurationDays;

        /// <summary>Quick check: was this a corrupt deal?</summary>
        public bool IsShady;

        /// <summary>Vendor name hash for UI display</summary>
        public int VendorNameHash;

        /// <summary>Reconstruct building Entity from typed ref.</summary>
        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 9);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "svc", (byte)Service);
                KeyedSerializer.WriteEnumByteField(writer, "cType", (byte)Type);
                KeyedSerializer.WriteField(writer, "qual", Quality);
                KeyedSerializer.WriteField(writer, "kick", KickbackAmount);
                KeyedSerializer.WriteField(writer, "stDay", ContractStartDay);
                KeyedSerializer.WriteField(writer, "durD", NormalizeDurationDays(ContractDurationDays));
                KeyedSerializer.WriteField(writer, "shady", IsShady);
                KeyedSerializer.WriteField(writer, "vnH", VendorNameHash);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ContractData)))
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
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "svc": Service = KeyedSerializer.ReadEnumByte<TReader, CityService>(reader, tag, "svc", CityService.Electricity); break;
                            case "cType": Type = KeyedSerializer.ReadEnumByte<TReader, ContractType>(reader, tag, "cType", ContractType.Maintenance); break;
                            case "qual": Quality = KeyedSerializer.ReadSafeFloat(reader, tag, "qual", 0f, 1f, 0.98f); break;
                            case "kick": KickbackAmount = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "stDay": ContractStartDay = KeyedSerializer.ReadBoundedInt(reader, tag, "stDay", 0, 100000, 0); break;
                            case "durD": ContractDurationDays = KeyedSerializer.ReadBoundedInt(reader, tag, "durD", 1, 10000, GetDefaultDurationDays()); break;
                            case "shady": IsShady = KeyedSerializer.ReadBool(reader, tag, "shady"); break;
                            case "vnH": VendorNameHash = KeyedSerializer.ReadInt(reader, tag, "vnH"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ContractDataLog.Log.Error($"Deserialize {nameof(ContractData)} failed: {ex}");
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
            Quality = 0.98f;
            ContractDurationDays = GetDefaultDurationDays();
        }

        private static int GetDefaultDurationDays() =>
            NormalizeDurationDays(BalanceConfig.Current.Procurement.ContractDurationDays);

        private static int NormalizeDurationDays(int durationDays) =>
            System.Math.Clamp(durationDays, 1, 10000);
    }
}


