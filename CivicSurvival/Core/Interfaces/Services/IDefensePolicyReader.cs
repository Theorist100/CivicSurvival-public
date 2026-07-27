using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Read-only interface for accessing current defense policy.
    /// Allows cross-domain systems (e.g. ThreatDamage) to read policy
    /// without importing AirDefense.Systems directly (АКСІОМА 5).
    /// Registered by AirDefensePolicySystem via ServiceRegistry.
    /// Null-object: Unavailable is fail-closed for scandal checks when AirDefense is unavailable.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IDefensePolicyReader
    {
        [NullReturn(DefensePolicy.Unavailable)]
        DefensePolicy CurrentPolicy { get; }
    }
}
