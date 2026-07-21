using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Diplomacy
{
    /// <summary>
    /// Read-only crisis state as ECS singleton.
    /// Updated by CrisisMonitorSystem each frame.
    ///
    /// Access: SystemAPI.GetSingleton&lt;CrisisStateSingleton&gt;()
    ///
    /// Writer: CrisisMonitorSystem (updates each frame)
    /// Readers: DonorConferenceSystem
    /// </summary>
    public struct CrisisStateSingleton : IComponentData
    {
        /// <summary>
        /// Current crisis level (0-100).
        /// Percentage of population in blackout-affected buildings.
        /// </summary>
        public float CrisisLevel;

        /// <summary>Default state.</summary>
        public static CrisisStateSingleton Default => new()
        {
            CrisisLevel = 0f
        };
    }
}
