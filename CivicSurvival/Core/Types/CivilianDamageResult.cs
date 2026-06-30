namespace CivicSurvival.Core.Types
{
    public struct CivilianDamageResult
    {
        /// <summary>True if hit was recorded (building valid, system enabled).</summary>
        public bool IsValid;
        /// <summary>True if building exceeded hit threshold and should be destroyed.</summary>
        public bool ShouldDestroy;
        /// <summary>Current hit count after this hit.</summary>
        public int HitCount;
        /// <summary>Hits required to destroy this building.</summary>
        public int HitsToDestroy;
    }
}
