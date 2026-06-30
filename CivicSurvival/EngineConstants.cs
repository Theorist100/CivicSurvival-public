namespace CivicSurvival
{
    /// <summary>
    /// Compile-time constants for ENGINE systems and Burst-compiled code.
    ///
    /// Architecture:
    /// - BalanceConfig (JSON/Server) = Game Design (prices, chances, damage)
    /// - Engine (C# const) = Engineering (buffer sizes, Burst, memory limits)
    ///
    /// This file contains ONLY:
    /// - ENGINE constants (Timing, Audio, Toast, DataStructures, Notifications, PowerGrid)
    /// - Burst-compiled code requirements (Schedule cycle params, NeighborEnvy)
    /// - Default parameter values (Narrative.BATCH_WINDOW_SECONDS)
    ///
    /// DO NOT add gameplay constants here - add them to RemoteBalanceConfig.cs instead.
    /// </summary>
    public static class Engine
    {
        // ============================================================================
        // TIMING - Frame & Game Time (ENGINE)
        // ============================================================================
        public static class Timing
        {
            public const float HOURS_PER_DAY = 24f;             // Game hours
            public const float DEFAULT_GAME_HOUR = 12f;         // Noon as default start

            public const float SIMULATION_FPS = 60f;

            // System update intervals (frames at 60fps)
            public const int UPDATE_INTERVAL_500_MS = 30;      // ~0.5 seconds
            public const int UPDATE_INTERVAL_1_SECOND = 60;
            public const int UPDATE_INTERVAL_2_SECONDS = 120;
            public const int UPDATE_INTERVAL_2500_MS = 150;    // ~2.5 seconds (GC optimized)
            public const int UPDATE_INTERVAL_5_SECONDS = 300;
            public const int UPDATE_INTERVAL_1_MINUTE = 3600;

            // Chirper/notification intervals
            public const int CHIRPER_POST_INTERVAL_HOURS = 6;
        }

        // ============================================================================
        // AUDIO - Threat sounds (ENGINE)
        // ============================================================================
        public static class Audio
        {
            // Volume calculation
            public const float VOLUME_PER_THREAT = 0.2f;
            public const float VOLUME_FULL_DISTANCE = 500f;
            public const float VOLUME_FADE_DISTANCE = 4500f;
            public const float VOLUME_MIN = 0f;

            // Pitch calculation (Doppler-like effect)
            public const float PITCH_FAR_DISTANCE = 2000f;
            public const float PITCH_CLOSE_DISTANCE = 200f;
            public const float PITCH_BASE = 1.0f;
            public const float PITCH_MAX = 1.5f;

            // Sound effect volumes
            public const float AA_FIRE_VOLUME = 0.8f;

            // AudioManager spatial audio settings
            public const float SPATIAL_DOPPLER_LEVEL = 0.5f;
            public const float DEFAULT_MIN_DISTANCE = 100f;
            public const float DEFAULT_MAX_DISTANCE = 2000f;
        }

        // ============================================================================
        // THREATS - Spawn & Physics (ENGINE)
        // ============================================================================
        public static class Threats
        {
            // Shahed: fly altitude above terrain, spawned at the map edge.
            public const float SHAHED_SPAWN_ALTITUDE = 150f;
            // Ballistic: launched from the map edge (like drones), flies a lofted arc high
            // across the map, then dives near-vertically onto the target. Spawn height here;
            // the arc shape (cruise height, climb/dive fractions) lives in BallisticMovementJobEntity.
            public const float BALLISTIC_LAUNCH_ALTITUDE = 100f;   // spawn height above terrain at the edge

            // Audio
            public const float SHAHED_AUDIO_RADIUS = 500f;
            public const float SHAHED_AUDIO_PITCH = 0.8f;

            // Data structure capacity
            public const int TARGET_MAP_CAPACITY = 64;

            // Significance thresholds
            public const int MIN_SIGNIFICANT_LOSS_MW = 10;
        }

        // ============================================================================
        // POWER GRID - Unit Conversions & Thresholds (ENGINE)
        // ============================================================================
        public static class PowerGrid
        {
            public const float GRID_POWER_THRESHOLD = 0.9f;
            /// <summary>
            /// Reduced threshold during active load shedding (S13a-7 fix).
            /// Partial power is better than zero during crisis.
            /// </summary>
            public const float GRID_POWER_THRESHOLD_CRISIS = 0.5f;
            public const int CATEGORY_MULTIPLIER = 10;

            // Status thresholds (kW). The Warning-side deficit boundary is NOT a constant:
            // it is the GridStress dead zone (BalanceConfig GridStress.DeficitDeadZone*),
            // shared with GridStressSystem so the UI status and the stress integrator
            // agree on what counts as a deficit.
            public const int CRITICAL_DEFICIT_THRESHOLD = -100_000;
            public const int SURPLUS_THRESHOLD = 50_000;

            // UI encoding
            public const int UI_DISTRICT_ENCODING_MULTIPLIER = 100;

            // Unit conversions
            public const int DEFAULT_LEGAL_IMPORT_MW = 100;
            public const int DEFAULT_LEGAL_EXPORT_MW = 0;
            public const int KW_PER_MW = 1000;
        }

        // ============================================================================
        // SCHEDULE CYCLE - Schedule params for Burst-compiled ScheduleHelper
        // NOTE: Class kept as Engine.LoadShedding for backwards compat with ScheduleHelper references
        // ============================================================================
        public static class LoadShedding
        {
            // Schedule cycle parameters (used by Burst-compiled ScheduleHelper)
            public const int MILD_ON_HOURS = 4;
            public const int MILD_OFF_HOURS = 2;
            public const int BALANCED_ON_HOURS = 4;
            public const int BALANCED_OFF_HOURS = 4;
            public const int SEVERE_ON_HOURS = 2;
            public const int SEVERE_OFF_HOURS = 4;

            // DayShift schedule
            public const int DAYSHIFT_START_HOUR = 8;
            public const int DAYSHIFT_END_HOUR = 20;
            public const int DAYSHIFT_PHASE_SPREAD = 12;

            // District phase distribution
            public const int MAX_PHASE_OFFSETS = 24;
        }

        // ============================================================================
        // NEIGHBOR ENVY - Burst job constants
        // ============================================================================
        public static class NeighborEnvy
        {
            public const float ENVY_RADIUS = 100f;
            public const float CELL_SIZE = 100f;
            // Spatial hash primes (for Burst jobs)
            public const int SPATIAL_HASH_PRIME_X = 73856093;
            public const int SPATIAL_HASH_PRIME_Z = 19349663;

            // Capacity constants
            public const int INITIAL_DISTRICT_SET_CAPACITY = 64;
            public const int ENTITY_BUFFER_HEADROOM = 500;
            public const int VIP_DISTRICT_SET_CAPACITY = 64;
            public const int ADJACENT_CELLS_COUNT = 9;
            public const int POWER_JOB_BATCH_SIZE = 64;
            public const int SEARCH_JOB_BATCH_SIZE = 32;

            /// <summary>
            /// Single source of the spatial-grid hash formula: maps an integer cell (cx, cz)
            /// to its grid key. All grid build/lookup sites must route through this so the
            /// build side and the lookup side can never drift apart. Pure int arithmetic,
            /// Burst-safe, no Unity.Mathematics dependency.
            /// </summary>
            public static int GridKeyFromCell(int cx, int cz)
                => cx * SPATIAL_HASH_PRIME_X ^ cz * SPATIAL_HASH_PRIME_Z;
        }

        // ============================================================================
        // DATA STRUCTURES - Collection Capacities (ENGINE)
        // ============================================================================
        public static class DataStructures
        {
            public const int MEDIUM_CAPACITY = 256;
            public const int LARGE_CAPACITY = 2000;

            // StringBuilder
            public const int STRING_BUILDER_SIZE = 256;
            public const int STRING_BUILDER_POOL_SIZE = 16;
            public const int STRING_BUILDER_MAX_CAPACITY = 4096;
        }

        // ============================================================================
        // NARRATIVE - Default parameter (BATCH_WINDOW_SECONDS used in BatchAggregator)
        // ============================================================================
        public static class Narrative
        {
            /// <summary>
            /// Time window for batching events (real seconds).
            /// Used as default parameter in BatchAggregator constructor.
            /// </summary>
            public const float BATCH_WINDOW_SECONDS = 3f;

            /// <summary>
            /// Game-time bucket width (seconds) for content-stable news ids
            /// (<see cref="Core.Utils.NotificationIdHelper.ContentId"/>). Two identical
            /// news posts from the same source inside this window collapse to one Herald
            /// entry via NewsFeedService.m_SeenIds; a later bucket lets a legitimately
            /// re-issued same-type news through. One game hour (GameRate.SECONDS_PER_HOUR)
            /// — narrative news of the same type firing within an in-game hour is the
            /// duplicate case we are collapsing.
            /// </summary>
            public const int NEWS_CONTENT_BUCKET_SECONDS = 3600;
        }

        // ============================================================================
        // TOAST - UX Timing (ENGINE - real seconds)
        // ============================================================================
        public static class Toast
        {
            public const float DISPLAY_SECONDS = 45f;
            public const int QUEUE_MAX = 3;
        }

        // ============================================================================
        // DISTRICTS - Category counts (only TOTAL_BUILDING_CATEGORIES needed)
        // ============================================================================
        public static class Districts
        {
            public const int TOTAL_BUILDING_CATEGORIES = 5;

            /// <summary>
            /// Logical district id for the Unzoned / No-District bucket. Buildings whose
            /// CurrentDistrict.m_District == Entity.Null map to this id in the blackout /
            /// snapshot / UI state layer (the player toggles Unzoned via DTO entityIndex 0).
            /// The power-aggregation buffer uses a separate sentinel of -1 in its DistrictRef
            /// key; the mapping point between the two is the single place that translates
            /// the buffer sentinel into this logical id. Unzoned is a first-class
            /// controllable district — this constant is the single source of truth for it.
            /// </summary>
            public const int NO_DISTRICT_INDEX = 0;
        }

        // ============================================================================
        // ERROR REPORTING - Diagnostics (ENGINE)
        // ============================================================================
        public static class ErrorReporting
        {
            public const int MAX_LOG_LINES = 100;
            public const int MAX_RECENT_ERRORS = 50;
            public const int MAX_STACK_TRACE_LINES = 5;

            /// <summary>Minimum seconds between identical error reports (dedupe window)</summary>
            public const float DEDUPE_WINDOW_SECONDS = 10f;

            /// <summary>Max unique error keys tracked for deduplication</summary>
            public const int MAX_DEDUPE_KEYS = 64;

            /// <summary>Sentinel file written on clean shutdown, absence = crash</summary>
            public const string CLEAN_SHUTDOWN_FILE = "session_clean.flag";
        }

        // ============================================================================
        // REFLECTION - Private field names accessed via AccessTools (ENGINE)
        // ============================================================================
        public static class Reflection
        {
            public const string PREFAB_SYSTEM_PREFABS_FIELD = "m_Prefabs";
        }

        // ============================================================================
        // UTILITIES
        // ============================================================================
        public static class Util
        {
            /// <summary>Snap value to nearest preset in array.</summary>
            /// <exception cref="System.ArgumentException">Thrown when presets is null or empty.</exception>
            public static int SnapToPreset(int value, int[]? presets)
            {
                if (presets == null || presets.Length == 0)
                    throw new System.ArgumentException("presets cannot be null or empty", nameof(presets));

                int closest = presets[0];
                int minDiff = int.MaxValue;

                foreach (int preset in presets)
                {
                    int diff = value > preset ? value - preset : preset - value;
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closest = preset;
                    }
                }

                return closest;
            }
        }
    }
}
