using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using Unity.Mathematics;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Adapter for vanilla Game.Simulation.ClimateSystem.
    /// Owns the live climate snapshot and updates it throttled (1/sec).
    ///
    /// PATTERN: Humble Object
    /// - ServiceRegistry owns process-lifetime ClimateState facade
    /// - System owns the per-World snapshot and rebinds the facade on hot-reload
    /// - Consumers get thread-safe immutable snapshots
    ///
    /// USAGE:
    ///   IVanillaClimateAdapter climate = ServiceRegistry.Instance.Get&lt;IVanillaClimateAdapter&gt;();
    ///   var snapshot = climate.Current;
    ///   float temp = snapshot.Temperature;
    ///
    /// LIFECYCLE:
    /// - Created by ECS
    /// - Binds ClimateState facade to this world host
    /// - Updates snapshot throttled (climate changes slowly)
    /// - Destroyed on game exit
    /// </summary>
    [ActIndependent]
    public partial class VanillaClimateAdapter : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("VanillaClimateAdapter");

        // Climate changes slowly - 1Hz is plenty
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private ClimateSystem m_ClimateSystem = null!;
        [System.NonSerialized] private ClimateState? m_State;
        [System.NonSerialized] private readonly VersionedView<ClimateSnapshot> m_SnapshotView = new(ClimateSnapshot.Default);

        // PERF: cache season-name → int mapping. ToUpperInvariant() allocates ~30B every tick
        // if computed unconditionally. Vanilla currentSeasonName returns the same string instance
        // across ticks within one season; cache via ReferenceEquals (fast) → Ordinal Equals
        // (handles ref change without value change, e.g. after settings rebuild / hot-reload) →
        // re-resolve (real transition). Guarantees no missed transition: any value change goes
        // through ResolveSeason → GetSeason path.
        [System.NonSerialized] private string? m_CachedSeasonName;
        [System.NonSerialized] private int m_CachedSeasonInt;
        [System.NonSerialized] private bool m_WarnedInvalidTemperature;
        [System.NonSerialized] private bool m_WarnedUnknownSeason;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();

            // Register in SingletonRegistry for lifecycle tracking
            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Register<VanillaClimateAdapter>(this, "VanillaClimateAdapter.OnCreate");
            }

            Log.Info("Created (Humble Object pattern)");
        }

        protected override void OnDestroy()
        {
            // Race-guard against out-of-order host teardown across worlds.
            if (m_State != null && ReferenceEquals(m_State.CurrentHost, this))
                m_State.CurrentHost = null;
            m_State = null;

            // Unregister from SingletonRegistry
            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Unregister<VanillaClimateAdapter>();
            }

            Log.Info("Destroyed");

            base.OnDestroy();
        }

        protected override void OnThrottledUpdate()
        {
            if (!TryBuildSnapshot(out var snapshot))
                return;

            PublishSnapshot(snapshot);
        }

        internal ClimateSnapshot GetCurrentSnapshot()
        {
            if (m_SnapshotView.Version == 0)
                return ClimateSnapshot.Default;

            int observerVersion = -1;
            return m_SnapshotView.Observe(ref observerVersion).Value;
        }

        internal void BindFacade(ClimateState facade)
        {
            m_State = facade;
            facade.CurrentHost = this;
        }

        private void PublishSnapshot(ClimateSnapshot snapshot)
        {
            m_SnapshotView.Publish(snapshot);
        }

        private bool TryBuildSnapshot(out ClimateSnapshot snapshot)
        {
            snapshot = default;

            if (m_ClimateSystem.currentClimate == Entity.Null)
                return false;

            string? seasonName = m_ClimateSystem.currentSeasonName;
            if (seasonName == null)
                return false;

            snapshot = new ClimateSnapshot(
                GetTemperature(),
                ResolveSeason(seasonName),
                seasonName);
            return true;
        }

        private int ResolveSeason(string seasonName)
        {
            // Fast path: same vanilla string instance — typical 99% case (1Hz throttle, ~3600
            // calls/hour, ~4 real transitions/year).
            if (ReferenceEquals(seasonName, m_CachedSeasonName))
                return m_CachedSeasonInt;

            // Slow path 1: ref changed but value identical (vanilla rebuilt string after
            // save/load / settings change). No allocation needed — refresh cached ref, return
            // cached int.
            if (m_CachedSeasonName != null
                && string.Equals(seasonName, m_CachedSeasonName, System.StringComparison.Ordinal))
            {
                m_CachedSeasonName = seasonName;
                return m_CachedSeasonInt;
            }

            // Slow path 2: real season transition. ToUpperInvariant() + switch fires here
            // (~once per season change, not per tick).
            int seasonInt = GetSeason(seasonName);
            m_CachedSeasonName = seasonName;
            m_CachedSeasonInt = seasonInt;
            return seasonInt;
        }

        private float GetTemperature()
        {
            float temperature = m_ClimateSystem.temperature.value;
            if (!math.isfinite(temperature))
            {
                if (!m_WarnedInvalidTemperature)
                {
                    m_WarnedInvalidTemperature = true;
                    Log.Warn("ClimateSystem.temperature is not finite; using 15C fallback");
                }

                return ClimateSnapshot.Default.Temperature;
            }

            return temperature;
        }

        private int GetSeason(string seasonName)
        {
            // Vanilla SeasonInfo.name returns either the BaseSeason.name ("Spring") for variant
            // prefabs, or the prefab's own name ("SeasonSpring") for base prefabs without a
            // variant pack. Both forms are valid and depend on the map's climate setup, so the
            // switch lists both shapes for each season.
#pragma warning disable CIVIC135 // Vanilla API returns string season name — no enum available
            return seasonName.ToUpperInvariant() switch
            {
                "SPRING" or "SEASONSPRING" => 0,
                "SUMMER" or "SEASONSUMMER" => 1,
                "FALL" or "AUTUMN" or "SEASONFALL" or "SEASONAUTUMN" => 2,
                "WINTER" or "SEASONWINTER" => 3,
                _ => GetFallbackSeason(seasonName)
            };
#pragma warning restore CIVIC135
        }

        private int GetFallbackSeason(string seasonName)
        {
            if (!m_WarnedUnknownSeason)
            {
                m_WarnedUnknownSeason = true;
                Log.Warn($"Unknown ClimateSystem season '{seasonName}'; keeping last valid season");
            }

            return m_CachedSeasonName == null ? 0 : m_CachedSeasonInt;
        }
    }
}
