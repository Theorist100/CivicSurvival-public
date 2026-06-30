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
    }

    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    [GenerateNullObject]
    public interface IReputationDebugMutator
    {
        void DebugSetTrust(float value, string source);
        void DebugResetReputation(string source);
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
}
