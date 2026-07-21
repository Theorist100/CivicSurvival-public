using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Shadow export persistent state (ECS singleton, CrossDomain).
    /// Split from ShadowTradeState — export cluster only.
    ///
    /// Writer: ShadowTradeDailySystem (sole owner)
    /// Readers: ShadowExportUISystem (PowerGrid), PowerGridDataSystem (PowerGrid),
    ///          CorruptionStateUpdateSystem (Economy)
    /// </summary>
    public struct ShadowExportState : IComponentData
    {
        /// <summary>Export percentage (0-100).</summary>
        public int ExportPercentage;

        /// <summary>Currently exported MW (calculated each update).</summary>
        public int ExportedMW;

        /// <summary>Daily income from exports.</summary>
        public int ExportDailyIncome;

        /// <summary>Last accumulation time in game seconds.</summary>
        public double ExportLastAccumulationTime;

        /// <summary>Fractional income remainder carried between frames (fixes rounding loss).</summary>
#pragma warning disable CIVIC167 // Accumulator remainder, not monetary amount; ECS IComponentData
        public double ExportIncomeRemainder;
#pragma warning restore CIVIC167

        /// <summary>Suspicion cooldown in game days.</summary>
        public int SuspicionCooldown;

        /// <summary>Random state for deterministic suspicion rolls.</summary>
        public uint RngState;

        public static ShadowExportState CreateDefault()
        {
            return new ShadowExportState
            {
                ExportPercentage = 0,
                ExportedMW = 0,
                ExportDailyIncome = 0,
                ExportLastAccumulationTime = 0.0,
                ExportIncomeRemainder = 0.0,
                SuspicionCooldown = 0,
#pragma warning disable CIVIC156 // Deterministic ECS seed: re-seeded from save on deserialize
                RngState = new Random(0xE507u).state // EX = Export seed
#pragma warning restore CIVIC156
            };
        }

        /// <summary>Feature-aware fallback for cross-domain readers — zero export.</summary>
        public static ShadowExportState Default => CreateDefault();
    }
}
