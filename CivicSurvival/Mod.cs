using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Infrastructure.Audio;
using CivicSurvival.Localization;
using CivicSurvival.Patches;
using CivicSurvival.Core.Services;
using CivicSurvival.Services.Bootstrap;
using CivicSurvival.Services.UI;
using CivicSurvival.Services.Arena;
using CivicSurvival.Services.DevTools;
using CivicSurvival.Services.DistrictState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Services.RemoteConfig;
using CivicSurvival.Core.Config;
using CivicSurvival.Domains.Notifications.Services;

// Domains
using CivicSurvival.Domains.PowerGrid;
using CivicSurvival.Domains.PowerBackup;
using CivicSurvival.Domains.Blackout;
using CivicSurvival.Domains.Engineering;
using CivicSurvival.Domains.ShadowEconomy;
using CivicSurvival.Domains.Economics;
using CivicSurvival.Domains.Finance;
using CivicSurvival.Domains.Corruption;
using CivicSurvival.Domains.Countermeasures;
using CivicSurvival.Domains.NeighborEnvy;
using CivicSurvival.Domains.Diplomacy;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Domains.Scenario;
using CivicSurvival.Domains.Tutorial;
using CivicSurvival.Domains.Attention;
using CivicSurvival.Domains.ThreatFlight;
using CivicSurvival.Domains.ThreatDamage;
using CivicSurvival.Domains.Waves;
using CivicSurvival.Domains.ThreatUI;
using CivicSurvival.Domains.AirDefense;
using CivicSurvival.Domains.Intel;
using CivicSurvival.Domains.Spotters;
using CivicSurvival.Domains.Cognitive;
using CivicSurvival.Domains.Notifications;
using CivicSurvival.Domains.Narrative;
using CivicSurvival.Domains.Refugees;
using CivicSurvival.Domains.GridWarfare;
using CivicSurvival.Domains.Network;
using CivicSurvival.Domains.Mobilization;

namespace CivicSurvival
{
    /// <summary>
    /// Civic Survival - Infrastructure Survival Mod
    /// One mod, two experiences: neutral EN / realistic UA
    ///
    /// "Systems critical. Initiating blackout protocol."
    /// </summary>
#pragma warning disable CA1716 // Reserved keyword - standard CS2 modding convention
    public class Mod : IMod
#pragma warning restore CA1716
    {
        public const string MOD_NAME = "CivicSurvival";
        public const string HARMONY_ID = "com.civicsurvival.mod";

        /// <summary>
        /// Set to true during OnDispose. ThreadPool callbacks should check this
        /// before accessing mod resources to avoid orphaned work after unload.
        /// </summary>
#pragma warning disable CA2211 // Volatile field for ThreadPool cancellation — property would add indirection
        public static volatile bool IsUnloading;
#pragma warning restore CA2211

        private static void SetUnloading(bool value) => IsUnloading = value;

        /// <summary>
        /// Save format version. Increment when save structure changes in breaking way.
        /// Per-system versions handle field additions, this tracks global compatibility.
        /// History: v1 = initial release
        /// </summary>
        public const byte SAVE_FORMAT_VERSION = 1;

        // Rotate the previous session's mod log to CivicSurvival-prev.log BEFORE the GetLogger below
        // makes Colossal.Logging truncate it. Declared (and thus run) ahead of Log so a post-restart
        // bug report can still attach the crashing session's log — otherwise the report ships the fresh
        // (empty) session and the cause is gone. Uses Application.persistentDataPath directly because
        // ModPaths.Initialize() has not run yet at static-init time.
#pragma warning disable S1144, CA1823 // Intentional: the field exists only to run RotatePreviousLog as a side-effect BEFORE the Log initializer below truncates the file; it is never read.
        private static readonly bool s_PrevLogRotated = RotatePreviousLog();
#pragma warning restore S1144, CA1823

        public static readonly ILog Log = LogManager
            .GetLogger(MOD_NAME)
            .SetShowsErrorsInUI(false)
            // The game persists a per-logger "logStackTrace" flag in FallbackSettings.coc and
            // reloads it over our code at logger creation. When true it dumps a full managed
            // call-stack onto EVERY line (any level), unconditionally — that is the real source
            // of the per-line stack spam, not showsStackTraceAboveLevels. Force it off here so
            // the reloaded value can't reinstate it; the game re-persists this false on exit.
            .SetLogStackTrace(false)
            // Keep scoped stacks for Error+ where they're useful.
            .SetShowsStackTraceAboveLevels(Level.Error);

        // Single source of truth — derived from the built assembly so these can never drift from what
        // actually shipped. csproj <Version> drives VERSION; the SDK appends the git commit to
        // AssemblyInformationalVersion ("0.1.0+<sha>") so BUILD_COMMIT auto-tracks source; BUILD_DATE
        // is the built DLL's timestamp. Declared after Log so ResolveBuildDate can log on failure.
        private static readonly string s_InformationalVersion = ResolveInformationalVersion();
        public static readonly string VERSION = ParseVersion(s_InformationalVersion);
        public static readonly string BUILD_COMMIT = ParseBuildCommit(s_InformationalVersion);
        public static readonly string BUILD_DATE = ResolveBuildDate();
#if CIVIC_DIAG
        // Non-null only on a private test build (-p:CivicTestModVersion=X.Y.Z). Surfaces the test-listing
        // version in the runtime log so it is clear which test build is running; compiled out of prod.
        public static readonly string? TEST_MOD_VERSION = ResolveTestModVersion();
#endif

        static Mod()
        {
            // Info for shipping/perf. PerformanceProfiler.Measure and Debug-level hot-path
            // logging are gated on Level.Debug (see OnLoad, isLevelEnabled(Level.Debug)) and
            // tank FPS when enabled. Flip to Level.Debug only for diagnostic profiling.
            Log.SetEffectiveness(Level.Info);
        }

        // Copy last session's CivicSurvival.log to CivicSurvival-prev.log before the logger truncates
        // it. Best-effort: never block mod load on log rotation, and avoid ModPaths (not yet
        // initialized at static-init). Mirrors Unity's Player.log -> Player-prev.log.
        private static bool RotatePreviousLog()
        {
            try
            {
                string logs = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Logs");
                string current = System.IO.Path.Combine(logs, Core.Config.ModPaths.ModLogFile);
                // No File.Exists pre-check (TOCTOU): File.Copy throws if the source is absent and the
                // catch below swallows it — first run has no prior log, which is fine.
                System.IO.File.Copy(current, System.IO.Path.Combine(logs, Core.Config.ModPaths.PrevModLogFile), overwrite: true);
            }
#pragma warning disable CIVIC052 // Best-effort log rotation at static-init; a failure must not block mod load.
            catch { /* swallow — diagnostic convenience, not load-critical */ }
#pragma warning restore CIVIC052
            return true;
        }

        private static string ResolveInformationalVersion()
        {
            var asm = typeof(Mod).Assembly;
            var attr = (System.Reflection.AssemblyInformationalVersionAttribute?)System.Attribute.GetCustomAttribute(
                asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            string? info = attr?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
                return info!;
            return asm.GetName().Version?.ToString() ?? "0.0.0";
        }

        private static string ParseVersion(string informational)
        {
            // InformationalVersion is "<semver>+<git sha>"; the version is everything before '+'.
            return informational.Split('+')[0];
        }

        private static string ParseBuildCommit(string informational)
        {
            string[] parts = informational.Split('+');
            if (parts.Length < 2 || parts[1].Length == 0)
                return "unknown";
            string commit = parts[1];
            return commit.Length >= 8 ? commit.Substring(0, 8) : commit;
        }

        private static string ResolveBuildDate()
        {
            // Baked at compile time via the csproj AssemblyMetadata "BuildDate" item. CS2 mods load
            // from a byte array (Assembly.Location is empty), so a file-timestamp fallback can't work.
            var attrs = System.Attribute.GetCustomAttributes(typeof(Mod).Assembly, typeof(System.Reflection.AssemblyMetadataAttribute));
            foreach (var attr in attrs)
            {
                if (attr is System.Reflection.AssemblyMetadataAttribute meta
                    && meta.Key == "BuildDate"
                    && !string.IsNullOrEmpty(meta.Value))
                    return meta.Value;
            }
            return "unknown";
        }

#if CIVIC_DIAG
        private static string? ResolveTestModVersion()
        {
            // Baked only when -p:CivicTestModVersion=X.Y.Z is passed (private test listing builds).
            var attrs = System.Attribute.GetCustomAttributes(typeof(Mod).Assembly, typeof(System.Reflection.AssemblyMetadataAttribute));
            foreach (var attr in attrs)
            {
                if (attr is System.Reflection.AssemblyMetadataAttribute meta
                    && meta.Key == "CivicTestModVersion"
                    && !string.IsNullOrEmpty(meta.Value))
                    return meta.Value;
            }
            return null;
        }
#endif

        /// <summary>
        /// Directory of the loaded mod assembly, resolved from the executable asset
        /// the game registered for this <see cref="IMod"/>. Works for both the local
        /// dev deploy (Mods/CivicSurvival) and Paradox Mods subscribers (subscription
        /// cache), where the icon/audio assets live next to the DLL.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// The executable asset can't be resolved. At OnLoad the game has already
        /// loaded this assembly to call us, so the asset must exist — a failure means
        /// a broken install with no valid path to the icons/audio that live next to
        /// the DLL, which is fatal.
        /// </exception>
        private string ResolveModInstallDirectory()
        {
            if (GameManager.instance?.modManager != null
                && GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)
                && asset != null
                && !string.IsNullOrEmpty(asset.path))
            {
                string? directory = System.IO.Path.GetDirectoryName(asset.path);
                if (!string.IsNullOrEmpty(directory))
                    return directory;
            }

            throw new System.InvalidOperationException(
                "Could not resolve the mod executable asset; no valid install directory for icons/audio.");
        }

        private Harmony? _harmony;
        // world-lifetime: composition-root holds the world ref captured at OnLoad
        // purely to route Mod.OnDispose Cleanup() to per-World handlers for an
        // explicit restore in the hot-reload scenario (mod unload without prior
        // exit-to-menu). Cleared in OnDispose before ServiceRegistry teardown.
        private Unity.Entities.World? _world;
        private RemoteConfigService? m_RemoteConfig;
        private ThreadSafeDistrictState? m_DistrictState;

        /// <summary>
        /// Called when the mod is loaded.
        /// </summary>
        public void OnLoad(UpdateSystem updateSystem)
        {
            BacktraceMarkers.Phase("Mod.OnLoad/start");

            // Initialize centralized paths (TIER 0: must be first). BurstLogBootstrap below and
            // all later path access read ModPaths, so this must precede them — otherwise
            // ModPaths.get_State() throws and OnLoad aborts before localization loads.
            //
            // Resolve the mod install directory from the loaded executable asset rather
            // than assuming gameDataRoot/Mods/CivicSurvival. That hardcoded layout exists
            // only when the mod is deployed locally to the Mods folder (dev); Paradox Mods
            // subscribers load from the subscription cache, where the icons/audio actually
            // live. asset.path is the loaded DLL in both cases, so deriving the directory
            // from it is the only path that works for shipped subscribers (icons via
            // coui://ui-mods/cs-icons, audio). An unresolved asset is fatal — see ResolveModInstallDirectory.
            string modInstallDirectory = ResolveModInstallDirectory();
            ModPaths.Initialize(UnityEngine.Application.persistentDataPath, modInstallDirectory);

            // Burst-capable diagnostic logger (separate BurstDiag.log). Initialized early so any
            // Collections API incompatibility surfaces here as a TypeLoad, not deep in a job.
            Core.Diagnostics.BurstLogBootstrap.Initialize();

            Log.Info("╔════════════════════════════════════════╗");
            Log.Info("║     CIVIC SURVIVAL                     ║");
            Log.Info("║     Infrastructure Survival Mod        ║");
            Log.Info("╚════════════════════════════════════════╝");
            Log.Info($"Build {VERSION} ({BUILD_DATE}, commit {BUILD_COMMIT})");
#if CIVIC_DIAG
            if (TEST_MOD_VERSION != null)
                Log.Info($"[TEST BUILD] private test-listing version {TEST_MOD_VERSION} (DLL AssemblyVersion stays {VERSION})");
#endif

            // Reset unloading flag from previous session (H7: sticky true after OnDispose)
            SetUnloading(false);

            // Open a new VanillaReflectionRegistry generation. Increments the gen
            // counter, clears per-generation failure dedup; preserves cached FieldInfo
            // resolves (same AppDomain / same Game.dll across hot-reload boundary).
            Core.Infrastructure.VanillaReflectionRegistry.StartGeneration("Mod.OnLoad");

            // Clear static error log from previous game (BUG-SRV-003)
            ErrorReportService.ClearErrors();
            PatchStatusTracker.Clear();
            Core.Infrastructure.PressureRegistry.Reset();

            // Initialize FeatureRegistry (TIER 0: Bootstrap)
            // Must be BEFORE ServiceRegistry - domains may register services
            BacktraceMarkers.Phase("Mod.OnLoad/FeatureRegistry-init");
            FeatureRegistry.Initialize();
            RegisterFeatures();
            BacktraceMarkers.Phase("Mod.OnLoad/Features-registered");

            // Initialize SingletonRegistry (tracks all singleton instances)
            SingletonRegistry.Initialize();
            if (SingletonRegistry.IsInitialized)
                SingletonRegistry.Instance.Register<Mod>(this, "Mod.OnLoad");

            // Performance Profiler — lifecycle subscribers initialize/shutdown PERF
            // only while gameplay is ready, not during cold menu bootstrap.
            Core.Utils.PerformanceProfiler.SetDebugMode(Log.isLevelEnabled(Level.Debug));
            CivicGameLifecycle.RegisterDefaultSubscribers();

            // Initialize ServiceRegistry (Layer 2: Infrastructure)
            BacktraceMarkers.Phase("Mod.OnLoad/ServiceRegistry-init");
            ServiceRegistry.Initialize();
            var services = ServiceRegistry.Instance;

            // Register infrastructure services
            var modSettings = new ModSettings();
            // Telemetry opt-in is persisted globally (save-independent). Seed it here at
            // init so the Options toggle reflects real consent immediately — before any
            // save loads — and so TelemetryConfig.Load below sees the true state.
            modSettings.ApplyPatch(ModSettingsPatch.SetTelemetryEnabled(Core.Services.TelemetryOptInStore.Read()));
            services.Register(modSettings);

            // Remote Config (hot-update balance without patches)
            var telemetryConfig = TelemetryConfig.Load(modSettings);
            services.Register(new TelemetryIdentityService(telemetryConfig));
            var remoteConfig = new RemoteConfigService(telemetryConfig);
            m_RemoteConfig = remoteConfig;

            var eventBus = new EventBus();
            services.Register<IEventBus>(eventBus);
            var writeBarrier = new RenderWriteBarrier();
            services.Register<IRenderWriteBarrier>(writeBarrier);
            services.Register<IVanillaWriteBarrier>(writeBarrier);

            // Cross-system Destroy/Ignite same-frame dedup.
            // Single shared NativeParallelHashMap<int, FrameMutationKind> replacing
            // per-system m_IgniteQueuedThisFrame / m_DestroyQueuedThisFrame sets in
            // BackupPowerEffectsSystem / CounterfeitBatteryFireSystem /
            // PlantWearSimulation / ThreatDamageSystem / BuildingDamageHelper.
            // Frame-end clear runs in FrameMutationDedupClearSystem after
            // GameSimulationEndBarrier gameplay playback and before ModCleanupBarrier.
            services.Register<IFrameMutationDedup>(new FrameMutationDedupService());
            services.Register<IThreatLifecycleDedup>(new ThreatLifecycleDedupService());
            services.Register<IThreatTerminalizationSink>(new ThreatTerminalizationSink());

            // ActEpoch generation clock (C-5 root fix). Single managed source of the
            // narrative act-generation stamp.
            // Registered here — process-lifetime, before any system OnCreate — so the
            // init-order hazard is eliminated by construction.
            services.Register(new ActEpochClock());

            // Threat transient generation clock. This invalidates threat/arrival/impact
            // leftovers on load/reset boundaries without killing legitimate in-flight
            // threats across narrative act transitions.
            services.Register(new ThreatGenerationClock());

            // Ownership transfer pattern: assign null after transfer to signal analyzer (CA2000)
            var districtState = new ThreadSafeDistrictState();
            try
            {
                districtState.Initialize(eventBus);
                services.Register<IDistrictStateReader>(districtState);
                services.Register<IDistrictStateWriter>(districtState);
                services.Register<IAutoDispatchStateWriter>(districtState);
                services.Register<IDistrictStateSerialization>(districtState);
                m_DistrictState = districtState;
                districtState = null!; // Ownership transferred to ServiceRegistry
            }
            finally
            {
                districtState?.Dispose(); // Only disposes if registration failed
            }

            // SocialFeedService now registered by NotificationsDomain.RegisterContent()
            // (IContentFeatureModule, called before RegisterSystems).

            // Register generic audio infrastructure (Core layer)
            var audioManager = AudioManager.Create();
            audioManager.Initialize();
            services.Register(audioManager);
            if (SingletonRegistry.IsInitialized)
                SingletonRegistry.Instance.Register<AudioManager>(audioManager, "Mod.OnLoad");

            // Initialize localization
            LocalizationManager.Initialize();
            Log.Info($"Localization: {LocalizationManager.CurrentLocale}");

            // Satire providers are now registered via IContentFeatureModule.RegisterContent()
            // by FeatureRegistry.RegisterOpenFeatures, called from SystemRegistrar.RegisterAll
            // (later in OnLoad). Closed features no longer leak content into active UI.

            // Initialize Harmony patches
            BacktraceMarkers.Phase("Mod.OnLoad/Harmony-start");
            _harmony = HarmonyPatchBootstrapper.Apply(HARMONY_ID);
            BacktraceMarkers.Phase("Mod.OnLoad/Harmony-done");

            // Log Burst compilation status (diagnostic)
            BacktraceMarkers.Phase("Mod.OnLoad/BurstStatus-start");
            BurstDiagnosticsLogger.LogStatus();
            BacktraceMarkers.Phase("Mod.OnLoad/BurstStatus-done");

            // Capture world for Mod.OnDispose hot-reload safety nets (see _world docstring).
            _world = updateSystem.World;

            // Façades live process-lifetime in ServiceRegistry; matching host systems
            // (registered via SystemRegistrar) own the per-World vanilla refs and
            // write themselves into the façade on OnCreate.
            ProcessLifetimeFacadeBootstrapper.Register(services);

            // Build feature manifest from currently-loaded balance config (bootstrap snapshot).
            // Background remote refresh updates the cache for next launch — does NOT mutate
            // active gates (closed-feature semantics, §2.3 of FEATURE_MODULE_ARCHITECTURE_PLAN).
            var manifest = FeatureManifest.FromBalance(remoteConfig.GetConfig());

            // Register ALL ECS systems (single point of registration)
            BacktraceMarkers.Phase("Mod.OnLoad/SystemRegistrar-start");
            SystemRegistrar.RegisterAll(updateSystem, manifest);
            BacktraceMarkers.Phase("Mod.OnLoad/SystemRegistrar-done");
            SatireRegistry.ValidateStartupContent(manifest);

            // Hot-reload safety net: if the world survived the previous Mod.OnDispose
            // (mod hot-reload without world recreation), our host systems are still
            // alive but their OnCreate hooks won't fire again, so the freshly-registered
            // façades above have CurrentHost==null. Walk the live world and rebind each
            // façade/state facade to the existing host. Cold-load case: this is a harmless idempotent
            // overwrite of what host.OnCreate just wrote.
            ProcessLifetimeFacadeBootstrapper.ReattachToLiveHosts(services, _world);

            // Hot-reload-in-game recovery: vanilla load events do not replay when the
            // mod is reloaded into an already-live city, so the lifecycle oracle must
            // recover from vanilla's current state.
            var gameManager = GameManager.instance;
            if (gameManager != null && gameManager.gameMode.IsGame() && !gameManager.isGameLoading)
                CivicGameLifecycle.SnapHotReloadReady(gameManager.gameMode);

            // Background check for newer config on server. Starts only after the immutable
            // bootstrap manifest has been built and consumed by ECS registration.
            remoteConfig.Refresh();

            // Register UI bindings (for React UI)
            RegisterUIBindings();
            BacktraceMarkers.Phase("Mod.OnLoad/UIBindings-done");

            Log.Info("Civic Survival loaded successfully!");
            Log.Info($"Mode: {(LocalizationManager.IsUkrainian ? "Realistic (UA)" : "Neutral (EN)")}");

            BacktraceMarkers.Phase("Mod.OnLoad/end");
        }

        /// <summary>
        /// Register UI bindings for React components.
        /// These expose C# values/methods to the JavaScript UI.
        /// </summary>
        private void RegisterUIBindings()
        {
            Log.Info("Registering UI bindings...");

            // UI bindings are registered via MainMenuShellUISystem (menu-safe
            // globals) and GameSessionUISystem (city-loaded). The values are
            // exposed to React via cs2/api bindValue()

            Log.Info("UI bindings registered.");
        }

        /// <summary>
        /// Register all domains for self-registration architecture.
        /// TIER 0: Bootstrap layer - determines WHICH systems to create.
        /// </summary>
        private void RegisterFeatures()
        {
            Log.Info("Registering domains...");

            var registry = FeatureRegistry.Instance;

            // Gameplay domains (priority 2000-2999) - sorted by priority
            registry.Register(new PowerGridDomain());         // 2000
            registry.Register(new PowerBackupDomain());       // 2970
            registry.Register(new BlackoutDomain());          // 2050
            registry.Register(new EffectsDomain());           // 2050 - before Threats (effect cache)
            registry.Register(new EngineeringDomain());       // 2100
            registry.Register(new Core.Features.Wellbeing.WellbeingFeature());                               // 2140
            registry.Register(new Core.Features.Population.PopulationFeature());                             // 2480
            registry.Register(new MobilizationDomain());      // 2150
            registry.Register(new ShadowEconomyDomain());     // 2151
            registry.Register(new EconomyDomain());           // 2200
            registry.Register(new FinanceDomain());          // 2210
            registry.Register(new CorruptionDomain());        // 2220
            registry.Register(new CountermeasuresDomain());   // 2240
            registry.Register(new NeighborEnvyDomain());      // 2250
            registry.Register(new DiplomacyDomain());         // 2270
            registry.Register(new ScenarioDomain());          // 2300
            registry.Register(new TutorialDomain());          // 2310
            registry.Register(new AttentionDomain());         // 2400
            registry.Register(new ThreatFlightDomain());       // 2501 — movement, obstacle avoidance
            registry.Register(new ThreatDamageDomain());       // 2502 — arrival, debris, damage
            registry.Register(new WavesDomain());              // 2520 — wave execution + spawn/target/cleanup (dep on ThreatsAirDefense 2511)
            registry.Register(new ThreatUIDomain());           // 2503 — identify, audio, UI
            registry.Register(new AirDefenseDomain());        // 2510
            registry.Register(new IntelDomain());             // 2512
            registry.Register(new SpottersDomain());          // 2514
            registry.Register(new CognitiveDomain());         // 2550
            registry.Register(new NotificationsDomain());     // 2590
            registry.Register(new NarrativeDomain());         // 2600
            registry.Register(new RefugeesDomain());          // 2700
            registry.Register(new GridWarfareDomain());       // 2800
            registry.Register(new NetworkDomain());           // 2850

            // Cross-domain coordinator features (Phase 5)
            registry.Register(new Core.Features.CrossDomain.DamageAccounting.DamageAccountingFeature());     // 2495
            registry.Register(new Core.Features.CrossDomain.ThreatsAirDefense.ThreatsAirDefenseFeature());  // 2511
            registry.Register(new Services.Arena.ArenaFeature());                                            // 2860
            registry.Register(new Core.Features.Efficiency.EfficiencyFeature());                             // 2215
            registry.Register(new Core.Features.Efficiency.EfficiencyFinalizeFeature());                     // 2960

            // UI domain (priority 3000+)
            registry.Register(new UIDomain());                // 3000
            registry.Register(new ArenaUIDomain());           // 3010 — Arena UI panel, depends on Arena (2860)

            Log.Info($"Features registered: {registry.Count}");
        }

        /// <summary>
        /// Called when the mod is unloaded.
        /// </summary>
        public void OnDispose()
        {
            SetUnloading(true);

            // Freeze VanillaReflectionRegistry BEFORE Harmony cleanup. Cached hits
            // continue to resolve so cleanup-path callers keep working; uncached
            // lookups during freeze return false silently (no AccessTools, no log,
            // no PatchStatusTracker.ReportFailure after PatchStatusTracker.Clear).
            Core.Infrastructure.VanillaReflectionRegistry.BeginUnload("Mod.OnDispose");

            Log.Info("Unloading Civic Survival...");

            CivicGameLifecycle.MarkNotReady("ModDispose");
            CivicGameLifecycle.UnregisterDefaultSubscribers();

            // Shutdown profiler (flush final report) — safety net for hot-reload-in-game
            // where the lifecycle event path may not run to completion.
            Core.Utils.PerformanceProfiler.Shutdown();

            // Stop background config fetch before disposing services
            m_RemoteConfig?.Shutdown();
            m_RemoteConfig = null;

            HarmonyPatchBootstrapper.Cleanup(HARMONY_ID, _harmony, _world);
            _harmony = null;

            // Dispose FeatureRegistry FIRST so domain-level Dispose() (e.g. NotificationsDomain
            // unregistering its SocialFeedService) can still see ServiceRegistry.
            if (FeatureRegistry.IsInitialized)
            {
                FeatureRegistry.Instance.Dispose();
            }

            // Dispose services before clearing EventBus (Dispose may Unsubscribe)
            if (ServiceRegistry.IsInitialized)
            {
                var services = ServiceRegistry.Instance;
                var eventBus = services.Get<IEventBus>() as EventBus;

                // Unregister services we registered in OnLoad. SocialFeedService teardown
                // moved to NotificationsDomain.Dispose() (called by FeatureRegistry.Dispose()).
                services.Unregister<ModSettings>();
                services.Unregister<IEventBus>();
                ProcessLifetimeFacadeBootstrapper.Unregister(services);
                services.Unregister<IDistrictStateReader>();
                services.Unregister<IDistrictStateWriter>();
                services.Unregister<IAutoDispatchStateWriter>();
                services.Unregister<IDistrictStateSerialization>();
                var audioManager = services.Get<AudioManager>();
                services.Unregister<AudioManager>();

                // Dispose IDisposable services — Unsubscribe from bus BEFORE Clear
                m_DistrictState?.Dispose();
                m_DistrictState = null;
                // S26-#6 FIX: AudioManager is MonoBehaviour, not IDisposable — call Cleanup explicitly
                audioManager?.Cleanup();

                // Clear remaining EventBus subscriptions after all Dispose/Unsubscribe calls
                eventBus?.Clear();
            }

            // Unregister from SingletonRegistry
            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Unregister<Mod>();
                SingletonRegistry.Instance.Unregister<AudioManager>();
            }

            // Dispose remaining registered services
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Dispose();
            }

            // Dispose SingletonRegistry
            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Dispose();
            }

            _world = null;

            // Clear static registries AFTER ServiceRegistry.Dispose — services may access these during teardown
            SatireRegistry.Clear();
            Localization.LocalizationManager.Cleanup();
            // CrisisEconomicsAdapter.ClearCache() removed — facade lifecycle handled by CrisisEconomicsSystem.OnDestroy.
            Core.UI.TriggerDispatch.ResetAll();
            Core.Utils.DiagnosticTracker.Reset();
            // EntityCountProbe.Reset() removed — queries owned by EntityCountProbeHost (ECS OnDestroy).
            Core.Infrastructure.PressureRegistry.Reset();
            // VanillaReflectionRegistry frozen via BeginUnload at the top of OnDispose;
            // a destructive Clear() here would re-burn the per-generation report-once
            // budget and discard FieldInfo entries that survive across the hot-reload
            // boundary (same AppDomain hosts the same Game.dll). See V_REGRESSION_FIX_PLAN_PHASE6_EXPANDED.md.

            Log.Info("Civic Survival unloaded.");
        }

    }
}
