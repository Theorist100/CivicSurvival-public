using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Core;

using CivicSurvival.Core.Features.Wellbeing;
namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Published when scenario act changes.
    /// All downstream systems should subscribe to this event to react to act transitions.
    ///
    /// Upstream: ScenarioDirectorSystem, CrisisActCoordinator (publishers)
    /// Downstream: RefugeeInfluxCoordinator, CrisisEconomicsSystem, CrisisTutorialSystem (subscribers)
    ///
    /// </summary>
    public readonly struct ActChangedEvent : IGameEvent
    {
        /// <summary>Act before transition.</summary>
        public readonly Act PreviousAct;

        /// <summary>Act after transition.</summary>
        public readonly Act NewAct;

        /// <summary>Game time when transition occurred (from GameTimeSystem.TotalGameHours).</summary>
        public readonly double Timestamp;

        /// <summary>
        /// Absolute game day when crisis started. 0 = fresh transition (receiver calculates).
        /// Non-zero on post-load re-publish from CrisisActCoordinator (S7-08 fix).
        /// </summary>
        public readonly int CrisisStartDay;

        public ActChangedEvent(Act previousAct, Act newAct, double timestamp, int crisisStartDay = 0)
        {
            PreviousAct = previousAct;
            NewAct = newAct;
            Timestamp = timestamp;
            CrisisStartDay = crisisStartDay;
        }
    }

    /// <summary>
    /// Published when pre-war ominous effects activate (Village scenario).
    /// DistrictPenaltySystem subscribes for happiness penalty.
    ///
    /// Published by: OminousSignsSystem
    /// Consumed by: DistrictPenaltySystem (happiness), future systems (loans, commerce)
    /// </summary>
    public record PreWarTensionEvent(PreWarEffect Effect, float Value) : IGameEvent;

    /// <summary>Pre-war effect types published by OminousSignsSystem.</summary>
    public enum PreWarEffect
    {
        HappinessPenalty = 0,
        CommercePenalty,
        LoansDisabled,
        WarStarted  // Clears all pre-war effects
    }

    // Note: IntroCompleteEvent already exists in GameEvents.cs
}
