using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Corruption
{
    /// <summary>
    /// Per-day fire dedup reader for counterfeit battery fires.
    /// Lets backup-power effect producers skip triggering a generic fire on a
    /// building that a counterfeit-battery scheme already ignited today.
    ///
    /// Implementor: CounterfeitBatteryFireSystem (Corruption domain).
    /// Consumers: BackupPowerEffectsSystem (PowerBackup domain).
    ///
    /// Null-object semantics: when Corruption is closed or the producer hasn't
    /// loaded yet, NullCounterfeitFireDedupReader.Instance is returned and
    /// WasFiredToday => false ("no prior fires today, proceed normally").
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.CorruptionName)]
    public interface ICounterfeitFireDedupReader
    {
        [NullReturn(false)]
        bool WasFiredToday(Entity buildingEntity);
    }
}
