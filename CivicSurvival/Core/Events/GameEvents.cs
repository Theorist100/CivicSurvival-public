using System.Collections.Generic;
using System.Collections.ObjectModel;
using CivicSurvival.Core.Features.Wellbeing;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Events
{
    public record SocialPostEvent(string Author, string Message, SocialMood Mood) : IGameEvent;

    public record NewsPostEvent(
        string Id,
        string Source,
        string Title,
        string Body,
        SocialMood Mood,
        long Timestamp,
        string Category) : IGameEvent;

    public record BuildingDamagedEvent(int BuildingIndex, int DamageAmount) : IGameEvent;

    /// <summary>
    /// Diagnostic: the drone obstacle-avoidance grid pointed at a building index out of
    /// range for the building cache. The read was skipped (native AV guard) instead of
    /// crashing the Burst worker. Published once per drain cycle with the worst-seen index
    /// so telemetry can size the producer desync. Published by: ThreatMovementSystem.
    /// </summary>
    public record ObstacleIndexOobEvent(int BuildingIdx, int BuildingsLength, int GridCount, int OccurrenceCount) : IGameEvent;

    /// <summary>
    /// Diagnostic: a building spatial-hash query pointed at a Positions index out of range for
    /// the building frame cache. The read was skipped (bounds guard) instead of throwing on the
    /// main thread. Published once per drain cycle with the worst-seen index so telemetry can
    /// size the hash/array desync. Published by: ThreatDamageSystem.
    /// </summary>
    public record SpatialIndexOobEvent(int BuildingIdx, int PositionsLength, int HashCount, int OccurrenceCount) : IGameEvent;

    /// <summary>
    /// A2 FIX: L1→L1 blackout recovery event (replaces BlackoutRecoveryEvent).
    /// Published by BlackoutEventProducerSystem (Layer 1) when district transitions
    /// from blackout to powered. Eliminates L3→L1 backward flow.
    ///
    /// Published by: BlackoutEventProducerSystem
    /// Consumed by: ScenarioStatisticsSystem
    /// </summary>
    public record BlackoutRecoveredEvent(int DistrictIndex) : IGameEvent;

    public record PenaltyRegisteredEvent(int DistrictIndex, PenaltySource Source) : IGameEvent;
    public record PenaltyRemovedEvent(int DistrictIndex, PenaltySource Source) : IGameEvent;

    /// <summary>
    /// Command to override exodus rate during special phases (Crisis act).
    /// 0 = use normal shock-based calculation, > 0 = forced rate.
    ///
    /// Published by: CrisisActCoordinator (Scenario domain)
    /// Consumed by: ScenarioStateMachine → writes to ScenarioSingleton.ExodusRateOverrideFraction
    /// </summary>
    public record ExodusRateOverrideFractionCommand(float RateFraction) : IGameEvent;

    /// <summary>
    /// Command event to spawn a wave of threats.
    /// Published by: Scenario (IntroScenarioSystem, etc.)
    /// Consumed by: ThreatSpawnSystem
    /// </summary>
    public record SpawnWaveRequestEvent(
        int ThreatCount, int WaveNumber, WaveType WaveType,
        int BallisticOverride = -1,
        WaveRole WaveRole = WaveRole.Regular) : IGameEvent;

    /// <summary>
    /// Notification that threats were spawned.
    /// Published by: ThreatSpawnSystem (after SpawnWave completes)
    /// Consumed by: WaveExecutor (updates counters)
    ///
    /// This ensures counters are updated regardless of spawn source
    /// (WaveExecutor, IntroScenarioSystem, debug tools, etc.)
    /// </summary>
    public record ThreatsSpawnedEvent(int ShahedCount, int BallisticCount, int WaveNumber) : IGameEvent;

    /// <summary>
    /// Published when district power/schedule state changes.
    /// Decouples domain systems from NeighborEnvy via EventBus.
    ///
    /// Publishers:
    /// - BlackoutEventProducerSystem (blackout start/end transitions)
    /// - DistrictUISystem (pause-safe player district controls)
    /// - AutoDispatchSystem (auto-shed schedule changes)
    /// - SettingsRequestSystem (settings changes)
    ///
    /// Consumers:
    /// - NeighborEnvySystem (incremental envy update for changed + adjacent districts)
    /// </summary>
    public record DistrictStateChangedEvent(int DistrictIndex) : IGameEvent;

    /// <summary>
    /// District lifecycle type for DistrictLifecycleEvent.
    /// </summary>
    public enum DistrictLifecycle
    {
        Created = 0,
        Destroyed
    }

    /// <summary>
    /// Published when district entity is created or destroyed.
    /// Solves Entity.Index reuse problem: when district is destroyed,
    /// subscribers can clean up state keyed by DistrictIndex.
    ///
    /// Published by: DistrictLifecycleSystem
    /// Consumed by: ThreadSafeDistrictState (cleanup on Destroyed)
    /// </summary>
    public record DistrictLifecycleEvent(int DistrictIndex, DistrictLifecycle Lifecycle) : IGameEvent;

    /// <summary>
    /// Trigger for narrative system to generate a social post.
    /// Decouples event sources from character/message selection.
    ///
    /// Published by: Any domain (Threats, Blackout, etc.)
    /// Consumed by: NarrativeNotificationSystem
    ///
    /// TriggerKey maps to SatireConfig which defines author and message.
    /// ContextData carries serialized values for message formatting and is copied
    /// at construction so queued narrative handlers cannot observe caller mutation.
    /// </summary>
    public record NarrativeTriggerEvent : IGameEvent
    {
        public string TriggerKey { get; }
        public IReadOnlyDictionary<string, string> ContextData { get; }

        private static readonly IReadOnlyDictionary<string, string> s_EmptyContext =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        public NarrativeTriggerEvent(
            string triggerKey,
            IReadOnlyDictionary<string, string>? contextData = null)
        {
            TriggerKey = triggerKey;
            ContextData = contextData == null
                ? s_EmptyContext
                : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(contextData));
        }
    }

    /// <summary>
    /// Published when corruption should be added from external source.
    /// Decouples Scenario/Services domains from Countermeasures domain.
    ///
    /// Published by: AirDefenseActionRequestSystem, etc.
    /// Consumed by: CountermeasuresSystem
    /// </summary>
    /// <param name="Amount">Corruption points to add</param>
    /// <param name="Source">Source identifier for logging (e.g., "BankingWithdraw", "SpotterSBU")</param>
    public record CorruptionGainEvent(float Amount, string Source) : IGameEvent;

    /// <summary>
    /// FIX T17-04/05: Published when player makes investigation or police choice.
    /// Enables telemetry tracking of critical game decisions.
    ///
    /// Published by: CountermeasuresSystem
    /// Consumed by: TelemetryService
    /// </summary>
    /// <param name="ChoiceType">"investigation" or "police"</param>
    /// <param name="Choice">Choice enum value as string</param>
    /// <param name="Result">Outcome of the choice</param>
    public record CountermeasuresChoiceEvent(string ChoiceType, string Choice, string Result) : IGameEvent;

    /// <summary>
    /// Published when game day changes (midnight transition).
    /// Eliminates duplicate day detection logic across systems.
    ///
    /// Published by: TimeSystem
    /// Consumed by: Corruption schemes (daily income), etc.
    /// EventBus watermarks are reset on load by PostLoadValidationSystem so loading
    /// an earlier save can re-deliver the loaded day.
    /// </summary>
    /// <param name="DayNumber">Total days since game start</param>
    /// <param name="Hour">Current hour (0-24) at time of event</param>
    public record DayChangedEvent(int DayNumber, float Hour) : ISequencedEvent
    {
        public long Sequence => DayNumber;
    }

    /// <summary>
    /// Published when ShadowWalletSystem fails to deduct shadow funds via ECB request.
    /// Consumers can react to failed deductions (e.g., disable imports).
    ///
    /// Published by: ShadowWalletSystem (ProcessDeductRequests)
    /// Consumed by: ShadowTradeDailySystem (disables import on failure),
    ///              IntelPurchaseSystem (rolls back intel grants/upgrades by Reason)
    /// </summary>
    /// <param name="Amount">Requested deduction amount that failed</param>
    /// <param name="Reason">Source identifier (e.g., "ShadowImport")</param>
    public record ShadowDeductFailedEvent(long Amount, string Reason) : IGameEvent;

    /// <summary>
    /// Published when ShadowWalletSystem successfully deducts shadow funds via ECB request.
    /// Consumers that need confirmed accounting should listen here rather than at request creation.
    /// </summary>
    /// <param name="Amount">Deducted amount</param>
    /// <param name="Reason">Source identifier (e.g., "IntelPurchaseSystem.Upgrade:1")</param>
    public record ShadowDeductSucceededEvent(long Amount, string Reason) : IGameEvent;

    /// <summary>
    /// Trigger for First Strike cascade targeting system.
    /// Decouples Scenario domain from Threats domain.
    /// Narrative event for First Strike scenario (drone wave at Crisis start).
    /// NOTE: Actual damage is done by drones via ThreatDamageSystem.
    /// This event is purely for narrative/tutorial/statistics.
    ///
    /// Published by: CrisisActCoordinator.StartCrisisAct()
    /// Consumed by: FirstStrikeNarrativeResolver, ScenarioStatisticsSystem, WorldShockSystem
    /// </summary>
    /// <param name="PlannedHits">Number of drones in wave (for narrative text)</param>
    public record FirstStrikeCascadeEvent(int PlannedHits) : IGameEvent;

    // ==================== Scenario Events ====================

    /// <summary>
    /// Published when intro cinematic sequence completes.
    ///
    /// Published by: IntroScenarioSystem (single publisher)
    /// Consumed by: ScenarioStateMachine (triggers StartWar in city path)
    /// Single-publisher drift is guarded by CIVIC242.
    /// </summary>
    public record IntroCompleteEvent : IGameEvent;

    /// <summary>
    /// Published when OminousSignsSystem decides war should begin (village/RNG path).
    /// ScenarioStateMachine handles this as an alternative trigger to IntroCompleteEvent.
    ///
    /// Published by: OminousSignsSystem (single publisher)
    /// Consumed by: ScenarioStateMachine (triggers StartWar in village path)
    /// Single-publisher drift is guarded by CIVIC242.
    /// </summary>
    public record WarStartRequestEvent : IGameEvent;

    /// <summary>
    /// Published when Crisis Act requests transition to a new act.
    /// Decouples CrisisActCoordinator from ScenarioDirectorSystem.
    ///
    /// Published by: CrisisActCoordinator
    /// Consumed by: ScenarioDirectorSystem
    /// </summary>
    public record ActTransitionRequestEvent(Act NewAct) : IGameEvent;

    /// <summary>
    /// Published when war day changes (day count since war started).
    /// Centralizes war day tracking instead of each system tracking independently.
    ///
    /// Published by: TimeSystem
    /// Consumed by: ScenarioDirectorSystem, CrisisActCoordinator, ExodusSystem, etc.
    /// EventBus watermarks are reset on load by PostLoadValidationSystem so loading
    /// an earlier save can re-deliver the loaded war day.
    /// </summary>
    /// <param name="WarDay">Days since war started (0 = first day of war)</param>
    /// <param name="GameDay">Total game days since start</param>
    // FIX L15: Add ISequencedEvent — prevents duplicate delivery on same WarDay
    public record WarDayChangedEvent(int WarDay, int GameDay) : ISequencedEvent
    {
        public long Sequence => WarDay;
    }

    /// <summary>
    /// Signals that war has officially started.
    ///
    /// <param name="Milestone">Achieved city milestone at war start — the war trigger point
    /// for the Village scenario; -1 if the milestone singleton was unavailable.</param>
    /// <param name="Population">City population at war start.</param>
    ///
    /// Published by: ScenarioStateMachine.StartWar (single publisher)
    /// Consumed by: GameTimeSystem, CognitiveStateSystem, TelemarathonSystem,
    ///              MilestoneTutorialSystem, TelemetryEventListener,
    ///              CrisisActCoordinator, WaveScheduler
    /// </summary>
    public record WarStartedEvent(int Milestone, int Population) : IGameEvent;

    /// <summary>
    /// Published when ScenarioStateMachine detects scenario type based on population.
    /// Allows systems to react to Village/Town/City branch selection.
    ///
    /// Published by: ScenarioStateMachine.DetectScenarioType()
    /// Consumed by: OminousSignsSystem (activates PreWar for Village), IntroScenarioSystem
    /// </summary>
    /// <param name="Type">Detected scenario type (Village/Town/City)</param>
    /// <param name="Population">Population at detection time</param>
    public record ScenarioTypeDetectedEvent(ScenarioType Type, int Population) : IGameEvent;

    /// <summary>
    /// Published when refugee influx overwhelms infrastructure (water/sewage).
    /// Triggers penalties and potential system degradation.
    ///
    /// Published by: RefugeeSpawnSystem.ShowCollapseModal()
    /// Consumed by: DistrictPenaltySystem (registers Infrastructure penalty),
    ///              TelemetryEventListener
    /// </summary>
    /// <param name="RefugeeCount">Total refugees that caused collapse</param>
    /// <param name="OriginalPopulation">Original city population before influx</param>
    /// <param name="PopulationRatio">Ratio of refugees to original (e.g., 5.0 = 5x)</param>
    public record InfrastructureCollapseEvent(
        int RefugeeCount,
        int OriginalPopulation,
        float PopulationRatio
    ) : IGameEvent;

    // ==================== Network/Telemetry Events ====================

    /// <summary>
    /// Command event to set player nickname.
    /// Published by UI when user enters a nickname in settings.
    /// TelemetryService subscribes to register with server.
    ///
    /// FIX BUG-TEL-001: Moved from Domains.Network.Events to Core/Events
    /// to allow Services to consume without violating architecture rules.
    /// </summary>
    public record SetNicknameCommand(string Nickname, int RequestId = 0, double CreatedTime = 0d) : IGameEvent;

    /// <summary>
    /// Authoritative post-write signal that the Online connection state has settled.
    /// Published by the single writer (GlobalNewsSystem.OnToggleConnection) AFTER it
    /// has applied the new value to ModSettings (ApplyPatch) and the global ConsentStore,
    /// carrying the final <paramref name="Enabled"/> value.
    ///
    /// Functional / identity consumers (TelemetryService identity, PersonalChronicleSystem,
    /// ArenaLeaderboardSystem) react to THIS — they read <paramref name="Enabled"/> from the
    /// event instead of re-reading ModSettings.NetworkConnectionEnabled or reloading
    /// TelemetryConfig. This removes the dispatch-order race on the raw
    /// ToggleGlobalConnectionCommand: the value is already written when this fires, and it
    /// is carried in the event, so the order in which the writer and the consumers
    /// subscribed no longer matters.
    ///
    /// Distinct from <c>GlobalConnectionChangedEvent</c>, which reports network REACHABILITY
    /// (connecting / connected / lost, driven by background fetch callbacks); this reports
    /// the user's Online opt-in SETTING after it is persisted.
    ///
    /// Lives in Core (not Domains.Network.Events) so Services/Core consumers can subscribe
    /// without importing the Network domain (AXIOM 5), mirroring SetNicknameCommand.
    /// </summary>
    public record OnlineConnectionStateChangedEvent(bool Enabled) : IGameEvent;

    /// <summary>
    /// Published by IntroScenarioSystem when cinematic enters attack phase.
    /// Consumed by WaveScheduler to calculate and schedule the intro wave through standard flow.
    /// </summary>
    public record IntroAttackEvent : IGameEvent;

    // ==================== Wave Scheduling Events ====================

    /// <summary>
    /// Command event to schedule a wave phase transition.
    /// Published by: WaveScheduler (Scenario domain)
    /// Consumed by: WaveExecutor (Threats domain)
    ///
    /// This implements "Writer Notifies" pattern:
    /// - Scheduler decides WHEN to attack
    /// - Executor writes to WaveStateSingleton and executes transitions
    /// </summary>
    /// <param name="TargetPhase">The phase to transition to</param>
    /// <param name="WaveNumber">Public wave number: 0 means no wave, 1 intro, 2+ regular</param>
    /// <param name="WaveType">Wave type (Harassment or MassiveStrike)</param>
    /// <param name="ThreatsExpected">Expected threat count for this wave</param>
    /// <param name="PhaseDuration">Duration of the phase in seconds; 0 means consumer default duration</param>
    /// <param name="IsDoubleTap">True if this is a storm double-tap scenario</param>
    /// <param name="BallisticOverride">Ballistic threat override; -1 means use wave defaults</param>
    /// <param name="WaveRole">Semantic wave role; do not infer intro from the number</param>
    public record ScheduleWaveCommand(
        GamePhase TargetPhase,
        int WaveNumber,
        WaveType WaveType = WaveType.Harassment,
        int ThreatsExpected = 0,
        float PhaseDuration = 0f,
        bool IsDoubleTap = false,
        int BallisticOverride = -1,
        WaveRole WaveRole = WaveRole.Regular
    ) : IGameEvent;

    /// <summary>
    /// WaveScheduler сообщает Executor, что Calm истёк, но запуск отложен до окна
    /// рассвета/заката (или ожидание снято). Executor — единственный писатель
    /// WaveStateSingleton.WaitingForLaunchWindow.
    /// Published by: WaveScheduler (Scenario domain). Consumed by: WaveExecutor.
    /// </summary>
    /// <param name="Waiting">True пока Scheduler ждёт dawn/dusk-окна в Calm.</param>
    public record WaveLaunchWindowWaitEvent(bool Waiting) : IGameEvent;

    // ==================== Game Over ====================

    /// <summary>
    /// Published when defeat conditions are met.
    /// Consumed by: ScenarioUISystem (shows defeat modal), TelemetryService
    /// </summary>
    public record GameOverEvent(DefeatCause Cause, int DaysSurvived) : IGameEvent;

    /// <summary>
    /// Debug: resupply all AA installations for free.
    /// Consumed by: AAAmmoSystem (calls ResupplyAll).
    /// </summary>
    public readonly struct DebugResupplyAllCommand : IGameEvent { }

    // ==================== Tutorial Events ====================

    // ==================== Session Lifecycle Events ====================

    /// <summary>
    /// Published after game is saved (flag pattern: set in Serialize, publish in OnUpdate).
    /// Published by: SaveMetadataSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record GameSavedEvent(int GameDay, Act CurrentAct) : IGameEvent;

    /// <summary>
    /// Published when game finishes loading (after deserialization, before validation).
    /// Published by: PostLoadValidationSystem.OnGameLoaded
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record GameLoadedEvent(int GameDay, Act CurrentAct, string SavedModVersion, byte SavedFormatVersion) : IGameEvent;

    /// <summary>
    /// Published after post-load validation completes with success/failure counts.
    /// Published by: PostLoadValidationSystem.RunValidation
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record LoadValidationCompleteEvent(int Successes, int Failures, string? FailedSystems) : IGameEvent;

    // ==================== Tutorial Telemetry Events ====================

    /// <summary>
    /// Published when a crisis tutorial step is shown.
    /// Published by: CrisisTutorialSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record TutorialStepEvent(string StepName, string Trigger) : IGameEvent;

    /// <summary>
    /// Published when player dismisses a tutorial modal.
    /// Published by: CrisisTutorialSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record TutorialStepDismissedEvent(string StepName, float DurationSeconds) : IGameEvent;

    /// <summary>
    /// Published when leaving Crisis act with summary of tutorial progress.
    /// Published by: CrisisTutorialSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record TutorialCrisisSummaryEvent(int StepsShown, int StepsTotal, int CrisisDurationDays) : IGameEvent;

    // ==================== Settings Events ====================

    /// <summary>
    /// Published when player changes a setting via UI.
    /// Published by: SettingsUISystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record SettingChangedEvent(string SettingName, string OldValue, string NewValue) : IGameEvent;

    // ==================== Spotter Telemetry Events ====================

    /// <summary>
    /// Published when player executes a spotter countermeasure action.
    /// Published by: SpotterAggregateSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record SpotterActionEvent(string ActionType, long Cost, bool Succeeded) : IGameEvent;

    /// <summary>
    /// Published when counter-OSINT is toggled on or off.
    /// Published by: SpotterAggregateSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
#pragma warning disable S101 // OSINT is a standard acronym
    public record CounterOSINTToggledEvent(bool Enabled) : IGameEvent;
#pragma warning restore S101

    // ==================== Intel Telemetry Events ====================

    /// <summary>
    /// Published when player purchases an insider.
    /// Published by: IntelPurchaseSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record IntelInsiderPurchasedEvent(long Cost) : IGameEvent;

    /// <summary>
    /// Published when player upgrades intel level.
    /// Published by: IntelPurchaseSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public record IntelUpgradedEvent(int NewLevel, long Cost) : IGameEvent;

    // ==================== Tutorial Events ====================

    /// <summary>
    /// Published when a tutorial modal is shown and should be marked as "seen".
    /// Decouples Tutorial domain from ScenarioState mutation.
    ///
    /// Published by: MilestoneTutorialSystem (Tutorial)
    /// Consumed by: ScenarioStateMachine (Scenario)
    /// </summary>
    public record ModalShownEvent(ModalFlags Flag) : IGameEvent;

    /// <summary>
    /// Published whenever ModalCoordinator activates a modal id.
    /// Direct shows, preemptions, and queue promotions all emit this event exactly once.
    /// Duplicate idempotent TryShow calls do not emit it.
    /// </summary>
    public record ModalActivatedEvent(string Id) : IGameEvent;

    /// <summary>
    /// Published when ModalCoordinator.Reset() clears the active slot.
    /// Allows all systems with active modal bindings to dismiss them,
    /// preventing orphaned visible modals after reset (save/load, act transition).
    ///
    /// Published by: ModalCoordinator.Reset()
    /// Consumed by: MilestoneTutorialSystem (clears all 8 bindings)
    /// </summary>
    public readonly struct ModalResetEvent : IGameEvent { }

    // ==================== Population Events ====================

    /// <summary>
    /// Published when refugees are successfully spawned.
    /// Decouples Refugees domain from Scenario statistics.
    ///
    /// Published by: RefugeeSpawnSystem.NotifyRefugeeAdded()
    /// Consumed by: ScenarioStatisticsSystem (RecordRefugeesReceived)
    /// </summary>
    /// <param name="Count">Number of refugees added this batch</param>
    public record RefugeesReceivedEvent(int Count) : IGameEvent;

    /// <summary>
    /// Published when citizens leave due to exodus.
    /// Decouples Attention domain from Scenario statistics.
    ///
    /// Published by: ExodusSystem (after applying exodus effects)
    /// Consumed by: ScenarioStatisticsSystem (RecordCitizensLeft)
    /// </summary>
    /// <param name="Count">Number of citizens leaving this batch</param>
    public record CitizensLeftEvent(int Count) : IGameEvent;

    // ==================== AirDefense Events ====================

    /// <summary>
    /// Event published when heritage AA is granted to player.
    /// Used for narrative messaging (TRO Commander notification).
    ///
    /// Published by: HeritageGrantSystem
    /// Consumed by: ThreatNarrativeResolver
    /// </summary>
    public readonly struct HeritageGrantedEvent : IGameEvent
    {
        public readonly int Count;
        public readonly int ProductionMW;

        public HeritageGrantedEvent(int count, int productionMW)
        {
            Count = count;
            ProductionMW = productionMW;
        }
    }
}
