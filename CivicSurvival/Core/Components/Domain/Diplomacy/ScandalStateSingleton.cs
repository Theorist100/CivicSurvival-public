using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Diplomacy
{
    /// <summary>
    /// Read-only scandal state as ECS singleton.
    /// Updated by ScandalSystem each day.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ScandalStateSingleton&gt;()
    ///
    /// Writer: ScandalSystem (updates on DayChangedEvent)
    /// Readers: DonorConferenceSystem
    /// </summary>
    public struct ScandalStateSingleton : IComponentData
    {
        /// <summary>
        /// Accumulated trust penalty from international scandals (0-80).
        /// Reduces effective Trust in donor calculations.
        /// </summary>
        public float ScandalPenalty;

        /// <summary>
        /// Game day when last scandal occurred.
        /// </summary>
        public int LastScandalDay;

        /// <summary>Default state.</summary>
        public static ScandalStateSingleton Default => new()
        {
            ScandalPenalty = 0f,
            LastScandalDay = 0
        };
    }
}
