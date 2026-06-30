using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// IEnableableComponent allows enable/disable without structural changes.
    /// When enabled = building is currently in blackout state.
    ///
    /// NOT ISerializable: data is ephemeral.
    /// Vanilla restores WantedConsumption on load; BlackoutSystem re-captures on next frame.
    ///
    /// Writer: BlackoutStateSetupSystem (added once, eligibility flags refreshed)
    /// Readers: BlackoutJob, BackupPowerJob, Narrative, UI, DistrictPenaltySystem
    /// </summary>
    public struct BlackoutState : IComponentData, IEnableableComponent
    {
        /// <summary>
        /// True if building has unconditional blackout exemption (fire station, police, prison, etc.).
        /// Refreshed by BlackoutStateSetupSystem because vanilla building aspects can
        /// change after this component has been added.
        /// BlackoutJob skips these buildings entirely when ProtectCriticalInfra=true.
        /// </summary>
        public bool IsCritical;

        /// <summary>
        /// True if building qualifies for backup power discharge under CriticalOnly policy
        /// (hospital, school, fire station, water pumping station).
        /// Refreshed by BlackoutStateSetupSystem because vanilla building aspects can
        /// change after this component has been added.
        /// Distinct from IsCritical: hospital has battery priority but no unconditional exemption.
        /// </summary>
        public bool HasBatteryPriority;

        /// <summary>
        /// True for the current blackout pass when this building would be blacked
        /// out by schedule/category rules but is explicitly supplied by backup.
        /// Ephemeral runtime signal; BackupPowerJob drains reserve from this
        /// instead of inferring backup service from FulfilledConsumption.
        /// </summary>
        public bool ServedByBackup;
    }
}
