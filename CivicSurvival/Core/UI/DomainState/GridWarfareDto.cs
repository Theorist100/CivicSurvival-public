using System.Collections.Generic;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Grid warfare domain DTO.
    /// Pushed by GridWarfareUISystem every 500ms via ThrottledUISystemBase.
    /// OperationSlots stays Raw (producer-built JSON array of OperationSlotDto
    /// entries); AttackCosts is a typed Dict that the generated writer
    /// expands into a JSON object keyed by attack type.
    /// </summary>
    public partial struct GridWarfareDto : IDomainDto
    {
        // Shadow wallet (canonical source; also in PowerGridDto for PlantsContent — see F24-M1)
        public int ShadowBalance;
        public int ShadowLocked;
        public int ShadowTotal;

        // Enemy state — three mirror axes (physical/digital/social; the enemy's defence is the
        // mirror city's positional AA, published on the MirrorCity snapshot instead of a scalar
        // here). Axis precision is intel-gated in the producer: Lv0 carries only a 25%-quantized
        // reading (exact health never leaves the sim — a PvP-safe seam), Lv1+ carries exact values.
        public float EnemyPhysicalAxis;
        public float EnemyDigitalAxis;
        public float EnemySocialAxis;

        // EFFECTIVE intel level (0-2, insider folded to 2) the producer used to gate this readout.
        // The UI reads it to pick the coarse (Lv0) vs exact (Lv1+) axis presentation and to reveal
        // the full-intel-only rows; the same value gates the MirrorCity snapshot, so every War Room
        // surface agrees.
        public int IntelLevel;

        // Per-axis strike-target preference (the last War Room pick, EnemyTarget.NoTargetId = 65535
        // when none). Readout of PlayerAttackSystem's preference so a re-opened War Room can restore
        // the highlight: the backend preference survives the overlay unmount, and an armed-but-
        // invisible preference would silently aim the next Execute at a target the UI no longer shows.
        public int PreferredTargetPhysical;
        public int PreferredTargetDigital;
        public int PreferredTargetSocial;

        // Aggregate enemy "health" 0..1 (mean of the three axes / cap) — the single seam where
        // axis health feeds wave strength (EnemyState.AggregatePressure01). Drives the War Room
        // enemy strip's AGGREGATE readout and the NEXT WAVE SCALE multiplier. Full-intel only:
        // -1 sentinel hides both rows below max intel.
        public float EnemyAggregatePressure01;

        // Respite ("enemy regroups") — per-axis flag that the axis is in its post-floor lull,
        // during which waves of that type weaken. Drives the "Suppressed" badge on each axis bar.
        // The badge (the fact of respite) shows at every level.
        public bool RespitePhysicalActive;
        public bool RespiteDigitalActive;
        public bool RespiteSocialActive;

        // Respite countdown per axis in game-hours — the timing behind the badge, full-intel only.
        // -1 sentinel when the axis is not in respite or intel is below max (the UI hides the timer).
        public float RespitePhysicalHoursLeft;
        public float RespiteDigitalHoursLeft;
        public float RespiteSocialHoursLeft;

        // Act-objective progress toward an enemy-beachhead collapse: 1 = all three axes are at or
        // below the objective threshold (loot triggers), 0 = at least one axis is at full health.
        public float ObjectiveProgress;

        // Counter-attack arsenal stock (units in hand per munition kind). Sourced from
        // ICounterAttackArsenalService.StockOf; 0 when the arsenal is unavailable.
        public int DroneStock;
        public int BallisticStock;

        // Number of built drone launchers (live DroneLauncherInstallation entities). Gates the
        // War Room EXECUTE button for the drone operation: 0 means outbound drones have no launch
        // origin, so the strike cannot fire (arsenal purchase is not blocked).
        public int DroneLauncherCount;

        // Shadow price of one drone launcher (GridWarfare.DroneLauncherCost, pre-markup) — the
        // STRIKE build card shows the purse the placement handler will actually charge.
        public int DroneLauncherCost;

        // City stability
        public float CityStability;
        public float StabilityDiscount;

        // Pre-serialized JSON array of OperationSlotDto entries.
        public string OperationSlotsJson;
        // Pre-serialized JSON array of ResolvedStrikeDto entries (last 8 outbound-strike
        // outcomes, newest first) — feeds the War Room STRIKE OUTCOMES ledger and C2 bus.
        public string ResolvedStrikesJson;
        // Typed map of attack-type → final cost; generated writer emits as JSON object.
        public IReadOnlyDictionary<string, long>? AttackCosts;
        public string OperationRequestJson;
        // Terminal placement-request result for the "build drone launcher" tool (mirrors the AA /
        // Center placement request lifecycle). Falls back to the RequestResultBridge snapshot in
        // the generated writer when the producer leaves it null.
        public string DroneLauncherPlacementRequestJson;

        // Prepare eligibility mirrors PlayerAttackSystem request validation.
        [Attributes.DtoEligibility(typeof(GridOperationEligibility), nameof(GridOperationEligibility.CanPrepareDrone), "PrepareDroneLockedReasonId")]
        public bool CanPrepareDrone;
        [Attributes.DtoEligibility(typeof(GridOperationEligibility), nameof(GridOperationEligibility.CanPrepareBlackout), "PrepareBlackoutLockedReasonId")]
        public bool CanPrepareBlackout;
        [Attributes.DtoEligibility(typeof(GridOperationEligibility), nameof(GridOperationEligibility.CanPrepareDisinfo), "PrepareDisinfoLockedReasonId")]
        public bool CanPrepareDisinfo;

        // Unlock state
        public bool GridWarfareUnlocked;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
