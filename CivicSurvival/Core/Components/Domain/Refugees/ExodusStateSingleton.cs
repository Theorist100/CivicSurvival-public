using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Refugees
{
    /// <summary>
    /// Read-only exodus state as ECS singleton.
    /// Updated by ExodusSystem each day.
    /// TotalExodus mirrors ExodusSystem's persisted session counter and is rebuilt after load.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ExodusStateSingleton&gt;()
    ///
    /// Writer: ExodusSystem (updates on DayChangedEvent)
    /// Readers: AttentionUIPanel
    /// </summary>
    public struct ExodusStateSingleton : IComponentData
    {
        /// <summary>Base resolved exodus rate before die-hard/eligibility dampers (% per day).</summary>
        public float BaseRatePercentPerDay;

        /// <summary>Actual player-visible rate used by the latest exodus simulation (% per day).</summary>
        public float EffectiveRatePercentPerDay;

        /// <summary>Compatibility alias for player-facing UI. Semantics: effective rate.</summary>
        public float ExodusRatePercentPerDay;

        /// <summary>Phase 14C — the resolved base rate attributed to the kinetic (WorldShock) cause
        /// (% per day). Kinetic + Psy sum to <see cref="BaseRatePercentPerDay"/>.</summary>
        public float KineticRatePercentPerDay;

        /// <summary>Phase 14C — the resolved base rate attributed to the psy-war (Poor sway) cause
        /// (% per day). A non-zero value at zero WorldShock is the info-war driving exodus alone.</summary>
        public float PsyRatePercentPerDay;

        /// <summary>Total population that has left.</summary>
        public int TotalExodus;

        /// <summary>Whether exodus is currently active.</summary>
        public bool IsExodusActive;

        /// <summary>Phase 16 "Batalion Monako" — cumulative count of swayed Wealthy families that
        /// have emigrated as the targeted capital-flight stream. Transient mirror of the persisted
        /// counter, rebuilt after load.</summary>
        public int MonacoFamiliesFled;

        /// <summary>Phase 16 "Batalion Monako" — cumulative household wealth (currency) that walked
        /// out with the fled Wealthy families. Informational readout only (vanilla emigration already
        /// removes the wallet/tax base); a long because a few thousand wallets can exceed int.</summary>
        public long MonacoCapitalFled;

        /// <summary>Default state.</summary>
        public static ExodusStateSingleton Default => new()
        {
            BaseRatePercentPerDay = 0f,
            EffectiveRatePercentPerDay = 0f,
            ExodusRatePercentPerDay = 0f,
            KineticRatePercentPerDay = 0f,
            PsyRatePercentPerDay = 0f,
            TotalExodus = 0,
            IsExodusActive = false,
            MonacoFamiliesFled = 0,
            MonacoCapitalFled = 0L
        };
    }
}
