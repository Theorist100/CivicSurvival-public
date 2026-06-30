namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Snapshot of a damaged civilian building for UI serialization.
    /// Built by CivilianDamageSystem, consumed by PowerGridUISystem.
    ///
    /// No string Name — name resolution happens in PowerGridUISystem
    /// via vanilla NameSystem.GetRenderedLabelName(buildingEntity).
    /// </summary>
    public struct CivilianBuildingDamageDto
    {
        /// <summary>Vanilla building entity identity for UI repair triggers.</summary>
        public EntityRef Building;
        /// <summary>Current hit count.</summary>
        public int HitCount;
        /// <summary>Hits required to destroy (from GetHitsToDestroy).</summary>
        public int MaxHits;
        /// <summary>HitCount / MaxHits (for progress bar).</summary>
        public float DamagePercent;
        /// <summary>True if currently under repair.</summary>
        public bool IsRepairing;
        /// <summary>Hours remaining until repair completes (0 if not repairing).</summary>
        public float RepairHoursLeft;
        /// <summary>Which repair lane (0=Municipal, 1=MunicipalWithKickback, 2=ShadowOps).</summary>
        public byte RepairTypeByte;
    }
}
