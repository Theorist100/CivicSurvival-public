using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Optional capability interface: feature exposes a gate that the registry
    /// evaluates against the current FeatureManifest.
    ///
    /// Features that do NOT implement this interface are treated as
    /// <see cref="FeatureGate.AlwaysOpen"/>.
    ///
    /// Keep the gate as an explicit property so module metadata stays visible to
    /// the registry and analyzers. Do not hide gate behavior behind helper-only
    /// conventions.
    /// </summary>
    public interface IGatedFeatureModule
    {
        FeatureGate Gate { get; }
    }
}
