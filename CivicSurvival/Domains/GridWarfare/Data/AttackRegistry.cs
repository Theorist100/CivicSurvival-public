using System;
using System.Collections.Generic;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.Domain.GridWarfare;

namespace CivicSurvival.Domains.GridWarfare.Data
{
    /// <summary>
    /// Definition of an attack type.
    /// Immutable metadata (category, names) lives here; balance values
    /// (cost, prepare duration, base damage) are resolved live from
    /// <see cref="BalanceConfig.Current"/> so remote-config tuning applies
    /// without rebuild.
    /// </summary>
    public readonly struct AttackDef
    {
        public readonly AttackCategory Category;
        public readonly string DisplayName;     // UI display name
        public readonly string Description;     // UI tooltip

        // Per-attack selectors into BalanceConfig.Current.GridWarfare. Each attack
        // binds its own config triple at construction, avoiding any magic numbers
        // in the offense path.
        private readonly Func<GridWarfareConfig, long> m_CostSelector;
        private readonly Func<GridWarfareConfig, float> m_PrepareSelector;
        private readonly Func<GridWarfareConfig, float> m_DamageSelector;

        public AttackDef(
            AttackCategory Category,
            Func<GridWarfareConfig, long> CostSelector,
            Func<GridWarfareConfig, float> PrepareSelector,
            Func<GridWarfareConfig, float> DamageSelector,
            string DisplayName,
            string Description)
        {
            this.Category = Category;
            this.m_CostSelector = CostSelector;
            this.m_PrepareSelector = PrepareSelector;
            this.m_DamageSelector = DamageSelector;
            this.DisplayName = DisplayName;
            this.Description = Description;
        }

        /// <summary>Shadow Cash base cost (before stability discount), from balance config.</summary>
        public long BaseCost => m_CostSelector(BalanceConfig.Current.GridWarfare);

        /// <summary>Seconds to prepare the operation, from balance config.</summary>
        public float PrepareDuration => m_PrepareSelector(BalanceConfig.Current.GridWarfare);

        /// <summary>Base pressure reduction %, from balance config.</summary>
        public float BaseDamage => m_DamageSelector(BalanceConfig.Current.GridWarfare);
    }

    /// <summary>
    /// Registry of available player attacks. Each attack's category selects the enemy
    /// axis it lowers (Kinetic→physical, Cyber→digital, Psyops→social).
    /// </summary>
    public static class AttackRegistry
    {
#pragma warning disable CIVIC148 // Immutable attack metadata — balance values resolve live from BalanceConfig
        private static readonly Dictionary<string, AttackDef> s_Attacks = new()
#pragma warning restore CIVIC148
        {
            ["drone"] = new AttackDef(
                Category: AttackCategory.Kinetic,
                CostSelector: static cfg => cfg.DroneCost,
                PrepareSelector: static cfg => cfg.DronePrepareDuration,
                DamageSelector: static cfg => cfg.DroneBaseDamage,
                DisplayName: "Drone Swarm",
                Description: "Deploy reconnaissance drones to strike enemy positions. Lowers the enemy physical axis."
            ),

            // BUG-3 FIX: Renamed from "Grid Blackout" - this attack only reduces pressure counter,
            // not actual power grid. Honest naming prevents player confusion.
            ["blackout"] = new AttackDef(
                Category: AttackCategory.Cyber,
                CostSelector: static cfg => cfg.BlackoutCost,
                PrepareSelector: static cfg => cfg.BlackoutPrepareDuration,
                DamageSelector: static cfg => cfg.BlackoutBaseDamage,
                DisplayName: "Cyber Pressure",
                Description: "Digital pressure on enemy command systems. Lowers the enemy digital axis."
            ),

            ["disinfo"] = new AttackDef(
                Category: AttackCategory.Psyops,
                CostSelector: static cfg => cfg.DisinfoCost,
                PrepareSelector: static cfg => cfg.DisinfoPrepareDuration,
                DamageSelector: static cfg => cfg.DisinfoBaseDamage,
                DisplayName: "Mass Disinfo",
                Description: "Information warfare to demoralize enemy forces. Lowers the enemy social axis."
            )
        };

        public static IReadOnlyDictionary<string, AttackDef> Attacks => s_Attacks;
    }
}
