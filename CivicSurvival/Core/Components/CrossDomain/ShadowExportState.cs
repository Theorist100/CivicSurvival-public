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

        /// <summary>
        /// Share of the city's OWN dispatchable capacity actually being sold covertly, 0-100
        /// (calculated each update alongside <see cref="ExportedMW"/>, never persisted).
        ///
        /// Distinct from <see cref="ExportPercentage"/>, which is only where the player put the
        /// slider: the slider says how much of the export HEADROOM to sell, so a city with no
        /// spare capacity reads 100% while selling nothing. Consumers that punish the city for
        /// power leaving it (mobilization morale) must read this, not the slider — otherwise they
        /// charge full price for a sale that never happened. Exposure-style consumers, which key
        /// off the scheme existing at all, keep reading the slider.
        /// </summary>
        public int ExportLoadPercent;

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
                ExportLoadPercent = 0,
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
