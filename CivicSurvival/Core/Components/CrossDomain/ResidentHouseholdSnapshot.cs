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
            int aliveCitizensInSelection,
            int pendingDayChanges,
            CatchUpPolicy catchUp,
            int selectionSlot)
        {
            Version = version;
            EligibleHouseholds = eligibleHouseholds;
            LiveCitizensPerHousehold = liveCitizensPerHousehold;
            AliveCitizensInSelection = aliveCitizensInSelection;
            PendingDayChanges = pendingDayChanges;
            CatchUp = catchUp;
            m_SelectionSlotPlusOne = selectionSlot + 1;
        }

        public int Version { get; }
        public NativeArray<Entity>.ReadOnly EligibleHouseholds { get; }
        public NativeArray<int>.ReadOnly LiveCitizensPerHousehold { get; }

        /// <summary>
        /// Sum of <see cref="LiveCitizensPerHousehold"/> over this very selection — the
        /// numerator that belongs to this denominator. Published by the producer from the
        /// SAME job result the borrowed pair was flattened from, so a consumer that divides
        /// one by the other never mixes two measurements of "how many people live here".
        ///
        /// It is not a second count: the producer's chunk job increments the per-household
        /// tally and the citywide total in one iteration of one loop, and households with
        /// zero live citizens are dropped from both, so this is identically the number the
        /// retired scalar (<c>ResidentPopulationSnapshot.AliveResidentCitizens</c>) carried
        /// — moved to where its denominator lives instead of being recomputed.
        ///
        /// Zero on a snapshot that borrows nothing (boot / empty / reset publish),
        /// which is honest: there is no selection to sum. Consumers gate on
        /// <c>IsSelectionReady</c> before dividing, exactly as they do for the arrays above.
        /// Not persisted — it is rebuilt with the selection it belongs to.
        /// </summary>
        public int AliveCitizensInSelection { get; }

        public int PendingDayChanges { get; }
        public CatchUpPolicy CatchUp { get; }

        /// <summary>
        /// Ring bookkeeping for the producer's identity-checked slot return; -1 when the
        /// snapshot borrows nothing (boot/empty/reset publishes). Internal so
        /// no consumer can manufacture a snapshot that aliases a ring slot.
        /// </summary>
        internal int SelectionSlot => m_SelectionSlotPlusOne - 1;
    }
}
