using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Singleton: UI-facing backup power stats.
    /// Written by: BackupPowerRuntimeSystem
    /// Read by: BackupPowerUIPanel
    ///
    /// Note: HasBackupPowerAvailable() method stays in IBackupPowerService
    /// (used by BlackoutSystem for per-entity checks).
    /// </summary>
    public struct BackupPowerStateSingleton : IComponentData
    {
        /// <summary>Total charge percent across all batteries (0-100).</summary>
        public float ChargePercent;

        /// <summary>Number of buildings with backup power.</summary>
        public int ProtectedBuildings;

        /// <summary>Total battery capacity in kWh.</summary>
        public int TotalCapacityKWh;

        /// <summary>Number of units currently discharging.</summary>
        public int DischargingCount;

        /// <summary>
        /// Discharge policy: how batteries are used during blackouts.
        /// See: BackupPolicy enum, BACKUP_POLICY_PLAN.md
        /// </summary>
        public BackupPolicy Policy;

        /// <summary>Hospitals with charged battery / total hospitals with battery.</summary>
        public int HospitalsPowered;
        public int HospitalsTotal;

        /// <summary>Schools with charged battery / total schools with battery.</summary>
        public int SchoolsPowered;
        public int SchoolsTotal;

        public static BackupPowerStateSingleton Default => new()
        {
            ChargePercent = 0f,
            ProtectedBuildings = 0,
            TotalCapacityKWh = 0,
            DischargingCount = 0,
            Policy = BackupPolicy.Reserve,
            HospitalsPowered = 0,
            HospitalsTotal = 0,
            SchoolsPowered = 0,
            SchoolsTotal = 0
        };

        public void SetDefaults() => this = Default;

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
