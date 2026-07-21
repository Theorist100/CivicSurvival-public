using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Economy;

namespace CivicSurvival.Domains.Corruption.Data
{
    /// <summary>
    /// Vendor templates for procurement offers.
    /// Used by MaintenanceContractSystem to generate realistic offers.
    /// </summary>
    public static class ContractVendors
    {
        // === MAINTENANCE VENDORS ===
        public static readonly VendorTemplate[] MaintenanceOfficial = new[]
        {
            new VendorTemplate("EnergyTech Ltd", 0.98f, 1.0f),
            new VendorTemplate("PowerGrid Solutions", 0.97f, 1.05f),
            new VendorTemplate("National Energy Corp", 0.99f, 1.1f),
            new VendorTemplate("StateEnergy Services", 0.96f, 0.95f),
        };

        public static readonly VendorTemplate[] MaintenanceShady = new[]
        {
            new VendorTemplate("Mayor's Brother LLC", 0.70f, 0.50f, 0.20f),
            new VendorTemplate("Trustworthy Services", 0.65f, 0.45f, 0.25f),
            new VendorTemplate("Quality Maintenance Co", 0.75f, 0.55f, 0.15f),
            new VendorTemplate("Budget Solutions", 0.60f, 0.40f, 0.30f),
            new VendorTemplate("Friendly Contractors", 0.68f, 0.48f, 0.22f),
        };

        // === SUPPLY VENDORS ===
        public static readonly VendorTemplate[] SupplyOfficial = new[]
        {
            new VendorTemplate("National Fuel Corp", 0.98f, 1.0f),
            new VendorTemplate("Premium Coal Ltd", 0.97f, 1.05f),
            new VendorTemplate("StateEnergy Supplies", 0.99f, 1.1f),
        };

        public static readonly VendorTemplate[] SupplyShady = new[]
        {
            new VendorTemplate("Discount Fuel LLC", 0.75f, 0.55f, 0.15f),
            new VendorTemplate("Wet Coal Traders", 0.65f, 0.45f, 0.25f),
            new VendorTemplate("Friend's Imports", 0.70f, 0.50f, 0.20f),
            new VendorTemplate("Budget Supplies", 0.60f, 0.40f, 0.30f),
        };

        // === LEGACY ALIASES (for existing code) ===
#pragma warning disable CA1819 // Properties should not return arrays - returns cached static array
        public static VendorTemplate[] OfficialVendors => MaintenanceOfficial;
        public static VendorTemplate[] ShadyVendors => MaintenanceShady;
#pragma warning restore CA1819

        public static VendorTemplate GetRandomOfficial(uint seed, ContractType type = ContractType.Maintenance)
        {
            var vendors = type == ContractType.Supply ? SupplyOfficial : MaintenanceOfficial;
            if (vendors.Length == 0) return default;
            var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            return vendors[random.NextInt(vendors.Length)];
        }

        public static VendorTemplate GetRandomShady(uint seed, ContractType type = ContractType.Maintenance)
        {
            var vendors = type == ContractType.Supply ? SupplyShady : MaintenanceShady;
            if (vendors.Length == 0) return default;
            var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            return vendors[random.NextInt(vendors.Length)];
        }

        /// <summary>
        /// Resolve vendor name from hash. Returns "Unknown" if not found.
        /// </summary>
        public static string GetVendorNameByHash(int hash, ContractType type, bool isShady)
        {
            var vendors = (type, isShady) switch
            {
                (ContractType.Maintenance, false) => MaintenanceOfficial,
                (ContractType.Maintenance, true) => MaintenanceShady,
                (ContractType.Supply, false) => SupplyOfficial,
                (ContractType.Supply, true) => SupplyShady,
                _ => MaintenanceOfficial
            };

            foreach (var vendor in vendors)
            {
                if (vendor.GetNameHash() == hash)
                    return vendor.Name;
            }

            return "Unknown";
        }
    }

    /// <summary>
    /// Vendor template for generating procurement offers.
    /// </summary>
    public struct VendorTemplate
    {
        public string Name;

        /// <summary>Quality: 0.0-1.0 where 1.0 = perfect, 0.7 = shady standard</summary>
        public float Quality;

        /// <summary>Price multiplier vs base maintenance cost</summary>
#pragma warning disable CIVIC167 // Multiplier (0.7-1.3), not monetary amount
        public float PriceMultiplier;
#pragma warning restore CIVIC167

        /// <summary>Kickback as percent of savings (0 for official vendors)</summary>
        public float KickbackPercent;

        public VendorTemplate(string name, float quality, float priceMultiplier, float kickbackPercent = 0f)
        {
            Name = name;
            Quality = quality;
            PriceMultiplier = priceMultiplier;
            KickbackPercent = kickbackPercent;
        }

        /// <summary>
        /// Get deterministic hash for vendor name.
        /// Uses FNV-1a algorithm which is stable across .NET versions.
        /// </summary>
        public int GetNameHash()
        {
            if (string.IsNullOrEmpty(Name))
                return 0;

            // FNV-1a hash - deterministic across .NET versions
            unchecked
            {
                const int fnvPrime = 16777619;
                int hash = -2128831035; // FNV offset basis

                foreach (char c in Name)
                {
                    hash ^= c;
                    hash *= fnvPrime;
                }

                return hash;
            }
        }
    }
}
