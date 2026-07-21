using Unity.Entities;

namespace CivicSurvival.Core.Components.PsyImpact
{
    /// <summary>
    /// Custom SharedComponent for round-robin slot assignment on HouseholdPsyState mod entities.
    /// 4 slots (0-3): MHR processes one slot per fire cycle (sim-tick based).
    /// Full cycle = 4 × SIM_TICK_INTERVAL ticks (~0.75-1s game time, FPS-independent).
    ///
    /// Cannot use vanilla UpdateFrame — UpdateGroupSystem.GetGroupSizeArray() only recognizes
    /// vanilla component types. Mod entities would get default(NativeArray) and log error.
    ///
    /// ISharedComponentData: entities are sorted into chunk groups by SlotIndex value.
    /// 172K / 4 = ~43K per group = ~344 chunks. Healthy density.
    /// </summary>
    public struct PsySlot : ISharedComponentData, System.IEquatable<PsySlot>
    {
        public int SlotIndex; // 0-3

        /// <summary>
        /// Current active slot (set by MHR, read by WRS/PsyTransientReset).
        /// Lives in Core so Core systems can read without Domain dependency.
        /// </summary>
        public static int CurrentSlot { get; set; }

        public bool Equals(PsySlot other) => SlotIndex == other.SlotIndex;
        public override bool Equals(object obj) => obj is PsySlot other && Equals(other);
        public override int GetHashCode() => SlotIndex;
        public static bool operator ==(PsySlot left, PsySlot right) => left.Equals(right);
        public static bool operator !=(PsySlot left, PsySlot right) => !left.Equals(right);
    }
}
