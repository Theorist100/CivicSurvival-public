using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares the feature that owns a service interface or concrete service class.
    /// Attribute arguments must use FeatureIds.XName constants. Applied to interfaces
    /// (the common case — ownership of cross-feature contracts) and to concrete
    /// classes that are registered through a feature lifecycle but have no separate
    /// interface contract (e.g. state holders, content services).
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OwnedByFeatureIdAttribute : Attribute
    {
        public OwnedByFeatureIdAttribute(string featureId)
        {
            FeatureId = featureId ?? string.Empty;
        }

        public string FeatureId { get; }
    }
}
