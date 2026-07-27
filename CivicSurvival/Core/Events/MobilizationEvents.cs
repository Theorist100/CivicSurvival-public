using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Published when available manpower drops below 20%.
    /// </summary>
    public record ManpowerCriticalEvent(int Available, int Total, float Percent) : IGameEvent;

    /// <summary>
    /// Published when conscription is activated.
    /// </summary>
    public record ConscriptionActivatedEvent() : IGameEvent;

    /// <summary>
    /// Published when conscription is deactivated.
    /// </summary>
    public record ConscriptionDeactivatedEvent() : IGameEvent;

    /// <summary>
    /// Published when manpower is recruited.
    /// </summary>
    public record ManpowerRecruitedEvent(int Amount, string Reason, int Remaining) : IGameEvent;

    /// <summary>
    /// Published when manpower is released.
    /// </summary>
    public record ManpowerReleasedEvent(int Amount, string Reason, int Available) : IGameEvent;

    /// <summary>
    /// Published when building placement fails due to insufficient manpower.
    /// Consumed by MobilizationNarrativeResolver for social/narrative feedback.
    /// </summary>
    public record InsufficientManpowerEvent(
        string BuildingType,
        int Required,
        int Available) : IGameEvent;

    /// <summary>
    /// Published when casualties are reported (crew killed/wounded).
    /// </summary>
    public record ManpowerCasualtiesEvent(int Amount, int TotalCasualties, string Reason) : IGameEvent;

    /// <summary>
    /// Published when CallToArms is activated.
    /// </summary>
    public record CallToArmsEvent(int Recovered, int RemainingCasualties) : IGameEvent;

    /// <summary>
    /// Published when manpower deficit forces automatic crew release.
    /// Population exodus shrank the pool below currently used → phantom allocations trimmed.
    /// MobilizationSystem issues ForceCrewReleaseRequest entities; AACrewReleaseSystem
    /// consumes those requests and disables affected AA stations.
    /// </summary>
    public record ManpowerForceReleasedEvent(int Released, int NewTotal) : IGameEvent;
}
