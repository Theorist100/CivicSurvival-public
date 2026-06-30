namespace CivicSurvival
{
    /// <summary>
    /// Air attack intensity presets.
    /// Controls frequency and scale of threat waves.
    /// </summary>
    public enum AirAttackPreset
    {
        /// <summary>No air attacks - peaceful mode.</summary>
        Off = 0,

        /// <summary>Light attacks - rare waves, few threats (2-3).</summary>
        Light = 1,

        /// <summary>Normal attacks - standard frequency and intensity.</summary>
        Normal = 2,

        /// <summary>Heavy attacks - frequent waves, more threats (5-8).</summary>
        Heavy = 3,

        /// <summary>Overwhelming - constant pressure, massive waves.</summary>
        Overwhelming = 4
    }

    /// <summary>
    /// Difficulty preset types.
    /// Themed names that tell a story.
    /// </summary>
    public enum DifficultyPreset
    {
        /// <summary>
        /// "Managed Deficit" - Tutorial/Easy mode.
        /// Legal import: 100 MW. Shadow Import cheap ($300/MW).
        /// For learning mechanics without burning down the city.
        /// </summary>
        ManagedDeficit = 0,

        /// <summary>
        /// "Blackout Protocol" - The DEFAULT experience.
        /// Legal import: 30 MW (barely enough for water + hospital).
        /// Shadow Import standard price ($600/MW). Realistic survival.
        /// </summary>
        BlackoutProtocol = 1,

        /// <summary>
        /// "Island Mode" - Hardcore/Frostpunk-style.
        /// Legal import: 0 MW. Complete isolation.
        /// Shadow Import is ONLY external source, mafia prices ($1200/MW).
        /// </summary>
        IslandMode = 2,

        /// <summary>
        /// Custom settings - player has modified a preset.
        /// Tracks the base preset it was derived from.
        /// </summary>
        Custom = 3
    }

    /// <summary>
    /// Container for all difficulty-related settings.
    /// Used by presets and for serialization.
    /// </summary>
    public sealed class DifficultySettings : System.IEquatable<DifficultySettings>
    {
        // === Power Grid ===

        /// <summary>
        /// Legal import limit in MW from outside connections.
        /// 0 = completely isolated (Island Mode).
        /// </summary>
        public int LegalImportMW { get; set; } = 100;

        /// <summary>
        /// Legal export limit in MW through outside connections (interconnector capacity).
        /// 0 = no legal export. Vanilla pays from actual flow, so capping the flow caps
        /// the income with it.
        /// </summary>
        public int LegalExportMW { get; set; } = 0;

        /// <summary>
        /// Shadow Import price per MW per day.
        /// Higher = more expensive black market.
        /// </summary>
        public float ShadowImportPrice { get; set; } = 600f;

        // Challenge mechanics
        public bool ConstructionDelay { get; set; } = false;
        public bool RandomDisasters { get; set; } = false;
        public bool WinterMultiplier { get; set; } = false;
        // NOTE: CascadeEffect REMOVED - vanilla handles via EfficiencyFactor.ElectricitySupply
        public bool NeighborEnvy { get; set; } = false;

        // Grid stress mechanics
        /// <summary>
        /// Enable grid stress system - deficit accumulates stress leading to collapse.
        /// 2 hours of deficit = 24 hour full shutdown.
        /// </summary>
        public bool GridStress { get; set; } = false;

        /// <summary>
        /// Enable threshold operation - buildings below 90% power get nothing.
        /// Forces player to choose who gets power via blackout schedules.
        /// </summary>
        public bool ThresholdOperation { get; set; } = false;

        /// <summary>
        /// Enable equipment wear - overloaded plants degrade faster and can explode.
        /// Running >100% load = 10x faster wear.
        /// </summary>
        public bool EquipmentWear { get; set; } = false;

        // Tools/helpers
        public bool BackupPower { get; set; } = false;

        /// <summary>
        /// Protect critical infrastructure (hospitals, fire stations, water pumps).
        /// When true, these buildings never lose power during blackouts.
        /// </summary>
        public bool ProtectCriticalInfra { get; set; } = true;

        /// <summary>
        /// Winter severity multiplier: 0.67 = easy (max x2.0), 1.0 = normal (max x3.0), 1.33 = hardcore (max x4.0)
        /// Controls maximum winter consumption multiplier at -10°C.
        /// Formula: maxMultiplier = 1.0 + (2.0 * WinterSeverity)
        /// </summary>
        public float WinterSeverity { get; set; } = 1.0f;

        /// <summary>
        /// Air attack intensity preset.
        /// Controls frequency and scale of threat waves.
        /// </summary>
        public AirAttackPreset AirAttacks { get; set; } = AirAttackPreset.Off;

        /// <summary>
        /// Create a deep copy of settings.
        /// </summary>
        public DifficultySettings Clone()
        {
            return new DifficultySettings
            {
                LegalImportMW = this.LegalImportMW,
                LegalExportMW = this.LegalExportMW,
                ShadowImportPrice = this.ShadowImportPrice,
                ConstructionDelay = this.ConstructionDelay,
                RandomDisasters = this.RandomDisasters,
                WinterMultiplier = this.WinterMultiplier,
                WinterSeverity = this.WinterSeverity,
                NeighborEnvy = this.NeighborEnvy,
                GridStress = this.GridStress,
                ThresholdOperation = this.ThresholdOperation,
                EquipmentWear = this.EquipmentWear,
                BackupPower = this.BackupPower,
                ProtectCriticalInfra = this.ProtectCriticalInfra,
                AirAttacks = this.AirAttacks
            };
        }

        /// <summary>
        /// Check if settings match another set of settings.
        /// </summary>
        public bool Equals(DifficultySettings? other)
        {
            if (other == null) return false;

            return LegalImportMW == other.LegalImportMW
                && LegalExportMW == other.LegalExportMW
                && System.Math.Abs(ShadowImportPrice - other.ShadowImportPrice) < 0.01f
                && ConstructionDelay == other.ConstructionDelay
                && RandomDisasters == other.RandomDisasters
                && WinterMultiplier == other.WinterMultiplier
                && System.Math.Abs(WinterSeverity - other.WinterSeverity) < 0.01f
                && NeighborEnvy == other.NeighborEnvy
                && GridStress == other.GridStress
                && ThresholdOperation == other.ThresholdOperation
                && EquipmentWear == other.EquipmentWear
                && BackupPower == other.BackupPower
                && ProtectCriticalInfra == other.ProtectCriticalInfra
                && AirAttacks == other.AirAttacks;
        }

        public override bool Equals(object? obj) => Equals(obj as DifficultySettings);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + LegalImportMW;
                hash = hash * 31 + LegalExportMW;
                hash = hash * 31 + ShadowImportPrice.GetHashCode();
                hash = hash * 31 + (int)AirAttacks;
                return hash;
            }
        }
    }

    /// <summary>
    /// Static container for difficulty presets.
    /// Each preset defines a complete gameplay experience.
    /// </summary>
    public static class DifficultyPresets
    {
        // ===== Preset-specific values (not shared with DifficultySettings defaults) =====
        private const float MANAGED_DEFICIT_SHADOW_PRICE = 300f;
        private const float MANAGED_DEFICIT_WINTER_SEVERITY = 0.67f;
        // Peaceful income for the tutorial preset; revisit after the [TradePrice] log.
        // War presets (BlackoutProtocol / IslandMode) keep the engine default of 0.
        private const int MANAGED_DEFICIT_LEGAL_EXPORT = 50;
        private const int BLACKOUT_PROTOCOL_LEGAL_IMPORT = 30;
        private const float BLACKOUT_PROTOCOL_SHADOW_PRICE = 600f;
        private const float ISLAND_MODE_SHADOW_PRICE = 1200f;
        private const float ISLAND_MODE_WINTER_SEVERITY = 1.5f;

        /// <summary>
        /// "Managed Deficit" - Tutorial/Easy mode.
        /// Legal import: 100 MW. Shadow Import cheap ($300/MW).
        /// For learning mechanics without burning down the city.
        /// </summary>
        public static DifficultySettings ManagedDeficit => new DifficultySettings
        {
            LegalImportMW = 100,           // Enough for small city (5-10k pop)
            LegalExportMW = MANAGED_DEFICIT_LEGAL_EXPORT,
            ShadowImportPrice = MANAGED_DEFICIT_SHADOW_PRICE,      // Cheap - forgiving
            ConstructionDelay = false,
            RandomDisasters = false,
            WinterMultiplier = false,
            WinterSeverity = MANAGED_DEFICIT_WINTER_SEVERITY,        // Easy: max x2.0 multiplier (1 + 2*0.67 ≈ 2.34, clamped to 2.0)
            NeighborEnvy = false,
            GridStress = false,            // No collapse risk for learning
            ThresholdOperation = false,    // Full proportional distribution
            EquipmentWear = false,         // No degradation
            BackupPower = true,            // Available for learning
            ProtectCriticalInfra = true,
            AirAttacks = AirAttackPreset.Light  // Rare, few threats
        };

        /// <summary>
        /// "Blackout Protocol" - The DEFAULT experience.
        /// Legal import: 30 MW (barely enough for water + hospital + city hall).
        /// Shadow Import standard price ($600/MW). Realistic survival.
        /// </summary>
        public static DifficultySettings BlackoutProtocol => new DifficultySettings
        {
            LegalImportMW = BLACKOUT_PROTOCOL_LEGAL_IMPORT,            // Water + Hospital + City Hall only
            LegalExportMW = 0,             // Wartime: the interconnector does not sell out
            ShadowImportPrice = BLACKOUT_PROTOCOL_SHADOW_PRICE,      // Standard price
            ConstructionDelay = true,
            RandomDisasters = true,
            WinterMultiplier = true,
            WinterSeverity = 1.0f,         // Normal: max x3.0 multiplier (1 + 2*1.0 = 3.0)
            NeighborEnvy = true,
            GridStress = true,             // Collapse after 2h deficit
            ThresholdOperation = true,     // <90% power = cutoff
            EquipmentWear = true,          // Overload = degradation
            BackupPower = true,
            ProtectCriticalInfra = true,
            AirAttacks = AirAttackPreset.Normal
        };

        /// <summary>
        /// "Island Mode" - Hardcore/Frostpunk-style.
        /// Legal import: 0 MW. Complete isolation from outside grid.
        /// Shadow Import is ONLY external source, mafia prices ($1200/MW).
        /// </summary>
        public static DifficultySettings IslandMode => new DifficultySettings
        {
            LegalImportMW = 0,             // ZERO - complete isolation
            LegalExportMW = 0,             // ZERO - isolation cuts both directions
            ShadowImportPrice = ISLAND_MODE_SHADOW_PRICE,     // Mafia prices
            ConstructionDelay = true,
            RandomDisasters = true,
            WinterMultiplier = true,
            WinterSeverity = ISLAND_MODE_WINTER_SEVERITY,         // Hardcore: max x4.0 multiplier (1 + 2*1.5 = 4.0)
            NeighborEnvy = true,
            GridStress = true,             // Collapse = death sentence
            ThresholdOperation = true,     // No mercy mode
            EquipmentWear = true,          // Plants will explode
            BackupPower = true,            // Absolutely required
            ProtectCriticalInfra = false,  // Hardcore: even hospitals can lose power
            AirAttacks = AirAttackPreset.Heavy
        };

        /// <summary>
        /// Get settings for a preset type.
        /// </summary>
        public static DifficultySettings GetPreset(DifficultyPreset preset)
        {
            return preset switch
            {
                DifficultyPreset.ManagedDeficit => ManagedDeficit,
                DifficultyPreset.BlackoutProtocol => BlackoutProtocol,
                DifficultyPreset.IslandMode => IslandMode,
                DifficultyPreset.Custom => BlackoutProtocol.Clone(),  // Custom defaults to recommended
                _ => throw new System.ArgumentOutOfRangeException(nameof(preset), preset, null)
            };
        }

        /// <summary>
        /// Detect which preset (if any) matches the given settings.
        /// Returns Custom if no exact match found.
        /// </summary>
        public static DifficultyPreset DetectPreset(DifficultySettings settings)
        {
            if (settings.Equals(ManagedDeficit)) return DifficultyPreset.ManagedDeficit;
            if (settings.Equals(BlackoutProtocol)) return DifficultyPreset.BlackoutProtocol;
            if (settings.Equals(IslandMode)) return DifficultyPreset.IslandMode;
            return DifficultyPreset.Custom;
        }

        /// <summary>
        /// Find the closest base preset for custom settings.
        /// Used for "Custom (based on X)" display.
        /// </summary>
        public static DifficultyPreset FindClosestPreset(DifficultySettings settings)
        {
            // Count matching fields for each preset
            int managedScore = CountMatches(settings, ManagedDeficit);
            int blackoutScore = CountMatches(settings, BlackoutProtocol);
            int islandScore = CountMatches(settings, IslandMode);

            if (blackoutScore >= managedScore && blackoutScore >= islandScore)
                return DifficultyPreset.BlackoutProtocol;
            if (islandScore >= managedScore)
                return DifficultyPreset.IslandMode;
            return DifficultyPreset.ManagedDeficit;
        }

        private static int CountMatches(DifficultySettings a, DifficultySettings b)
        {
            int score = 0;
            if (a.LegalImportMW == b.LegalImportMW) score++;
            if (a.LegalExportMW == b.LegalExportMW) score++;
            if (System.Math.Abs(a.ShadowImportPrice - b.ShadowImportPrice) < 0.01f) score++;
            if (a.ConstructionDelay == b.ConstructionDelay) score++;
            if (a.RandomDisasters == b.RandomDisasters) score++;
            if (a.WinterMultiplier == b.WinterMultiplier) score++;
            if (System.Math.Abs(a.WinterSeverity - b.WinterSeverity) < 0.01f) score++;
            if (a.NeighborEnvy == b.NeighborEnvy) score++;
            if (a.GridStress == b.GridStress) score++;
            if (a.ThresholdOperation == b.ThresholdOperation) score++;
            if (a.EquipmentWear == b.EquipmentWear) score++;
            if (a.BackupPower == b.BackupPower) score++;
            if (a.ProtectCriticalInfra == b.ProtectCriticalInfra) score++;
            if (a.AirAttacks == b.AirAttacks) score++;
            return score;
        }
    }
}
