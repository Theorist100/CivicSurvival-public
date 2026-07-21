using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Debug
{
    [OwnedByFeatureId(FeatureIds.AttentionName)]
    [GenerateNullObject]
    public interface IShockDebugMutator
    {
        void DebugSetShockLevel(float level, string source);
        void DebugResetShock(string source);
    }

    [OwnedByFeatureId(FeatureIds.AttentionName)]
    [GenerateNullObject]
    public interface IExodusDebugMutator
    {
        bool DebugIsExodusActive { get; }
        void DebugSetExodusActive(bool active, string source);
        void DebugResetExodus(string source);
    }

    [OwnedByFeatureId(FeatureIds.CountermeasuresName)]
    [GenerateNullObject]
    public interface ICountermeasuresDebugMutator
    {
        void DebugSetCorruption(float value, string source);
        void DebugSetHeat(float value, string source);
        void DebugResetCountermeasures(string source);
        /// <summary>Force the FSM into WaitingForInvestigationChoice (opens the decision modal) for pause-safety testing.</summary>
        void DebugForceInvestigationChoice(string source);
        /// <summary>Force the FSM into WaitingForPoliceChoice (opens the ultimatum modal) for pause-safety testing.</summary>
        void DebugForcePoliceChoice(string source);
    }

    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    [GenerateNullObject]
    public interface IReputationDebugMutator
    {
        void DebugSetTrust(float value, string source);
        void DebugResetReputation(string source);
    }

    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    [GenerateNullObject]
    public interface IDraftExemptionDebugMutator
    {
        void DebugForceOffer(string source);
    }

    [OwnedByFeatureId(FeatureIds.MobilizationName)]
    [GenerateNullObject]
    public interface IMobilizationDebugMutator
    {
        void DebugSetMoraleFactor(float value, string source);
        void DebugResetMobilization(string source);
    }

    [OwnedByFeatureId(FeatureIds.GridWarfareName)]
    [GenerateNullObject]
    public interface IEnemyDebugMutator
    {
        void DebugSetPressure(float value, string source);
        void DebugResetEnemy(string source);
    }

    [OwnedByFeatureId(FeatureIds.EconomyName)]
    [GenerateNullObject]
    public interface IEconomyDebugMutator
    {
        void DebugResetEconomy(string source);
    }

    /// <summary>
    /// Launches enemy PSYOPS raids on demand. In play a raid only composes when a wave enters its
    /// PsyAttack phase, so the info-war block is otherwise unreachable from a running city — these
    /// hooks drive the SAME composition path (launch log, PsyOpsAttackEvent, inbound modal, land
    /// snapshot, [PsyOpsHarm] probe), just without the wave cadence.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.CognitiveName)]
    [GenerateNullObject]
    public interface IPsyOpsDebugMutator
    {
        /// <param name="typeOverride">
        /// -1 = a full raid for the current wave (gap-biased dominant + secondary contacts, exactly
        /// as in play); 0/1/2 = a single contact of <see cref="PsyOpsAttackType"/> Propaganda / Rumor
        /// / FakeVideo, for reading one carrier's damage against one hero posture.
        /// </param>
        /// <param name="stratumOverride">
        /// 0 = the in-play weighted targeting; 1/2/3 = force the target
        /// <see cref="SocialStratum"/> Poor / Middle / Wealthy — without this a dev raid rolls a
        /// random stratum and testing a stratum-gated effect (draft fear, fake sick-out) means
        /// re-rolling until the right class comes up.
        /// </param>
        void DebugLaunchRaid(int typeOverride, int stratumOverride, string source);

        /// <summary>
        /// Land every in-flight attack now. A raid's damage is only readable after it lands, and the
        /// in-flight window is hours of game time — without this a debug launch means waiting.
        /// </summary>
        void DebugLandInFlight(string source);
    }
}
