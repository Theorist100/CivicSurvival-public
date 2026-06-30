using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public enum CatchUpPolicy
    {
        EachDay = 0,
        BoundedAggregate = 1
    }

    /// <summary>
    /// Published resident-household selection. The snapshot BORROWS its backing pair of
    /// lists from the producer's selection ring (<c>ResidentPopulationModelSystem</c>) —
    /// it does not own memory and is deliberately NOT <c>System.IDisposable</c>: a
    /// consumer-side <c>using</c>/<c>Dispose()</c> on a struct copy would otherwise
    /// silently return the producer's live pair to the ring, and a hazard that only a
    /// convention protects against WILL eventually be triggered by a future consumer.
    /// Lifetime contract: the borrowed pair stays untouched while this snapshot or its
    /// predecessor is the published one (the ring keeps the previous snapshot's slot
    /// borrowed for one full publish cycle); consumers read through
    /// <c>IVersionedView.Observe</c> within their own update and must not cache the
    /// struct across frames.
    /// </summary>
    public readonly struct ResidentHouseholdSnapshot
    {
        // Slot index + 1 so default(ResidentHouseholdSnapshot) means "borrows nothing"
        // (SelectionSlot == -1) instead of silently aliasing ring slot 0.
        private readonly int m_SelectionSlotPlusOne;

        public ResidentHouseholdSnapshot(
            int version,
            NativeArray<Entity>.ReadOnly eligibleHouseholds,
            NativeArray<int>.ReadOnly liveCitizensPerHousehold,
            int pendingDayChanges,
            CatchUpPolicy catchUp,
            int selectionSlot)
        {
            Version = version;
            EligibleHouseholds = eligibleHouseholds;
            LiveCitizensPerHousehold = liveCitizensPerHousehold;
            PendingDayChanges = pendingDayChanges;
            CatchUp = catchUp;
            m_SelectionSlotPlusOne = selectionSlot + 1;
        }

        public int Version { get; }
        public NativeArray<Entity>.ReadOnly EligibleHouseholds { get; }
        public NativeArray<int>.ReadOnly LiveCitizensPerHousehold { get; }
        public int PendingDayChanges { get; }
        public CatchUpPolicy CatchUp { get; }

        /// <summary>
        /// Ring bookkeeping for the producer's identity-checked slot return; -1 when the
        /// snapshot borrows nothing (boot/empty/restored-scalar publishes). Internal so
        /// no consumer can manufacture a snapshot that aliases a ring slot.
        /// </summary>
        internal int SelectionSlot => m_SelectionSlotPlusOne - 1;
    }
}
