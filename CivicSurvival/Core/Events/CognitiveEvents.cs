using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Published when a district's cognitive integrity drops below compromise threshold.
    /// District now receives happiness/commerce penalties.
    ///
    /// Published by: CognitiveStateSystem
    /// Consumed by: NarrativeNotificationSystem (for social posts)
    /// </summary>
    /// <param name="DistrictIndex">Affected district index.</param>
    /// <param name="Integrity">Current integrity value (0.0 - threshold).</param>
    public record CognitiveCompromisedEvent(int DistrictIndex, float Integrity) : IGameEvent;

    /// <summary>
    /// Published when a district's cognitive integrity recovers above compromise threshold.
    /// District no longer receives compromise penalties.
    ///
    /// Published by: CognitiveStateSystem
    /// Consumed by: NarrativeNotificationSystem (for recovery announcements)
    /// </summary>
    /// <param name="DistrictIndex">Affected district index.</param>
    /// <param name="Integrity">Current integrity value (threshold - 1.0).</param>
    public record CognitiveRecoveredEvent(int DistrictIndex, float Integrity) : IGameEvent;

    /// <summary>
    /// Published when hero unit (Gerda) is deployed.
    /// </summary>
    /// <param name="Mode">"deployed" or "lecturing".</param>
    /// <param name="Cost">Deployment cost.</param>
    public record HeroDeployedEvent(string Mode, int Cost) : IGameEvent;

    /// <summary>
    /// Published when hero unit is recalled.
    /// </summary>
    public record HeroRecalledEvent() : IGameEvent;

    /// <summary>
    /// Published when hero mode is switched (while already deployed).
    /// </summary>
    /// <param name="FromMode">Previous mode.</param>
    /// <param name="ToMode">New mode.</param>
    public record HeroModeChangedEvent(string FromMode, string ToMode) : IGameEvent;

    /// <summary>
    /// Published when buckwheat (humanitarian aid) is distributed to a district.
    ///
    /// Published by: BuckwheatSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    /// <param name="DistrictIndex">District receiving aid.</param>
    /// <param name="TonsRemaining">Buckwheat reserve after distribution.</param>
    /// <param name="TrustBoost">Trust bonus applied.</param>
    public record BuckwheatDistributedEvent(int DistrictIndex, float TonsRemaining, float TrustBoost) : IGameEvent;

    /// <summary>
    /// Published when buckwheat is procured from shadow funds.
    ///
    /// Published by: BuckwheatSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    /// <param name="TonsProcured">Tons of buckwheat procured.</param>
    /// <param name="Cost">Cost in shadow money.</param>
    /// <param name="ReserveTotal">Total reserve after procurement.</param>
    public record BuckwheatProcuredEvent(float TonsProcured, int Cost, float ReserveTotal) : IGameEvent;

    /// <summary>
    /// Published when telemarathon is in Soothing mode during an active attack (trust shock).
    ///
    /// Published by: TelemarathonSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public readonly struct TelemarathonShockEvent : IGameEvent
    {
        public readonly float TrustAfter;
        public TelemarathonShockEvent(float trustAfter) => TrustAfter = trustAfter;
    }

    /// <summary>
    /// Published when telemarathon narrative mode changes (Soothing/Alarmist/Realistic).
    ///
    /// Published by: TelemarathonSystem
    /// Consumed by: TelemetryEventListener
    /// </summary>
    public readonly struct TelemarathonModeChangedEvent : IGameEvent
    {
        public readonly NarrativeMode OldMode;
        public readonly NarrativeMode NewMode;
        public TelemarathonModeChangedEvent(NarrativeMode oldMode, NarrativeMode newMode)
        {
            OldMode = oldMode;
            NewMode = newMode;
        }
    }
}
