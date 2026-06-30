using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Penalty sources that can affect district happiness/commerce.
    /// Uses flags for efficient storage and checking.
    /// </summary>
    [Flags]
    public enum PenaltySource
    {
        None = 0,
        InternetDisabled = 1 << 0,    // SpotterSystem - unique mechanic, game doesn't know about it
        NeighborEnvy = 1 << 1,        // NeighborEnvySystem (Phase 5)
        WinterCold = 1 << 2,          // WinterMultiplierSystem
        VIPVisible = 1 << 3,          // Satire - VIP has power while others don't (Phase 5)
        // BONUS: negative penalty = +15% happiness. Flows through 3 linked points:
        // 1. DistrictPenaltySystem config (-0.15f) → 2. WRS job clamp (allows negative) → 3. BlackoutSystem.Serialization bounds (allows -1f).
        // Changing ANY of those 3 to reject negatives kills this bonus silently.
        FoodAidProvided = 1 << 4,
        Blackout = 1 << 5,            // BlackoutSystem - manual/load shedding blackout
        ScheduledBlackout = 1 << 6,   // BlackoutSystem - scheduled blackout (people prepared, lower penalty)
        InfrastructureCollapse = 1 << 7, // RefugeeSpawnSystem - water/sewage overwhelmed by refugees
        AutoDispatch = 1 << 8,        // AutoDispatchSystem - automatic load shedding (1.2x penalty multiplier)
        CognitiveCompromised = 1 << 9, // CognitiveWarfareSystem - cognitive integrity below threshold
        GretaDeployed = 1 << 10,       // CognitiveWarfareSystem - "lightning rod" decoy effect (effective but annoying)
        FirewallActive = 1 << 11,      // CognitiveWarfareSystem - global firewall mode (-10% commerce)
        InternetBlackout = 1 << 12,    // CognitiveWarfareSystem - global internet blackout (-25% commerce)
        ShadowExport = 1 << 13,        // ShadowEconomy - exporting power while this district is dark

        AllFlags = InternetDisabled
            | NeighborEnvy
            | WinterCold
            | VIPVisible
            | FoodAidProvided
            | Blackout
            | ScheduledBlackout
            | InfrastructureCollapse
            | AutoDispatch
            | CognitiveCompromised
            | GretaDeployed
            | FirewallActive
            | InternetBlackout
            | ShadowExport
    }

    public static class PenaltySources
    {
        public static PenaltySource Sanitize(int value)
        {
            PenaltySource sanitized = PenaltySource.None;
            AddIfPresent(value, PenaltySource.InternetDisabled, ref sanitized);
            AddIfPresent(value, PenaltySource.NeighborEnvy, ref sanitized);
            AddIfPresent(value, PenaltySource.WinterCold, ref sanitized);
            AddIfPresent(value, PenaltySource.VIPVisible, ref sanitized);
            AddIfPresent(value, PenaltySource.FoodAidProvided, ref sanitized);
            AddIfPresent(value, PenaltySource.Blackout, ref sanitized);
            AddIfPresent(value, PenaltySource.ScheduledBlackout, ref sanitized);
            AddIfPresent(value, PenaltySource.InfrastructureCollapse, ref sanitized);
            AddIfPresent(value, PenaltySource.AutoDispatch, ref sanitized);
            AddIfPresent(value, PenaltySource.CognitiveCompromised, ref sanitized);
            AddIfPresent(value, PenaltySource.GretaDeployed, ref sanitized);
            AddIfPresent(value, PenaltySource.FirewallActive, ref sanitized);
            AddIfPresent(value, PenaltySource.InternetBlackout, ref sanitized);
            AddIfPresent(value, PenaltySource.ShadowExport, ref sanitized);
            return sanitized;
        }

        private static void AddIfPresent(int value, PenaltySource source, ref PenaltySource sanitized)
        {
            if ((value & (int)source) != 0)
                sanitized |= source;
        }
    }
}
