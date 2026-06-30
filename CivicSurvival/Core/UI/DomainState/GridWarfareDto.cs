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

        // Enemy state — three mirror axes (physical/digital/social) + intercept defence.
        public float EnemyPhysicalAxis;
        public float EnemyDigitalAxis;
        public float EnemySocialAxis;
        public float EnemyInterceptChance;

        // Respite ("enemy regroups") — per-axis flag that the axis is in its post-floor lull,
        // during which waves of that type weaken. Drives the "Suppressed" badge on each axis bar.
        public bool RespitePhysicalActive;
        public bool RespiteDigitalActive;
        public bool RespiteSocialActive;

        // Act-objective progress toward an enemy-beachhead collapse: 1 = all three axes are at or
        // below the objective threshold (loot triggers), 0 = at least one axis is at full health.
        public float ObjectiveProgress;

        // Counter-attack arsenal stock (units in hand per munition kind). Sourced from
        // ICounterAttackArsenalService.StockOf; 0 when the arsenal is unavailable.
        public int DroneStock;
        public int BallisticStock;

        // City stability
        public float CityStability;
        public float StabilityDiscount;

        // Pre-serialized JSON array of OperationSlotDto entries.
        public string OperationSlotsJson;
        // Typed map of attack-type → final cost; generated writer emits as JSON object.
        public IReadOnlyDictionary<string, long>? AttackCosts;
        public string OperationRequestJson;

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
