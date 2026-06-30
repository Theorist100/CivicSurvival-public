using System.Collections.Generic;

namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Optional capability interface: feature declares hard dependencies on
    /// other features by id. If any dependency is closed, this feature is
    /// closed transitively (logged as `dep-skipped`).
    ///
    /// Optional/soft producers are documented in the feature class summary,
    /// not declared here.
    /// </summary>
    public interface IDependentFeatureModule
    {
        IReadOnlyList<string> Dependencies { get; }
    }
}
