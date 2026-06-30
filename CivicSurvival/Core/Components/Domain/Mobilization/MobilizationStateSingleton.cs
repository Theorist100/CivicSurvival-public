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

        /// <summary>Happiness impact (0.5-1.0)</summary>
        public float MoraleFactor;

        /// <summary>War fatigue factor (0.85 or 1.0)</summary>
        public float FatigueFactor;

        /// <summary>Conscription bonus (0.0 or 0.5)</summary>
        public float ConscriptionBonus;

        // ===== State =====

        /// <summary>Whether conscription is active (+50% manpower, -10 reputation)</summary>
        public bool IsConscriptionActive;

        /// <summary>War fatigue threshold reached</summary>
        public bool IsWarFatigued;

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
            ConscriptionBonus = 0f,
            IsConscriptionActive = false,
            IsWarFatigued = false,
            WarDay = -1,
            CallToArmsCooldownEndHour = 0f,
            IsCallToArmsOnCooldown = false,
            ConscriptionCooldownEndHour = 0f,
            IsConscriptionReactivationOnCooldown = false,
            PredictedConscriptionRelease = 0,
            SocialPenaltyProducerReady = true
        };
    }
}
