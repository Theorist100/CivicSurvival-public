using System;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure crew-commitment transition math shared by the manpower pool owner
    /// (Mobilization) and the launcher owner (AirDefense). The committed-crew
    /// transition spans both domains — Mobilization owns the pool, AirDefense owns
    /// the launcher lifecycle — so neither can own it without a Domain→Domain
    /// import (Axiom 5); Core is the only legal home, next to
    /// <see cref="ManpowerLogic"/> / <see cref="AALogic"/>.
    ///
    /// These are pure functions over blittable int/float: they RETURN the new
    /// committed value or the casualty split, and the CALLER applies it. That keeps
    /// the single-writer of each store intact — <c>MobilizationSystem.m_UsedManpower</c>
    /// stays the sole authoritative committed accumulator; CrewMath never mutates
    /// anything. Blittable + side-effect-free so the same transition is reusable by a
    /// future Burst job and a C# server recompute (forecast/server parity).
    /// </summary>
    public static class CrewMath
    {
        /// <summary>Manpower free for new commitments: <c>max(0, totalPool - committed)</c>.</summary>
        public static int Available(int totalPool, int committed)
            => Math.Max(0, totalPool - committed);

        /// <summary>
        /// Commit-on-place gate: can the pool afford one launcher's crew now?
        /// <c>crewRequired &lt;= Available(totalPool, committed)</c>.
        /// </summary>
        public static bool CanCommit(int totalPool, int committed, int crewRequired)
            => crewRequired <= Available(totalPool, committed);

        /// <summary>
        /// New committed total after a commitment of <paramref name="crewRequired"/>.
        /// The caller is responsible for gating with <see cref="CanCommit"/> first.
        /// Current callers pass non-negative values so the clamp is inert, but the
        /// <c>max(0, …)</c> floor preserves the runtime's original contract (the old
        /// <c>m_UsedManpower = Math.Max(0, m_UsedManpower + amount)</c>): the committed
        /// accumulator never goes negative even if a future caller passes a negative delta.
        /// </summary>
        public static int Commit(int committed, int crewRequired)
            => Math.Max(0, committed + crewRequired);

        /// <summary>
        /// Casualty split when a launcher's crew is released by destruction:
        /// <c>survivors = round(crewAssigned · survivalRate)</c> (away-from-zero, the
        /// runtime's rounding), <c>casualties = crewAssigned - survivors</c>.
        ///
        /// Defensive: <paramref name="survivalRate"/> is clamped to [0,1] before the
        /// split. <c>CasualtySurvivalRate</c> is the one unit-fraction config that is not
        /// ClampUnit-normalized, and it arrives from the server (RemoteBalanceConfig), so
        /// a pushed rate outside [0,1] would otherwise make survivors exceed crew (over-
        /// release of manpower) or go negative (crew silently vanishing). Behaviour only
        /// changes when the rate is already invalid; for any rate in [0,1] this is a no-op.
        /// </summary>
        public static (int survivors, int casualties) ReleaseSplit(int crewAssigned, float survivalRate)
        {
            float rate = Math.Clamp(survivalRate, 0f, 1f);
            int survivors = (int)Math.Round(crewAssigned * rate, MidpointRounding.AwayFromZero);
            int casualties = crewAssigned - survivors;
            return (survivors, casualties);
        }

        /// <summary>New committed total after releasing <paramref name="releasedCount"/>:
        /// <c>max(0, committed - releasedCount)</c>.</summary>
        public static int Release(int committed, int releasedCount)
            => Math.Max(0, committed - releasedCount);
    }
}
