using Game;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Grants Heritage AA credits at game start.
    ///
    /// Heritage AA ("старі зенітки зі складів"):
    /// - Credits for FREE AA placements from city reserves
    /// - Scales with city size (productionMW)
    /// - When player places AA, credits are consumed first (free)
    /// - After credits exhausted, player pays full price
    ///
    /// Does NOT create entities - just sets HeritageCredits in singleton.
    /// AAInstallationDetectorSystem handles consuming credits when placing AA.
    ///
    /// Credits granted immediately on first update (not tied to scenario phases).
    /// </summary>
    [ActIndependent]
#pragma warning disable CIVIC070 // PowerGridSingleton: one-shot with 300-frame zero-production fallback — stale first read is harmless
#pragma warning disable CIVIC249 // One-shot re-grant guarded by HeritageCreditsMax persistence + event latch
    public partial class HeritageGrantSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("HeritageGrantSystem");

        private const int ZERO_PRODUCTION_FALLBACK_FRAMES = 300; // ~5s at 60fps

        private int m_ZeroProductionFrames;
        // Terminal latch: re-populates from canonical state on first tick post-load. Without it
        // the TryGetSingleton-based gate fires on every sim tick and shows up as 100% sync flags
        // per VanillaProfiler — see HERITAGE_GRANT_REGRESSION.md.
        [System.NonSerialized]
        private bool m_GrantResolved;
        [System.NonSerialized]
        private bool m_EventBusUnavailableLogged;
        private EntityQuery m_PowerGridQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());

            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            // Terminal latch: once resolved, subsequent ticks short-circuit at 1 ns. Latch is
            // [NonSerialized] and re-derives from canonical state (PendingGrantQuery / singleton)
            // on the first tick after load — no parallel persisted flag.
            if (m_GrantResolved)
                return;

            // Check if credits already set (handles game load).
            // NOTE(M9): HeritageCreditsMax is the re-grant guard — persists even after credits depleted.
            if (SystemAPI.TryGetSingleton<AirDefenseCreditsSingleton>(out var state) &&
                (state.HeritageCredits > 0 || state.HeritageCreditsMax > 0))
            {
                m_GrantResolved = true;
                Log.Debug("Heritage credits already exist — latched resolved");
                return;
            }

            // FIX A6: Defer until power data is populated (one-shot reads stale default otherwise)
            if (!m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
                return;

            if (grid.Production <= 0)
            {
                // City exists but has zero production (no power plants yet).
                // Wait up to ~5s, then grant with the minimum city size to avoid permanent deadlock.
                m_ZeroProductionFrames++;
                if (m_ZeroProductionFrames < ZERO_PRODUCTION_FALLBACK_FRAMES)
                    return;
                Log.Warn($"Heritage grant: city has zero production after {ZERO_PRODUCTION_FALLBACK_FRAMES} frames, granting minimum");
            }
            else
            {
                m_ZeroProductionFrames = 0;
            }

            // City SIZE in MW from built nameplate (snapshot), NOT live production — the free AA
            // grant scales with how big the city IS, the same "city size" the waves use, so a struck
            // city does not get a smaller grant. Falls back to live production until the snapshot is
            // ready (boot); the zero-production deferral above still gates the one-shot timing.
            int citySizeMW = WaveContextGatherer.ResolveCitySizeMW(grid.Production);
            GrantHeritageCredits(citySizeMW);
        }

        /// <summary>
        /// Calculate heritage credits based on city production.
        /// Formula: BaseCount + (ProductionMW / MwPerAA), clamped to [Min, Max]
        /// </summary>
        private int CalculateHeritageCount(int productionMW)
        {
            int count = HeritageGrantLogic.CalculateHeritageCount(productionMW);
            var cfg = BalanceConfig.Current.AAUnits;   // kept ONLY for the log line, not dup arithmetic
            Log.Info($"Calculated heritage credits: {count} (base={cfg.HeritageBaseCount}, MW={productionMW}, MwPerAA={cfg.MwPerAA})");
            return count;
        }

        /// <summary>
        /// Grant heritage credits to player through same-tick event delivery.
        /// Single writer pattern: AirDefenseStateSystem applies the event to the
        /// credits singleton when it runs later in the same GameSimulation chain.
        /// </summary>
        private void GrantHeritageCredits(int productionMW)
        {
            int credits = CalculateHeritageCount(productionMW);

            if (EventBus == null)
            {
                if (!m_EventBusUnavailableLogged)
                {
                    Log.Warn("Heritage grant event bus unavailable; retrying next tick");
                    m_EventBusUnavailableLogged = true;
                }
                m_ZeroProductionFrames = math.min(m_ZeroProductionFrames, ZERO_PRODUCTION_FALLBACK_FRAMES - 1);
                return;
            }

            if (!EventBus.SafePublish(new HeritageGrantedEvent(credits, productionMW), "HeritageGrantSystem"))
            {
                if (!m_EventBusUnavailableLogged)
                {
                    Log.Warn("Heritage grant event had no delivery target; retrying next tick");
                    m_EventBusUnavailableLogged = true;
                }
                m_ZeroProductionFrames = math.min(m_ZeroProductionFrames, ZERO_PRODUCTION_FALLBACK_FRAMES - 1);
                return;
            }

            m_GrantResolved = true;
            m_EventBusUnavailableLogged = false;

            Log.Info($"Granted {credits} Heritage credits via event ({productionMW} MW city)");
        }
    }
}
