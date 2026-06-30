using System;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Corruption;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using Unity.Entities;

namespace CivicSurvival.Domains.Corruption.Systems
{
    internal readonly struct CorruptionStateInputSnapshot : IEquatable<CorruptionStateInputSnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public CorruptionStateInputSnapshot(
            bool hasShadowExport,
            int exportPercentage,
            bool hasShadowWallet,
            long offshoreBalance,
            bool hasEmergencyFund,
            decimal emergencyFundWithdrawn,
            bool hasFuelSiphoning,
            int fuelSiphonPercent,
            bool hasContractStats,
            int shadyContractCount,
            bool hasDistrictState,
            int vipDistrictCount,
            int vipBypassCount)
        {
            HasShadowExport = hasShadowExport;
            ExportPercentage = exportPercentage;
            HasShadowWallet = hasShadowWallet;
            OffshoreBalance = offshoreBalance;
            HasEmergencyFund = hasEmergencyFund;
            EmergencyFundWithdrawn = emergencyFundWithdrawn;
            HasFuelSiphoning = hasFuelSiphoning;
            FuelSiphonPercent = fuelSiphonPercent;
            HasContractStats = hasContractStats;
            ShadyContractCount = shadyContractCount;
            HasDistrictState = hasDistrictState;
            VIPDistrictCount = vipDistrictCount;
            VIPBypassCount = vipBypassCount;
        }

        public readonly bool HasShadowExport;
        public readonly int ExportPercentage;
        public readonly bool HasShadowWallet;
        public readonly long OffshoreBalance;
        public readonly bool HasEmergencyFund;
        public readonly decimal EmergencyFundWithdrawn;
        public readonly bool HasFuelSiphoning;
        public readonly int FuelSiphonPercent;
        public readonly bool HasContractStats;
        public readonly int ShadyContractCount;
        public readonly bool HasDistrictState;
        public readonly int VIPDistrictCount;
        public readonly int VIPBypassCount;

        /// <summary>All-zero/false snapshot — used as VersionedView seed before first publish.</summary>
        public static CorruptionStateInputSnapshot Empty { get; } = new(
            hasShadowExport: false, exportPercentage: 0,
            hasShadowWallet: false, offshoreBalance: 0,
            hasEmergencyFund: false, emergencyFundWithdrawn: 0m,
            hasFuelSiphoning: false, fuelSiphonPercent: 0,
            hasContractStats: false, shadyContractCount: 0,
            hasDistrictState: false, vipDistrictCount: 0, vipBypassCount: 0);

        public bool Equals(CorruptionStateInputSnapshot other)
            => HasShadowExport == other.HasShadowExport
               && ExportPercentage == other.ExportPercentage
               && HasShadowWallet == other.HasShadowWallet
               && OffshoreBalance == other.OffshoreBalance
               && HasEmergencyFund == other.HasEmergencyFund
               && EmergencyFundWithdrawn.Equals(other.EmergencyFundWithdrawn)
               && HasFuelSiphoning == other.HasFuelSiphoning
               && FuelSiphonPercent == other.FuelSiphonPercent
               && HasContractStats == other.HasContractStats
               && ShadyContractCount == other.ShadyContractCount
               && HasDistrictState == other.HasDistrictState
               && VIPDistrictCount == other.VIPDistrictCount
               && VIPBypassCount == other.VIPBypassCount;

        public override bool Equals(object? obj)
            => obj is CorruptionStateInputSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + HasShadowExport.GetHashCode();
                hash = (hash * HashMultiplier) + ExportPercentage;
                hash = (hash * HashMultiplier) + HasShadowWallet.GetHashCode();
                hash = (hash * HashMultiplier) + OffshoreBalance.GetHashCode();
                hash = (hash * HashMultiplier) + HasEmergencyFund.GetHashCode();
                hash = (hash * HashMultiplier) + EmergencyFundWithdrawn.GetHashCode();
                hash = (hash * HashMultiplier) + HasFuelSiphoning.GetHashCode();
                hash = (hash * HashMultiplier) + FuelSiphonPercent;
                hash = (hash * HashMultiplier) + HasContractStats.GetHashCode();
                hash = (hash * HashMultiplier) + ShadyContractCount;
                hash = (hash * HashMultiplier) + HasDistrictState.GetHashCode();
                hash = (hash * HashMultiplier) + VIPDistrictCount;
                hash = (hash * HashMultiplier) + VIPBypassCount;
                return hash;
            }
        }
    }

    internal sealed class CorruptionStateInputSnapshotPublisher
    {
        public void Publish(
            VersionedView<CorruptionStateInputSnapshot> view,
            EntityQuery shadowExportQuery,
            EntityQuery shadowWalletQuery,
            EntityQuery emergencyFundQuery,
            EntityQuery fuelSiphoningQuery,
            EntityQuery contractStatsQuery,
            IDistrictStateReader? districtState)
        {
            bool hasShadowExport = shadowExportQuery.TryGetSingleton<ShadowExportState>(out var shadowExport);
            bool hasShadowWallet = shadowWalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet);
            bool hasEmergencyFund = emergencyFundQuery.TryGetSingleton<EmergencyFundSingleton>(out var emergencyFund);
            bool hasFuelSiphoning = fuelSiphoningQuery.TryGetSingleton<FuelSiphoningSingleton>(out var fuelSiphoning);
            bool hasContractStats = contractStatsQuery.TryGetSingleton<ContractStatsSingleton>(out var contracts);

            int vipDistrictCount = 0;
            int vipBypassCount = 0;
            bool hasDistrictState = districtState != null;
            if (hasDistrictState)
            {
                var districtSnapshot = districtState!.TakeSnapshot();
                vipDistrictCount = districtSnapshot.VIPDistricts != null ? districtSnapshot.VIPDistricts.Count : 0;
                vipBypassCount = districtSnapshot.VIPBypass != null ? districtSnapshot.VIPBypass.Count : 0;
            }

            view.Publish(new CorruptionStateInputSnapshot(
                hasShadowExport,
                hasShadowExport ? shadowExport.ExportPercentage : 0,
                hasShadowWallet,
                hasShadowWallet ? wallet.GetTotalBalance() : 0,
                hasEmergencyFund,
                hasEmergencyFund ? (decimal)emergencyFund.WithdrawnAmount : 0m,
                hasFuelSiphoning,
                hasFuelSiphoning ? fuelSiphoning.SiphonPercent : 0,
                hasContractStats,
                hasContractStats ? contracts.ShadyContractCount : 0,
                hasDistrictState,
                vipDistrictCount,
                vipBypassCount));
        }
    }
}
