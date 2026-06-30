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

        /// <summary>Total population that has left.</summary>
        public int TotalExodus;

        /// <summary>Whether exodus is currently active.</summary>
        public bool IsExodusActive;

        /// <summary>Default state.</summary>
        public static ExodusStateSingleton Default => new()
        {
            BaseRatePercentPerDay = 0f,
            EffectiveRatePercentPerDay = 0f,
            ExodusRatePercentPerDay = 0f,
            TotalExodus = 0,
            IsExodusActive = false
        };
    }
}
