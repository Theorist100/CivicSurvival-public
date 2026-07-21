using Unity.Entities;
using Game.Buildings;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Building type classification for damage calculations.
    /// </summary>
    public enum BuildingType
    {
        Unknown,
        Residential,
        Commercial,
        Industrial,
        Hospital,
        School,
        FireStation,
        PoliceStation,
        PowerPlant,
        WaterPumping
    }

    /// <summary>
    /// Classifies buildings and provides damage-related data.
    /// Centralizes building type logic to avoid scattered ComponentLookups.
    ///
    /// Usage:
    ///   var classifier = new BuildingClassifier(EntityManager);
    ///   var type = classifier.Classify(building);
    ///   var cost = classifier.GetEstimatedCost(type);
    /// </summary>
    public readonly struct BuildingClassifier
    {
        // ===== Estimated Building Replacement Costs =====
        private const int COST_POWER_PLANT = 50000;
        private const int COST_WATER_PUMPING = 25000;
        private const int COST_HOSPITAL = 30000;
        private const int COST_SCHOOL = 20000;
        private const int COST_SERVICE = 15000;
        private const int COST_INDUSTRIAL = 15000;
        private const int COST_COMMERCIAL = 10000;
        private const int COST_RESIDENTIAL = 5000;

        private readonly EntityManager m_EntityManager;

        public BuildingClassifier(EntityManager entityManager)
        {
            m_EntityManager = entityManager;
        }

        /// <summary>
        /// Classify building by its components.
        /// Order matters: more specific types first (Hospital before Residential).
        /// </summary>
        public BuildingType Classify(Entity building)
        {
            if (!m_EntityManager.Exists(building))
                return BuildingType.Unknown;

            // Critical infrastructure (check first)
            if (m_EntityManager.HasComponent<ElectricityProducer>(building))
                return BuildingType.PowerPlant;
            if (m_EntityManager.HasComponent<WaterPumpingStation>(building))
                return BuildingType.WaterPumping;

            // Service buildings
            if (m_EntityManager.HasComponent<Hospital>(building))
                return BuildingType.Hospital;
            if (m_EntityManager.HasComponent<School>(building))
                return BuildingType.School;
            if (m_EntityManager.HasComponent<FireStation>(building))
                return BuildingType.FireStation;
            if (m_EntityManager.HasComponent<PoliceStation>(building))
                return BuildingType.PoliceStation;

            // Zoned buildings
            if (m_EntityManager.HasComponent<CommercialProperty>(building))
                return BuildingType.Commercial;
            if (m_EntityManager.HasComponent<IndustrialProperty>(building))
                return BuildingType.Industrial;
            if (m_EntityManager.HasComponent<ResidentialProperty>(building))
                return BuildingType.Residential;

            return BuildingType.Unknown;
        }

        /// <summary>
        /// Check if building is critical infrastructure.
        /// </summary>
        public bool IsCritical(Entity building)
        {
            var type = Classify(building);
            return IsCritical(type);
        }

        /// <summary>
        /// Check if building type is critical infrastructure.
        /// </summary>
        public static bool IsCritical(BuildingType type)
        {
            return type switch
            {
                BuildingType.PowerPlant => true,
                BuildingType.WaterPumping => true,
                BuildingType.Hospital => true,
                BuildingType.School => true,
                BuildingType.FireStation => true,
                BuildingType.PoliceStation => true,
                BuildingType.Industrial => false,
                BuildingType.Commercial => false,
                BuildingType.Residential => false,
                BuildingType.Unknown => false,
                _ => false
            };
        }

        /// <summary>
        /// Estimate building replacement cost for damage statistics.
        /// </summary>
        public static int GetEstimatedCost(BuildingType type)
        {
            return type switch
            {
                BuildingType.PowerPlant => COST_POWER_PLANT,
                BuildingType.WaterPumping => COST_WATER_PUMPING,
                BuildingType.Hospital => COST_HOSPITAL,
                BuildingType.School => COST_SCHOOL,
                BuildingType.FireStation => COST_SERVICE,
                BuildingType.PoliceStation => COST_SERVICE,
                BuildingType.Industrial => COST_INDUSTRIAL,
                BuildingType.Commercial => COST_COMMERCIAL,
                BuildingType.Residential => COST_RESIDENTIAL,
                BuildingType.Unknown => 0,
                _ => 0
            };
        }

        // Base casualty counts per building type (doubled 2026-06-28 alongside routing casualties
        // through the vanilla deathcare pipeline). random.Next upper bound is exclusive. Hospital and
        // School live in BalanceConfig; the rest are fixed here — bump these to raise caps further.
        private const int CASUALTY_UTILITY = 10;          // PowerPlant / WaterPumping
        private const int CASUALTY_RESIDENTIAL_MIN = 4;
        private const int CASUALTY_RESIDENTIAL_MAX = 20;  // exclusive → up to 19
        private const int CASUALTY_COMMERCIAL_MIN = 2;
        private const int CASUALTY_COMMERCIAL_MAX = 10;   // exclusive → up to 9
        private const int CASUALTY_SERVICE_MIN = 2;       // Industrial / FireStation / PoliceStation
        private const int CASUALTY_SERVICE_MAX = 6;       // exclusive → up to 5

        /// <summary>
        /// Get base casualty count for building type.
        /// Uses BalanceConfig for configurable values.
        /// </summary>
        public static int GetBaseCasualties(BuildingType type, ref SerializableRandom random)
        {
            var config = BalanceConfig.Current.Threats;
            return type switch
            {
                BuildingType.Hospital => config.HospitalBaseCasualties,
                BuildingType.School => config.SchoolBaseCasualties,
                BuildingType.PowerPlant => CASUALTY_UTILITY,
                BuildingType.WaterPumping => CASUALTY_UTILITY,
                BuildingType.Residential => random.Next(CASUALTY_RESIDENTIAL_MIN, CASUALTY_RESIDENTIAL_MAX),
                BuildingType.Commercial => random.Next(CASUALTY_COMMERCIAL_MIN, CASUALTY_COMMERCIAL_MAX),
                BuildingType.Industrial => random.Next(CASUALTY_SERVICE_MIN, CASUALTY_SERVICE_MAX),
                BuildingType.FireStation => random.Next(CASUALTY_SERVICE_MIN, CASUALTY_SERVICE_MAX),
                BuildingType.PoliceStation => random.Next(CASUALTY_SERVICE_MIN, CASUALTY_SERVICE_MAX),
                BuildingType.Unknown => 0,
                _ => 0
            };
        }

        /// <summary>
        /// Get casualty type for narrative events.
        /// </summary>
        public static CasualtyType GetCasualtyType(BuildingType type)
        {
            return type switch
            {
                BuildingType.Hospital => CasualtyType.Hospital,
                BuildingType.School => CasualtyType.School,
                BuildingType.PowerPlant => CasualtyType.CriticalInfra,
                BuildingType.WaterPumping => CasualtyType.CriticalInfra,
                BuildingType.FireStation => CasualtyType.CriticalInfra,
                BuildingType.PoliceStation => CasualtyType.CriticalInfra,
                BuildingType.Industrial => CasualtyType.Residential,
                BuildingType.Commercial => CasualtyType.Residential,
                BuildingType.Residential => CasualtyType.Residential,
                BuildingType.Unknown => CasualtyType.Residential,
                _ => CasualtyType.Residential
            };
        }
    }
}
