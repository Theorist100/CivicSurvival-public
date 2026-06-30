using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Shadow import persistent state (ECS singleton, CrossDomain).
    /// Split from ShadowTradeState — import cluster only.
    ///
    /// Writer: ShadowTradeDailySystem (sole owner)
    /// Readers: ShadowImportUISystem (PowerGrid), ElectricityPatch (Patches)
    /// </summary>
    public struct ShadowImportState : IComponentData
    {
        private const float FLOAT_EPSILON = 0.0001f;
        private const double DOUBLE_EPSILON = 0.0001;

        /// <summary>Current import in MW (0 to MaxImportMW).</summary>
        public int ImportMW;

        /// <summary>Days continuously active (for risk calculation).</summary>
        public int ImportDaysActive;

        /// <summary>Current discovery risk (0.0 to 1.0).</summary>
        public float ImportDiscoveryRisk;

        /// <summary>True if caught and banned from importing.</summary>
        public bool ImportIsSanctioned;

        /// <summary>Days remaining on sanctions (0 if not sanctioned).</summary>
        public int ImportSanctionDaysRemaining;

        /// <summary>Track day transitions for risk decay.</summary>
        public bool ImportWasActiveYesterday;

        /// <summary>Random state for deterministic discovery rolls.</summary>
        public uint RngState;

        /// <summary>
        /// Ensures the shadow trade state is represented by exactly one paired entity.
        /// Called once from ShadowWalletSystem.OnCreate and safe to call during repair.
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            var canonical = CivicSingleton.EnsurePaired(
                em,
                CreateDefault(),
                ShadowExportState.CreateDefault(),
                new EnsurePairedPolicy<ShadowImportState, ShadowExportState>
                {
                    MergeDuplicate = MergeDuplicate,
                    EnsureShape = EnsureShape
                });
            em.SetName(canonical, "ShadowTradeState");
        }

        private static void MergeDuplicate(EntityManager em, Entity canonical, Entity duplicate)
        {
            if (em.HasComponent<ShadowImportState>(duplicate) && ShouldPrefer(em.GetComponentData<ShadowImportState>(duplicate), em.GetComponentData<ShadowImportState>(canonical)))
                em.SetComponentData(canonical, em.GetComponentData<ShadowImportState>(duplicate));
            if (em.HasComponent<ShadowExportState>(duplicate) && ShouldPrefer(em.GetComponentData<ShadowExportState>(duplicate), em.GetComponentData<ShadowExportState>(canonical)))
                em.SetComponentData(canonical, em.GetComponentData<ShadowExportState>(duplicate));
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<ShadowImportState>(entity))
                em.AddComponentData(entity, CreateDefault());
            if (!em.HasComponent<ShadowExportState>(entity))
                em.AddComponentData(entity, ShadowExportState.CreateDefault());
        }

        private static bool ShouldPrefer(ShadowImportState candidate, ShadowImportState current)
        {
            return IsDefault(current) && !IsDefault(candidate);
        }

        private static bool ShouldPrefer(ShadowExportState candidate, ShadowExportState current)
        {
            return IsDefault(current) && !IsDefault(candidate);
        }

        private static bool IsDefault(ShadowImportState state)
        {
            var def = CreateDefault();
            return state.ImportMW == def.ImportMW
                && state.ImportDaysActive == def.ImportDaysActive
                && math.abs(state.ImportDiscoveryRisk - def.ImportDiscoveryRisk) < FLOAT_EPSILON
                && state.ImportIsSanctioned == def.ImportIsSanctioned
                && state.ImportSanctionDaysRemaining == def.ImportSanctionDaysRemaining
                && state.ImportWasActiveYesterday == def.ImportWasActiveYesterday
                && state.RngState == def.RngState;
        }

        private static bool IsDefault(ShadowExportState state)
        {
            var def = ShadowExportState.CreateDefault();
            return state.ExportPercentage == def.ExportPercentage
                && state.ExportedMW == def.ExportedMW
                && state.ExportDailyIncome == def.ExportDailyIncome
                && System.Math.Abs(state.ExportLastAccumulationTime - def.ExportLastAccumulationTime) < DOUBLE_EPSILON
                && System.Math.Abs(state.ExportIncomeRemainder - def.ExportIncomeRemainder) < DOUBLE_EPSILON
                && state.SuspicionCooldown == def.SuspicionCooldown
                && state.RngState == def.RngState;
        }

        public static ShadowImportState CreateDefault()
        {
            return new ShadowImportState
            {
                ImportMW = 0,
                ImportDaysActive = 0,
                ImportDiscoveryRisk = 0f,
                ImportIsSanctioned = false,
                ImportSanctionDaysRemaining = 0,
                ImportWasActiveYesterday = false,
#pragma warning disable CIVIC156 // Deterministic ECS seed: re-seeded from save on deserialize
                RngState = new Random(0x5E07u).state // Stable ShadowEnergy deterministic seed.
#pragma warning restore CIVIC156
            };
        }
    }
}
