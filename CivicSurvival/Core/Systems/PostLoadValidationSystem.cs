using System;
using System.Collections.Generic;
using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// One-shot system that runs IPostLoadValidation after deserialization.
    ///
    /// One-shot execution after load (m_SkipFrames=1 → skip one update, then run):
    ///   Restore singleton owners, run building-ref rebinds, run validators in
    ///   HydrationOrder, run initializables, then rebase throttled schedules to
    ///   their stagger phases so post-load refreshes are spread across their
    ///   normal intervals.
    ///
    /// Systems register via Register() in their OnCreate().
    /// NotifyLoadComplete() called after all Deserialize() completes.
    /// </summary>
    [ActIndependent]
    [FrameworkSystem]
    [ReentrantOneShot("PLVS runs once per load; re-arms via NotifyLoadComplete (:177); disables itself in OnUpdateImpl finally block (:393).")]
    public partial class PostLoadValidationSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("PostLoadValidation");

        private readonly PostLoadRegistry<IPostLoadValidation> m_Validators =
            new(16, nameof(IPostLoadValidation), v => v.HydrationOrder);
        private readonly PostLoadRegistry<IInitializable> m_Initializables =
            new(16, nameof(IInitializable), i => i.InitOrder);
        private readonly PostLoadRegistry<ICivicSingletonOwner> m_SingletonOwners =
            new(16, nameof(ICivicSingletonOwner));
        private readonly PostLoadRegistry<IBuildingRefRebindOwner> m_BuildingRefRebindOwners =
            new(8, nameof(IBuildingRefRebindOwner), o => o.RebindOrder);
        private readonly PostLoadRegistry<IResettable> m_Resettables =
            new(16, nameof(IResettable));
        private readonly PostLoadRegistry<ThrottledSystemBase> m_ThrottledSystems =
            new(16, nameof(ThrottledSystemBase));
        private readonly PostLoadRegistry<ThrottledUISystemBase> m_ThrottledUISystems =
            new(8, nameof(ThrottledUISystemBase));

        private bool m_ShouldRun;
        private int m_SkipFrames;
        [System.NonSerialized] private bool m_ArmedForGameplayLoad;
        [System.NonSerialized] private Purpose m_ArmedPurpose;
        [System.NonSerialized] private GameMode m_ArmedMode;
        // Set true by NotifyLoadComplete; reset to false at the start of OnGamePreload (the
        // normal cycle start). The OnGameLoadingComplete backstop reads it to recover a load
        // whose OnGamePreload/OnGameLoaded callbacks our systems missed by being created
        // mid-load (late playset activation — GameSystemBase wires these callbacks as event
        // subscriptions in OnCreate, so a system created after they fired never receives them).
        // Transient per-load; default false on a fresh instance, so the very first load fires
        // the backstop when both early callbacks were missed.
        [System.NonSerialized] private bool m_PostLoadRanThisCycle;
        private EntityQuery m_CurrentActQuery;

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            Enabled = false;
            Log.Info("Created (disabled until game load)");
        }

        /// <summary>
        /// Register a system that implements IPostLoadValidation.
        /// Called from system OnCreate().
        /// </summary>
        public void Register(IPostLoadValidation validator) => m_Validators.Register(validator);

        /// <summary>
        /// Remove a post-load validator when its owning system is destroyed.
        /// CivicSystemBase calls this automatically for systems implementing IPostLoadValidation.
        /// </summary>
        public void Unregister(IPostLoadValidation validator) => m_Validators.Unregister(validator);

        /// <summary>
        /// Register a system that implements IInitializable.
        /// Called automatically from CivicSystemBase.OnCreate() when the interface is detected.
        /// OnInitialize() runs in the post-load pass after validators and before throttled schedule rebasing.
        /// </summary>
        public void RegisterInitializable(IInitializable init)
        {
            m_Initializables.Register(init);
        }

        /// <summary>
        /// Remove an initializer when its owning system is destroyed.
        /// CivicSystemBase calls this automatically for systems implementing IInitializable.
        /// </summary>
        public void UnregisterInitializable(IInitializable init) => m_Initializables.Unregister(init);

        /// <summary>
        /// Register a system that owns a singleton and can recreate it after load.
        /// Called automatically from CivicSystemBase.OnCreate().
        /// </summary>
        public void RegisterSingletonOwner(ICivicSingletonOwner owner) => m_SingletonOwners.Register(owner);

        /// <summary>
        /// Remove a singleton owner when its owning system is destroyed.
        /// CivicSystemBase calls this automatically for systems implementing ICivicSingletonOwner.
        /// </summary>
        public void UnregisterSingletonOwner(ICivicSingletonOwner owner) => m_SingletonOwners.Unregister(owner);

        /// <summary>
        /// Register a system that owns post-load reconciliation for indexed building refs.
        /// Called automatically from CivicSystemBase.OnCreate().
        /// </summary>
        public void RegisterBuildingRefRebindOwner(IBuildingRefRebindOwner owner) => m_BuildingRefRebindOwners.Register(owner);

        /// <summary>
        /// Remove a building-ref rebind owner when its owning system is destroyed.
        /// </summary>
        public void UnregisterBuildingRefRebindOwner(IBuildingRefRebindOwner owner) => m_BuildingRefRebindOwners.Unregister(owner);

        /// <summary>
        /// Register a system with resettable runtime state for the new-game boundary.
        /// Called automatically from CivicSystemBase/CivicUISystemBase.OnCreate().
        /// </summary>
        public void RegisterResettable(IResettable resettable) => m_Resettables.Register(resettable);

        public void UnregisterResettable(IResettable resettable) => m_Resettables.Unregister(resettable);

        /// <summary>
        /// Register a throttled system for post-load schedule rebasing.
        /// Called from ThrottledSystemBase.OnCreate().
        /// </summary>
        public void RegisterThrottled(ThrottledSystemBase system) => m_ThrottledSystems.Register(system);

        public void UnregisterThrottled(ThrottledSystemBase system) => m_ThrottledSystems.Unregister(system);

        /// <summary>
        /// Register a throttled UI system for post-load schedule rebasing.
        /// Called from ThrottledUISystemBase.OnCreate().
        /// </summary>
        public void RegisterThrottledUI(ThrottledUISystemBase system) => m_ThrottledUISystems.Register(system);

        public void UnregisterThrottledUI(ThrottledUISystemBase system) => m_ThrottledUISystems.Unregister(system);

        /// <summary>
        /// Called after all Deserialize() methods have completed.
        /// Schedules validation after one skipped update, once other load callbacks settle.
        /// Triggered by OnGameLoaded override.
        /// </summary>
        private static bool IsGameplayLoad(Purpose purpose, GameMode mode)
            => mode == GameMode.Game && (purpose == Purpose.LoadGame || purpose == Purpose.NewGame);

        private void NotifyLoadComplete()
        {
            BuildingRefRebindRegistry.BeginPostLoadRebind();
            m_ShouldRun = true;
            m_SkipFrames = 1; // Skip one frame so sibling OnGameLoaded callbacks can settle.
            // Mark the pass scheduled for this cycle so the OnGameLoadingComplete backstop,
            // which fires after this on the normal path, sees it ran and does not re-fire.
            m_PostLoadRanThisCycle = true;
            Enabled = true;
            Log.Info("Load complete — post-load validation scheduled (frame +2)");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Fresh load cycle starts here on the normal path. Cleared so the
            // OnGameLoadingComplete backstop can distinguish a pass that has not run yet
            // from one this cycle already scheduled.
            m_PostLoadRanThisCycle = false;

            ArmForLoad(purpose, mode);
        }

        // Preload arming. Extracted from OnGamePreload so the OnGameLoadingComplete backstop
        // can run it when preload was missed (system created mid-load on a late playset
        // activation). Idempotent enough to re-run: ResetWatermarks/BeginPostLoadRebind are
        // already invoked twice on the normal path (preload + NotifyLoadComplete).
        private void ArmForLoad(Purpose purpose, GameMode mode)
        {
            var eventBus = ServiceRegistry.TryGet<IEventBus>();
            eventBus?.ResetWatermarks();

            m_ArmedForGameplayLoad = IsGameplayLoad(purpose, mode);
            m_ArmedPurpose = purpose;
            m_ArmedMode = mode;

            // Close the gameplay gate for the deserialize→validation window. On a
            // cold load the flags are already false (no-op); on a warm reload this
            // is the only place that resets IsGameplayReady, since vanilla skips
            // MainMenu/Cleanup when loading a save over a running game (decompile
            // GameManager.cs:1206-1208). The matching re-open is MarkGameplayReady
            // in this system's OnUpdate finally, after RestoreSingletonOwners.
            if (m_ArmedForGameplayLoad)
                CivicGameLifecycle.MarkReloadPending();

            // Vanilla reflection cache is NOT cleared on gameplay load boundary.
            // Game.dll compatibility does not change at LoadGame/NewGame (same AppDomain,
            // same loaded assembly), so clearing here would throw away successful
            // FieldInfo resolves and re-burn the per-generation report-once budget.
            // Generation/freeze lifecycle is owned by Mod.OnLoad / Mod.OnDispose.
            // See V_REGRESSION_FIX_PLAN_PHASE6_EXPANDED.md.

            if (purpose == Purpose.LoadGame)
            {
                BuildingRefRebindRegistry.BeginPostLoadRebind();
                SerializationGuard.BeginLoadSession();
            }
            else if (purpose == Purpose.NewGame)
            {
                RunNewGameResets();
                BuildingRefRebindRegistry.Reset();
            }
            else
            {
                // Other Purpose values (Asset/Editor/MainMenu/None/etc.) do not
                // touch the BuildingRef rebind registry — explicit no-op so
                // CIVIC102 sees the full enum covered.
            }
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            bool isArmedGameplayContext = m_ArmedForGameplayLoad
                && m_ArmedMode == GameMode.Game
                && serializationContext.purpose == m_ArmedPurpose
                && (serializationContext.purpose == Purpose.LoadGame || serializationContext.purpose == Purpose.NewGame);

            if (!isArmedGameplayContext)
            {
                if (m_ArmedForGameplayLoad)
                    CivicGameLifecycle.MarkGameplayReady(m_ArmedPurpose, m_ArmedMode);

                m_ArmedForGameplayLoad = false;
                m_ShouldRun = false;
                Enabled = false;
                // Do NOT touch m_PostLoadRanThisCycle here. If preload was missed (Window B:
                // created between OnGamePreload and OnGameLoaded), m_ArmedForGameplayLoad is
                // false so a real gameplay load lands here and returns WITHOUT scheduling —
                // leaving the flag false lets the OnGameLoadingComplete backstop recover it.
                Log.Info($"Ignoring non-gameplay load context purpose={serializationContext.purpose} armedPurpose={m_ArmedPurpose} mode={m_ArmedMode}");
                return;
            }

            RunLoadedSetupAndSchedule();
        }

        // Backstop for late playset activation. GameSystemBase subscribes OnGamePreload/
        // OnGameLoaded/OnGameLoadingComplete as events in OnCreate (decompile
        // GameSystemBase.cs:18-31), so a system created mid-load misses the earlier callbacks
        // but still receives this one (it fires last). When the normal preload→loaded handshake
        // never scheduled the pass for a gameplay load, run it here from the fully-deserialized
        // load-complete anchor. Recovers both miss windows: created after OnGameLoaded (preload
        // + loaded missed) and created between OnGamePreload and OnGameLoaded (loaded arrives
        // unarmed and self-ignores above). Gated on a gameplay load and on the pass not having
        // run this cycle, so the normal path never double-fires.
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (m_PostLoadRanThisCycle || !IsGameplayLoad(purpose, mode))
                return;

            Log.Warn($"Post-load pass missed its preload/loaded triggers (late playset activation) — running from load-complete backstop (purpose={purpose} mode={mode}).");
            ArmForLoad(purpose, mode);
            RunLoadedSetupAndSchedule();
        }

        // Loaded-phase setup + post-load scheduling. Extracted from OnGameLoaded so the
        // OnGameLoadingComplete backstop can run it too. Needs no serialization Context: base
        // OnGameLoaded is an empty virtual (decompile GameSystemBase.cs:119) and is called by
        // the real override; the fail-safe path reads the m_Armed* fields set by ArmForLoad.
        private void RunLoadedSetupAndSchedule()
        {
            try
            {
                SerializationGuard.FlushLoadReport();
                PressureRegistry.InvalidateValidation();
                ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).ResetPendingDeductions();
                TriggerDispatch.ResetAll();

                // Static modal mutex survives city/session loads. Reset once after every
                // deserialize pass, then validators can rebuild their own modal queues.
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
                ModalCoordinator.Instance.Reset();
#pragma warning restore CIVIC098

                // Publish GameLoadedEvent for telemetry
                var metadata = World.GetExistingSystemManaged<SaveMetadataSystem>();
#pragma warning disable CIVIC005 // Telemetry: graceful fallbacks during load — GTS/metadata may not be ready
                int gameDay = GameTimeSystem.Instance?.Current.CurrentDay ?? 0;
                var act = (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var ca)
                        ? ca : CurrentActSingleton.Default)
                    .CurrentAct;

                EventBus?.SafePublish(new GameLoadedEvent(
                    gameDay,
                    act,
                    metadata?.SavedModVersion ?? "",
                    metadata?.SavedFormatVersion ?? 0));
#pragma warning restore CIVIC005
            }
            catch (Exception ex)
            {
                Log.Error($"OnGameLoaded setup failed; continuing to post-load validation so gameplay gate can recover: {ex}");
            }
            finally
            {
                try
                {
                    NotifyLoadComplete();
                }
                catch (Exception ex)
                {
                    Log.Error($"NotifyLoadComplete failed; opening gameplay gate fail-safe: {ex}");
                    CivicGameLifecycle.MarkGameplayReady(m_ArmedPurpose, m_ArmedMode);
                    m_ArmedForGameplayLoad = false;
                    m_ShouldRun = false;
                    Enabled = false;
                }
            }
        }

        protected override void OnUpdateImpl()
        {
            if (!m_ShouldRun)
            {
                Enabled = false;
                return;
            }

            if (m_SkipFrames > 0)
            {
                m_SkipFrames--;
                return;
            }

            // Validators run only after the one-frame load-callback settle window.
            try
            {
                RestoreSingletonOwners();
                RunBuildingRefRebindOwners();
                RunValidation();
            }
            catch (Exception ex)
            {
                Log.Error($"Post-load validation failed: {ex}");
            }
            finally
            {
                if (m_ArmedForGameplayLoad && IsGameplayLoad(m_ArmedPurpose, m_ArmedMode))
                    CivicGameLifecycle.MarkGameplayReady(m_ArmedPurpose, m_ArmedMode);

                m_ArmedForGameplayLoad = false;
                m_ShouldRun = false;
                Enabled = false;
            }
        }

        private void RunValidation()
        {
            // Sort by HydrationOrder to ensure deterministic execution —
            // systems writing split capacity components or
            // depending on each other (ShadowWallet→PlayerAttack) run in correct sequence.
            Log.Info($"Running post-load validation on {m_Validators.Count} systems");

#pragma warning disable CIVIC050 // One-shot post-load, not per-frame
            var failedSystemNames = new List<string>();
#pragma warning restore CIVIC050

            var (successes, failures) = RunAll(
                m_Validators, "PostLoadValidation failed in", "Post-load validation",
                v => v.ValidateAfterLoad(),
                sort: true,
                onSuccess: (v, name) =>
                {
                    if (v.HydrationOrder != HydrationPriority.DEFAULT)
                        Log.Info($"  [{v.HydrationOrder}] {name} OK");
                },
                failedNamesOut: failedSystemNames);

            // Purge pass — strictly after every reconcile pass so a consumer that
            // destroys a shared transient type cannot drop entities another
            // validator still reconcile-reads. SAVE_LOAD_LIFECYCLE_DOCTRINE.md
            // Invariant 5 "Phase split". Same HydrationOrder (m_Validators already
            // stably sorted above, so no re-sort here).
            RunAll(m_Validators, "PurgeAfterLoad failed in", "Post-load purge",
                v => v.PurgeAfterLoad());

            // Publish validation results for telemetry
            string? failedNames = failedSystemNames.Count > 0 ? string.Join(",", failedSystemNames) : null;
            EventBus?.SafePublish(new LoadValidationCompleteEvent(successes, failures, failedNames));

            // IInitializable: compute derived singletons before throttled systems read them.
            // Runs after validators (repaired state) and before schedule rebasing.
            RunInitializables();

            ResetThrottledSchedulesAfterLoad();
        }

        /// <summary>
        /// Recreate owned singleton components before validators and UI hydration read them.
        /// </summary>
        private void RestoreSingletonOwners()
        {
            if (m_SingletonOwners.Count == 0)
                return;

            RunAll(m_SingletonOwners, "Singleton restore failed in", "Singleton restore",
                owner => owner.OnLoadRestore(EntityManager));
        }

        /// <summary>
        /// Reconcile typed indexed building refs before cleanup can purge them.
        /// </summary>
        private void RunBuildingRefRebindOwners()
        {
            if (m_BuildingRefRebindOwners.Count == 0)
                return;

            RunAll(m_BuildingRefRebindOwners, "Building-ref rebind failed in", "Building-ref rebind",
                owner => owner.RebindBuildingRefsAfterLoad(EntityManager),
                sort: true,
                onSuccess: (owner, name) =>
                {
                    var reboundTypes = owner.ReboundComponentTypes;
                    if (reboundTypes != null)
                    {
                        for (int t = 0; t < reboundTypes.Count; t++)
                            BuildingRefRebindRegistry.MarkComplete(reboundTypes[t], name);
                    }
                    if (owner.RebindOrder != HydrationPriority.DEFAULT)
                        Log.Info($"  [{owner.RebindOrder}] {name} building-ref rebind OK");
                });
        }

        private void RunNewGameResets()
        {
            if (m_Resettables.Count == 0)
                return;

            RunAll(m_Resettables, "New-game ResetState failed in", "New-game reset",
                resettable => resettable.ResetState());
        }

        /// <summary>
        /// Call OnInitialize() on all registered IInitializable systems in InitOrder.
        /// Runs in the post-load pass after IPostLoadValidation validators, before throttled schedule rebasing.
        /// </summary>
        private void RunInitializables()
        {
            if (m_Initializables.Count == 0)
                return;

            RunAll(m_Initializables, "IInitializable.OnInitialize failed in", "Initialization",
                init => init.OnInitialize(),
                sort: true,
                onSuccess: (init, name) =>
                {
                    if (init.InitOrder != InitPriority.DEFAULT)
                        Log.Info($"  [{init.InitOrder}] {name} initialized");
                });
        }

        /// <summary>
        /// Reset all registered throttled systems to their configured stagger phase.
        /// This clears stale force flags and timestamps after load without scheduling
        /// every throttled system to run in one frame. Disabled systems are skipped;
        /// the combined count is logged once instead of a per-registry summary.
        /// </summary>
        private void ResetThrottledSchedulesAfterLoad()
        {
            var (count, _) = RunAll(
                m_ThrottledSystems, "Throttled schedule reset failed in", null,
                sys => sys.ResetPostLoadThrottleSchedule(),
                filter: sys => sys.Enabled,
                logSummary: false);

            var (uiCount, _) = RunAll(
                m_ThrottledUISystems, "UI throttled schedule reset failed in", null,
                sys => sys.ResetPostLoadThrottleSchedule(),
                filter: sys => sys.Enabled,
                logSummary: false);

            Log.Info($"Reset post-load throttle schedules on {count} throttled + {uiCount} UI systems");
        }

        /// <summary>
        /// Shared post-load execution loop: optionally stable-sort, then run
        /// <paramref name="action"/> on each registered participant inside a
        /// try/catch, tally successes/failures, and emit the standard summary log.
        /// Failures log "<paramref name="failureVerb"/> {name}{order}: {ex}" and
        /// (when provided) append the participant name to <paramref name="failedNamesOut"/>.
        /// </summary>
        private (int successes, int failures) RunAll<T>(
            PostLoadRegistry<T> registry,
            string failureVerb,
            string? summaryLabel,
            Action<T> action,
            bool sort = false,
            Action<T, string>? onSuccess = null,
            Func<T, bool>? filter = null,
            List<string>? failedNamesOut = null,
            bool logSummary = true) where T : class
        {
            if (sort)
                registry.StableSort();

            int successes = 0;
            int failures = 0;
            var items = registry.Items;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (filter != null && !filter(item))
                    continue;

                var name = registry.NameOf(item);
                try
                {
                    action(item);
                    onSuccess?.Invoke(item, name);
                    successes++;
                }
                catch (Exception ex)
                {
                    failures++;
                    failedNamesOut?.Add(name);
                    Log.Error($"{failureVerb} {name}{registry.OrderSuffix(item)}: {ex}");
                }
            }

            if (logSummary && summaryLabel != null)
            {
                if (failures > 0)
                    Log.Warn($"{summaryLabel}: {successes} OK, {failures} FAILED");
                else
                    Log.Info($"{summaryLabel}: {successes} OK");
            }

            return (successes, failures);
        }

        protected override void OnDestroy()
        {
            BuildingRefRebindRegistry.Reset();
            m_Validators.Clear();
            m_Initializables.Clear();
            m_SingletonOwners.Clear();
            m_BuildingRefRebindOwners.Clear();
            m_Resettables.Clear();
            m_ThrottledSystems.Clear();
            m_ThrottledUISystems.Clear();
            base.OnDestroy();
        }
    }
}
