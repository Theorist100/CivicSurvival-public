using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Mobilization
{
    /// <summary>
    /// Read-only mobilization state as ECS singleton.
    /// Updated by MobilizationSystem each frame.
    ///
    /// Access: SystemAPI.GetSingleton&lt;MobilizationStateSingleton&gt;()
    ///
    /// Writer: MobilizationSystem (updates each frame)
    /// Readers: AACrewAssignmentSystem, MobilizationUIPanel
    ///
    /// Note: For operations (TryRecruit, Release, etc.), use IMobilizationService.
    /// </summary>
    public struct MobilizationStateSingleton : IComponentData
    {
        // ===== Core Values =====

        /// <summary>Available manpower (Total - Used)</summary>
        public int AvailableManpower;

        /// <summary>Currently used manpower</summary>
        public int UsedManpower;

        /// <summary>Total manpower capacity (BasePool x Modifiers)</summary>
        public int TotalManpower;

        /// <summary>Base pool from population (population / 1000)</summary>
        public int BasePool;

        /// <summary>Current casualties (reduces effective manpower)</summary>
        public int Casualties;

        /// <summary>Current population</summary>
        public int Population;

        // ===== Modifiers =====

        /// <summary>Corruption impact (0.5-1.0)</summary>
        public float PatriotismFactor;

        /// <summary>Aggregate corruption the city can see (0-100) behind PatriotismFactor. Carried
        /// so a clean city can be told apart from an aggregate that is not being computed.</summary>
        public float VisibleCorruptionScore;

        /// <summary>Realized share of dispatchable output sold covertly (0-100).</summary>
        public int ExportLoadPercent;

        /// <summary>True when the heritage-crew floor had to lift the pool.</summary>
        public bool IsDefenceFloorEngaged;

        /// <summary>Happiness impact (0.5-1.0)</summary>
        public float MoraleFactor;

        /// <summary>War fatigue factor (0.85 or 1.0)</summary>
        public float FatigueFactor;

        /// <summary>Draft-evasion factor (1 − DodgerMaxPenalty .. 1.0)</summary>
        public float DodgerFactor;

        /// <summary>Heads lost to draft evasion ("ухилянти") under enemy Propaganda pressure.</summary>
        public int DodgerCount;

        /// <summary>Heads removed by the sold-deferral rate (Draft-Exemption scheme, reversible).</summary>
        public int DeferralManpower;

        /// <summary>Heads permanently sold off as fake disability (Draft-Exemption scheme).</summary>
        public int DisabilityManpower;

        /// <summary>Conscription bonus (0.0 or 0.5)</summary>
        public float ConscriptionBonus;

        // ===== State =====

        /// <summary>Whether conscription is active (+50% manpower, -10 reputation)</summary>
        public bool IsConscriptionActive;

        /// <summary>War fatigue threshold reached</summary>
        public bool IsWarFatigued;

        /// <summary>
        /// Latched critical-manpower verdict, hysteresis included (enter 20%, clear 25%).
        /// Mirrors the persisted latch the alert edge fires on, so the UI badge and the toast
        /// can never disagree — do NOT re-derive it in the panel from Available/Total, that
        /// would flicker the badge off inside the recovery band while the alert is still armed.
        /// </summary>
        public bool IsManpowerCritical;

        /// <summary>Current war day (-1 if not started)</summary>
        public int WarDay;

        /// <summary>Game-hour when Call to Arms becomes available again.</summary>
        public float CallToArmsCooldownEndHour;

        /// <summary>Whether Call to Arms is currently on cooldown.</summary>
        public bool IsCallToArmsOnCooldown;

        /// <summary>Game-hour when conscription can be re-activated.</summary>
        public float ConscriptionCooldownEndHour;

        /// <summary>Whether conscription re-activation is currently on cooldown.</summary>
        public bool IsConscriptionReactivationOnCooldown;

        /// <summary>Crew that DeactivateConscription would force-release right now (0 if inactive or no excess).</summary>
        public int PredictedConscriptionRelease;

        /// <summary>Whether the Wellbeing penalty producer is available.</summary>
        public bool SocialPenaltyProducerReady;

        /// <summary>
        /// Whether every number above came out of a rebuild on a city that answered. False means
        /// the population owner refused and the breakdown is frozen at an earlier reading: the
        /// figures are the last good ones, not the current ones.
        ///
        /// This is the display-side half of the gate inside MobilizationSystem.UpdateBreakdown.
        /// Without it the mirror carried no way to say "stale", so a reader could not tell a pool
        /// that was measured at zero from a pool nobody has measured — and every panel resolved
        /// that ambiguity the same wrong way, by printing the number.
        ///
        /// Deliberately false in <see cref="Default"/>: a reader that falls back to the default
        /// because the singleton is absent has, by definition, measured nothing.
        /// </summary>
        public bool IsPoolMeasured;

        /// <summary>Default state.</summary>
        public static MobilizationStateSingleton Default => new()
        {
            AvailableManpower = 0,
            UsedManpower = 0,
            TotalManpower = 0,
            BasePool = 0,
            Casualties = 0,
            Population = 0,
            PatriotismFactor = 1f,
            MoraleFactor = 1f,
            FatigueFactor = 1f,
            DodgerFactor = 1f,
            DodgerCount = 0,
            DeferralManpower = 0,
            DisabilityManpower = 0,
            ConscriptionBonus = 0f,
            IsConscriptionActive = false,
            IsWarFatigued = false,
            IsManpowerCritical = false,
            WarDay = -1,
            CallToArmsCooldownEndHour = 0f,
            IsCallToArmsOnCooldown = false,
            ConscriptionCooldownEndHour = 0f,
            IsConscriptionReactivationOnCooldown = false,
            PredictedConscriptionRelease = 0,
            SocialPenaltyProducerReady = true,
            IsPoolMeasured = false
        };
    }
}
