using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Domain state DTO for the in-game crisis sweep (CrisisSweepUISystem). Mirrors
    /// <see cref="CivicSurvival.Core.Components.Diagnostics.CrisisSweepResultSingleton"/> — the UI
    /// reads the latest sweep verdict via the <c>CrisisSweepState</c> binding. Field declarations
    /// live here; the <c>WriteTo</c> serializer is generated into the matching partial in
    /// <c>DomainDtoWriters.g.cs</c> from <c>ui-dto.contract.yaml</c>.
    ///
    /// Mode discriminates which block is meaningful: 0 = Invariant (recoverability / grace /
    /// intercept / grant / opAA), 1 = Pacing (calm / wave pressure), 2 = Severity (blackout
    /// probability / median collapse day / unsheddable floor). <c>HasResult</c> is false until a
    /// sweep runs (and after load — the result singleton is transient, not saved).
    /// </summary>
    public partial struct CrisisSweepDto : IDomainDto
    {
        // ===== Meta (always written) =====
        public byte Mode;
        public bool HasResult;
        public double ComputedAtGameHours;
        public int ArchetypeId;
        public int PopulationPeak;
        public int WarDay;

        // ===== Invariant mode (Mode == 0) =====
        public float WorstCaseRecoveryBallisticOnly;
        public float WorstCaseRecoveryMixed;
        public bool IsRecoverableBallisticOnly;
        public bool IsRecoverableMixed;
        public float GraceWindowHours;
        public float DroneInterceptBallisticOnly;
        public float DroneInterceptMixed;
        public int FreeHeritageGrant;
        public int OperationalAaAtVerdict;
        public int ManpowerTotal;
        public int ManpowerUsed;
        public int ManpowerCasualties;
        public int ManpowerAvailable;
        public int AaHeritage;
        public int AaBofors;
        public int AaGepard;
        public int AaPatriot;
        public float CoveragePct;
        public float AreaKm2;
        public float BallisticInterceptBallisticOnly;
        public float BallisticInterceptMixed;
        public int BallisticTargets;
        public int MissilesSpentOnDrones;
        public bool PatriotInterceptsDrones;

        // ===== Pacing mode (Mode == 1) =====
        public float CalmHours;
        public float WavePressureAtPeak;

        // ===== Severity mode (Mode == 2) =====
        public int SampleCount;
        public float BlackoutProbabilityPct;
        public int MedianCollapseDay;
        public int UnsheddableFloorMW;
        public int RepairSlots;
        public long RepairFundingCash;
        public byte RepairTier;
        public bool RepairBudgetLive;
    }
}
