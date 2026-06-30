using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Domain.Economy;

namespace CivicSurvival.Core.Events
{
    // Threats - HYBRID events (have logic subscribers, keep as-is)
    public record ThreatImpactEvent(float3 Position, bool IsBallistic) : IGameEvent;
    public record ThreatInterceptEvent(float3 Position, bool IsBallistic) : IGameEvent;
    /// <summary>
    /// Published when a wave ends (Recovery phase starts).
    /// Contains full debriefing statistics.
    /// </summary>
    public record WaveEndedEvent(
        int WaveNumber,
        int Intercepted,
        int Hits,
        int ShotsFired = 0,
        int Casualties = 0,
        int DamageCost = 0,
        long InfrastructureDamageCost = 0,
        int Crashed = 0,
        WaveRole WaveRole = WaveRole.Regular,
        // Balance-telemetry breakdown (developer diagnostics, balance.wave_result). All default to 0
        // so existing publishers and the functional threat.wave_ended consumer are unaffected — only
        // the balance listener reads these.
        int DroneIntercepted = 0,
        int DroneHits = 0,
        int BallisticIntercepted = 0,
        int BallisticHits = 0,
        int RoundsConsumed = 0,
        int MissilesConsumed = 0
    ) : IGameEvent
    {
        public bool HadDamage => Hits > 0 || Casualties > 0 || DamageCost > 0 || InfrastructureDamageCost > 0;
    }

    /// <summary>
    /// R4-S6-11 FIX: Published AFTER WaveEndedEvent — damage costs already deducted.
    /// Subscribers that spend budget (resupply) listen here instead of WaveEndedEvent
    /// to avoid racing with debt/damage deductions.
    /// </summary>
    public record WaveSettledEvent(int WaveNumber) : IGameEvent;

    /// <summary>
    /// Event type for consolidated ThreatNarrativeEvent.
    /// Payload contract:
    /// - WavePhaseChanged: Phase, WaveNumber, ThreatCount
    /// - ThreatAlert: ThreatCount, Position
    /// - DebrisDamage: Position
    /// - HospitalHitScandal: Position
    /// - PowerPlantDamaged: LostMW, RemainingMW, IsFirstHit, AffectedPlantCount
    /// - RepairNoFunds: LostMW, RemainingMW
    /// - AAInstallationLost: Position
    /// </summary>
    public enum ThreatNarrativeEventType
    {
        WavePhaseChanged = 0,
        ThreatAlert,
        DebrisDamage,
        HospitalHitScandal,
        PowerPlantDamaged,
        RepairNoFunds,
        AAInstallationLost
    }

    /// <summary>
    /// Consolidated threat narrative event (replaces 6 separate events).
    /// Published by: WaveExecutor, ThreatDamageSystem, OperationalDamageSystem
    /// Consumed by: ThreatNarrativeResolver
    /// </summary>
    public record ThreatNarrativeEvent(
        ThreatNarrativeEventType Type,
        GamePhase Phase = GamePhase.Calm,
        int WaveNumber = 0,
        int ThreatCount = 0,
        float3 Position = default,
        int LostMW = 0,
        int RemainingMW = 0,
        bool IsFirstHit = false,  // FIX N4-03: First hit of a wave - enables "under attack" narrative
        int AffectedPlantCount = 0
    ) : IGameEvent;

    /// <summary>
    /// Result type for consolidated AA resupply event.
    /// </summary>
    public enum AAResupplyResult { Full = 0, Partial, Failed, Emergency }

    /// <summary>
    /// Consolidated AA resupply event (replaces 4 separate events).
    /// Published by: AAAmmoSystem, AAResupplyPipelineSystem, AirDefenseActionRequestSystem
    /// Consumed by: ThreatNarrativeResolver
    /// </summary>
    public record AAResupplyEvent(
        AAResupplyResult Result,
        int Rounds = 0,
        int Needed = 0,
        long Cost = 0
    ) : IGameEvent;

    // Infrastructure

    /// <summary>
    /// Event type for consolidated InfraEvent.
    /// Payload contract:
    /// - GeneratorFire / EquipmentExplosion: BuildingIndex, WearPercent, IsShady
    /// - WinterCrisis: StressPercent
    /// - WinterActivated / WinterEnded: no payload
    /// - BatteryLow / BatteryDepleted / BatteryRecharged: BatteryPercent
    /// - PowerPlantDisaster: BuildingIndex
    /// - ImportLimitReached: StressPercent
    /// - GridStressWarning / GridCollapse / GridRecovery: StressPercent, StressZone
    /// </summary>
    public enum InfraEventType
    {
        GeneratorFire = 0,
        WinterCrisis,
        /// <summary>Winter started. Consumer: InfraNarrativeResolver (no-op case, must exist to prevent warning).</summary>
        WinterActivated,
        WinterEnded,
        BatteryLow,
        BatteryDepleted,
        BatteryRecharged,
        PowerPlantDisaster,
        ImportLimitReached,
        ImportLimitCleared,
        GridStressWarning,
        GridCollapse,
        GridRecovery,
        EquipmentExplosion
    }

    /// <summary>
    /// Consolidated infrastructure event (replaces 13 separate events).
    /// Published by: Various engineering systems
    /// Consumed by: InfraNarrativeResolver
    /// </summary>
    public record InfraEvent(
        InfraEventType Type,
        int BatteryPercent = 0,
        float StressPercent = 0f,
        GridStressZone StressZone = GridStressZone.Normal,
        int BuildingIndex = -1,
        float WearPercent = 0f,
        bool IsShady = false
    ) : IGameEvent;

    // Corruption / Countermeasures - HYBRID events (have logic subscribers)
    public record ExportDeficitEvent(int ExportedMW) : IGameEvent;
    public record InvestigationStartedEvent(string JournalistName, int FineAmount = 0) : IGameEvent;

    /// <summary>
    /// Event type for consolidated CorruptionNarrativeEvent.
    /// Payload contract:
    /// - SuspicionRising: Percent
    /// - InvestigationProgress / InvestigationStopped: Percent, ChargesCount
    /// - PoliceInvestigation / PoliceInvestigationEnded: Percent
    /// - ArticlePublished / Arrest: ChargesCount, Location
    /// - ProtestStarted: Participants, Location
    /// - VIPProtected / VIPBypass / VIPOverridden: Location
    /// </summary>
    public enum CorruptionNarrativeEventType
    {
        SuspicionRising = 0,
        InvestigationProgress,
        InvestigationStopped,
        PoliceInvestigation,
        PoliceInvestigationEnded,
        ArticlePublished,
        Arrest,
        ProtestStarted,
        VIPProtected,
        VIPBypass,
        VIPOverridden  // BUG-PL-020: VIP forced to shed at CRITICAL stress — oligarch angry
    }

    /// <summary>
    /// Consolidated corruption narrative event (replaces 10 separate events).
    /// Published by: CountermeasuresSystem, InvestigationPhaseHandler
    /// Consumed by: CorruptionNarrativeResolver
    /// </summary>
    public record CorruptionNarrativeEvent(
        CorruptionNarrativeEventType Type,
        int Percent = 0,
        int ChargesCount = 0,
        long StolenAmount = 0,
        int Participants = 0,
        string? Location = null
    ) : IGameEvent;

    // Donor / Diplomacy

    /// <summary>
    /// Event type for consolidated DonorEvent.
    /// Payload contract:
    /// - ConferenceCalled / Refused: Trust
    /// - AidPackageReceived: one successful donor package
    /// - FundsReceived: Amount
    /// - GeneratorsReceived: Count, MWEach
    /// - PatriotReceived / PatriotExpired: Count, Days
    /// - SanctionsApplied / SanctionsExpired: Days, Penalty
    /// - Scandal: Message
    /// </summary>
    public enum DonorEventType
    {
        ConferenceCalled = 0,
        FundsReceived,
        GeneratorsReceived,
        PatriotReceived,
        Refused,
        PatriotExpired,
        SanctionsApplied,
        SanctionsExpired,
        Scandal,
        AidPackageReceived
    }

    /// <summary>
    /// Consolidated donor/diplomacy event (replaces 9 separate events).
    /// Published by: DonorConferenceSystem
    /// Consumed by: DonorNarrativeResolver
    /// </summary>
    public record DonorEvent(
        DonorEventType Type,
        TrustLevel Trust = TrustLevel.Full,
        // FIX S6-06: long to match CityBudgetService.AddFunds pipeline
        long Amount = 0,
        int Count = 0,
        int MWEach = 0,
        int Days = 0,
        float Penalty = 0f,
        string? Message = null
    ) : IGameEvent;

    // Blackout
    public record BlackoutStartedEvent(int DistrictIndex) : IGameEvent;
    public record BlackoutEndedEvent(int DistrictIndex) : IGameEvent;

    /// <summary>
    /// FIX S7-1: Published when a district's blackout exceeds the configured threshold (default 4h).
    /// Subscribers: SpotterSpawnSystem (OSINT spotter spawn chance).
    /// </summary>
    public record LongBlackoutEvent(int DistrictIndex) : IGameEvent;

    /// <summary>
    /// FIX S7-1: Published when a VIP district has power while non-VIP districts are blacked out.
    /// Subscribers: SpotterSpawnSystem (OSINT spotter spawn chance).
    /// </summary>
#pragma warning disable S101 // VIP is a standard acronym
    public record VIPVisibleDuringBlackoutEvent(int DistrictIndex) : IGameEvent;
#pragma warning restore S101

    // Air Defense VFX
    /// <summary>
    /// Published when an AA installation fires at a threat (hit or miss).
    /// Consumed by: TracerSpawnSystem (visual tracer rounds), InterceptorSpawnSystem (Patriot missile).
    /// ThreatIndex/ThreatVersion identify the engaged threat entity (Axiom 11 ref) so a visual
    /// interceptor missile can chase the live target; default 0 when no threat entity is in scope
    /// (e.g. dev-tools). The event never arbitrates the intercept outcome — that stays with the
    /// fire-control formula (AALogic.CalculateInterceptChance + SerializableRandom).
    /// </summary>
    public record AAFireEvent(float3 AAPosition, float3 ThreatPosition, AAType AAType, int AAEntityIndex, int ThreatIndex = 0, int ThreatVersion = 0, bool IsBallistic = false) : IGameEvent;

    // Balance Telemetry
    /// <summary>
    /// Published when wave is about to start (Alert phase begins).
    /// TelemetryService listens to capture player state snapshot.
    /// </summary>
    public record WaveStartingEvent(int WaveNumber, int ThreatsExpected, WaveRole WaveRole = WaveRole.Regular) : IGameEvent;

    // Shadow/Procurement - consolidated

    /// <summary>
    /// Event type for consolidated ShadowNarrativeEvent.
    /// Payload contract:
    /// - Procurement: DistrictIndex, BuildingCount, Cost, IsCorrupt, KickbackAmount
    /// - CounterfeitFire: BuildingIndex, BuildingName, DistrictIndex
    /// - ContractSigned: ContractType, Cost
    /// - ImportDiscovered / ImportSanctionsLifted: SanctionDays
    /// - WalletFrozen / WalletUnfrozen / WalletConfiscated: Cost
    /// - ProcurementFailed: AttentionIncrease, TrustDecrease
    /// </summary>
    public enum ShadowNarrativeEventType
    {
        Procurement = 0,
        CounterfeitFire,
        ContractSigned,
        ImportDiscovered,
        ImportSanctionsLifted,
        // FIX T17-06..08: Wallet operations telemetry
        WalletFrozen,
        WalletUnfrozen,
        WalletConfiscated,
        ProcurementFailed
    }

    /// <summary>
    /// Consolidated shadow/procurement narrative event (replaces 5 separate events).
    /// Published by: DistrictModernizationSystem, CounterfeitBatteryFireSystem, ContractResponseSystem, ShadowTradeDailySystem, ShadowWalletSystem
    /// Consumed by: ShadowNarrativeResolver, TelemetryEventListener
    /// </summary>
    public record ShadowNarrativeEvent(
        ShadowNarrativeEventType Type,
        int DistrictIndex = 0,
        int BuildingCount = 0,
        long Cost = 0,
        bool IsCorrupt = false,
        int KickbackAmount = 0,
        int BuildingIndex = -1,
        string? ContractType = null,
        int SanctionDays = 0,
        float AttentionIncrease = 0f,
        float TrustDecrease = 0f,
        string? BuildingName = null
    ) : IGameEvent;

    /// <summary>
    /// Published by ShadowWalletSystem after a keyed shadow income request is applied.
    /// </summary>
    public record ShadowIncomeAppliedEvent(string OperationKey, long Amount, string Reason) : IGameEvent;

    /// <summary>
    /// Published after BudgetResolutionSystem applies a durable debt billing request.
    /// </summary>
    public record DebtPaymentAppliedEvent(int BillingDay) : IGameEvent;

}
