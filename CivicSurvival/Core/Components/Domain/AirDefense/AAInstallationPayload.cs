using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// Player intent selected in the AA placement UI. Carried on the generic
    /// <c>BuildingPlacementPending</c>/<c>BuildingPlacementIntent</c> as the byte
    /// <c>Mode</c>; the AA handler maps it back to this enum.
    /// </summary>
    public enum AAPlacementMode : byte
    {
        Paid = 0,
        Heritage = 1,
        DonorCredit = 2,

        /// <summary>
        /// Bought off the books: the same barrel, tuned past its official spec, paid for out of the
        /// shadow wallet. Strictly better than the budget one — that is the temptation. The price
        /// is the corruption it takes to afford it, not a worse gun.
        /// </summary>
        BlackMarket = 3
    }

    /// <summary>
    /// Which granted credit a pending AA placement consumes. Carried on the generic
    /// <c>BuildingPlacementIntent.ReservedCreditKind</c> byte; the AA handler and the credit owner
    /// (<c>AirDefenseStateSystem</c>) map it back to this enum and return the credit on reject.
    /// Credits are counters only — money placements carry <c>None</c> here, and WHICH purse pays
    /// (city budget vs shadow wallet) travels separately in
    /// <c>BuildingPlacementIntent.PayingPurse</c> (<c>PlacementPurse</c>).
    /// </summary>
    public enum AAPlacementCreditKind : byte
    {
        /// <summary>No credit — a money placement (see <c>PlacementPurse</c> for the purse).</summary>
        None = 0,
        Heritage = 1,
        DonorPatriot = 2
    }

    /// <summary>
    /// AA-specific placement payload — the fully-resolved installation stats, stamped
    /// once at detection by <c>AABuildingPlacementHandler</c> and read at commit to
    /// build the <c>AirDefenseInstallation</c>. Lives on the same entity as the generic
    /// <c>BuildingPlacementIntent</c>; the generic pipeline owns the transaction state,
    /// this component owns the building-specific stats.
    ///
    /// Serializable so the placement-time-resolved stats survive a save taken between
    /// detection and commit (the city-scaled magazine is stamped once at placement and
    /// must not be re-derived from a possibly-changed city after load).
    /// </summary>
    public struct AAInstallationPayload : IComponentData, ISerializable
    {
        public AAType ResolvedType;
        public float Range;
        public float InterceptChanceShahed;
        public float InterceptChanceBallistic;
        public int MaxAmmo;
        public float CooldownDuration;
        public int CrewRequired;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;
        private const float DEFAULT_RANGE = 1200f;
        private const float MAX_RANGE = 5000f;
        private const float DEFAULT_COOLDOWN_DURATION = 30f;
        private const float MAX_COOLDOWN_DURATION = 600f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteEnumByteField(writer, "aaT", (byte)ResolvedType);
                KeyedSerializer.WriteField(writer, "rng", Range);
                KeyedSerializer.WriteField(writer, "icS", InterceptChanceShahed);
                KeyedSerializer.WriteField(writer, "icB", InterceptChanceBallistic);
                KeyedSerializer.WriteField(writer, "mxAm", MaxAmmo);
                KeyedSerializer.WriteField(writer, "cdDur", CooldownDuration);
                KeyedSerializer.WriteField(writer, "crew", CrewRequired);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AAInstallationPayload)))
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
                            case "aaT": ResolvedType = KeyedSerializer.ReadEnumByte<TReader, AAType>(reader, tag, "aaT", AAType.HeritageBofors); break;
                            case "rng": Range = KeyedSerializer.ReadSafeFloat(reader, tag, "rng", 1f, MAX_RANGE, DEFAULT_RANGE); break;
                            case "icS": InterceptChanceShahed = KeyedSerializer.ReadSafeFloat(reader, tag, "icS", 0f, 1f, 0f); break;
                            case "icB": InterceptChanceBallistic = KeyedSerializer.ReadSafeFloat(reader, tag, "icB", 0f, 1f, 0f); break;
                            case "mxAm": MaxAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "mxAm", 0, 10000, 0); break;
                            case "cdDur": CooldownDuration = KeyedSerializer.ReadSafeFloat(reader, tag, "cdDur", 0.1f, MAX_COOLDOWN_DURATION, DEFAULT_COOLDOWN_DURATION); break;
                            case "crew": CrewRequired = KeyedSerializer.ReadBoundedInt(reader, tag, "crew", 0, 100, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(AAInstallationPayload)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
