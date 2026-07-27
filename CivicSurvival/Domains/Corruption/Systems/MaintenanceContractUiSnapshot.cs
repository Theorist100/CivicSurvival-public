using CivicSurvival.Core.Components.Domain.Economy;
using System;

namespace CivicSurvival.Domains.Corruption.Systems
{
    internal readonly struct MaintenanceContractUiSnapshot : IEquatable<MaintenanceContractUiSnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public static MaintenanceContractUiSnapshot Empty { get; } = new(
            shadyContractCount: 0,
            totalContractCount: 0,
            activeContractsJson: "[]",
            hasPendingOffer: false,
            pendingOffer: PendingProcurementOfferRaw.Empty);

        public readonly int ShadyContractCount;
        public readonly int TotalContractCount;
        public readonly string ActiveContractsJson;
        public readonly bool HasPendingOffer;
        public readonly PendingProcurementOfferRaw PendingOffer;

        public MaintenanceContractUiSnapshot(
            int shadyContractCount,
            int totalContractCount,
            string activeContractsJson,
            bool hasPendingOffer,
            PendingProcurementOfferRaw pendingOffer)
        {
            ShadyContractCount = shadyContractCount;
            TotalContractCount = totalContractCount;
            ActiveContractsJson = string.IsNullOrEmpty(activeContractsJson) ? "[]" : activeContractsJson;
            HasPendingOffer = hasPendingOffer;
            PendingOffer = pendingOffer;
        }

        public bool Equals(MaintenanceContractUiSnapshot other)
            => ShadyContractCount == other.ShadyContractCount
               && TotalContractCount == other.TotalContractCount
               && string.Equals(ActiveContractsJson, other.ActiveContractsJson, StringComparison.Ordinal)
               && HasPendingOffer == other.HasPendingOffer
               && PendingOffer.Equals(other.PendingOffer);

        public override bool Equals(object? obj)
            => obj is MaintenanceContractUiSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + ShadyContractCount;
                hash = (hash * HashMultiplier) + TotalContractCount;
                hash = (hash * HashMultiplier) + StringComparer.Ordinal.GetHashCode(ActiveContractsJson ?? string.Empty);
                hash = (hash * HashMultiplier) + HasPendingOffer.GetHashCode();
                hash = (hash * HashMultiplier) + PendingOffer.GetHashCode();
                return hash;
            }
        }
    }

    internal readonly struct PendingProcurementOfferRaw : IEquatable<PendingProcurementOfferRaw>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public static PendingProcurementOfferRaw Empty => default;

        public readonly int EntityIndex;
        public readonly int EntityVersion;
        public readonly CityService Service;
        public readonly ContractType ContractType;
        public readonly int OfficialVendorHash;
        public readonly int ShadyVendorHash;
        public readonly int OfficialPrice;
        public readonly int ShadyPrice;
        public readonly int KickbackOffer;
        public readonly float OfficialQuality;
        public readonly float ShadyQuality;
        public readonly string BuildingName;

        public PendingProcurementOfferRaw(
            int entityIndex,
            int entityVersion,
            CityService service,
            ContractType contractType,
            int officialVendorHash,
            int shadyVendorHash,
            int officialPrice,
            int shadyPrice,
            int kickbackOffer,
            float officialQuality,
            float shadyQuality,
            string buildingName)
        {
            EntityIndex = entityIndex;
            EntityVersion = entityVersion;
            Service = service;
            ContractType = contractType;
            OfficialVendorHash = officialVendorHash;
            ShadyVendorHash = shadyVendorHash;
            OfficialPrice = officialPrice;
            ShadyPrice = shadyPrice;
            KickbackOffer = kickbackOffer;
            OfficialQuality = officialQuality;
            ShadyQuality = shadyQuality;
            BuildingName = string.IsNullOrEmpty(buildingName) ? $"Power Plant #{entityIndex}" : buildingName;
        }

        public bool Equals(PendingProcurementOfferRaw other)
            => EntityIndex == other.EntityIndex
               && EntityVersion == other.EntityVersion
               && Service == other.Service
               && ContractType == other.ContractType
               && OfficialVendorHash == other.OfficialVendorHash
               && ShadyVendorHash == other.ShadyVendorHash
               && OfficialPrice == other.OfficialPrice
               && ShadyPrice == other.ShadyPrice
               && KickbackOffer == other.KickbackOffer
               && OfficialQuality.Equals(other.OfficialQuality)
               && ShadyQuality.Equals(other.ShadyQuality)
               && string.Equals(BuildingName, other.BuildingName, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is PendingProcurementOfferRaw other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + EntityIndex;
                hash = (hash * HashMultiplier) + EntityVersion;
                hash = (hash * HashMultiplier) + Service.GetHashCode();
                hash = (hash * HashMultiplier) + ContractType.GetHashCode();
                hash = (hash * HashMultiplier) + OfficialVendorHash;
                hash = (hash * HashMultiplier) + ShadyVendorHash;
                hash = (hash * HashMultiplier) + OfficialPrice;
                hash = (hash * HashMultiplier) + ShadyPrice;
                hash = (hash * HashMultiplier) + KickbackOffer;
                hash = (hash * HashMultiplier) + OfficialQuality.GetHashCode();
                hash = (hash * HashMultiplier) + ShadyQuality.GetHashCode();
                hash = (hash * HashMultiplier) + StringComparer.Ordinal.GetHashCode(BuildingName ?? string.Empty);
                return hash;
            }
        }
    }
}
