using System.Collections.Generic;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Gate evaluated by FeatureRegistry to decide whether a feature enters
    /// the game on a given launch.
    ///
    /// Static startup gates only. Future runtime gates (OnPopulation,
    /// OnMilestone) are intentionally out of scope for this migration.
    ///
    /// Discriminated-union pattern: abstract base record + sealed nested records.
    /// <see cref="IFeatureGateContext"/> is provided by the registry so gate
    /// instances carry no state.
    /// </summary>
#pragma warning disable S1694 // Discriminated union: nested sealed records hold state, an interface would lose record value semantics.
    public abstract record FeatureGate
#pragma warning restore S1694
    {
        /// <summary>Evaluate the gate. Returns true if the feature is open.</summary>
        public abstract bool IsOpen(IFeatureGateContext ctx);

        /// <summary>Feature enters the game in every configuration.</summary>
        public sealed record AlwaysOpen() : FeatureGate
        {
            public override bool IsOpen(IFeatureGateContext ctx) => true;
        }

        /// <summary>
        /// Feature enters the game iff <paramref name="FeatureId"/> is itself
        /// open (transitive dependency). Closed dependency closes dependent.
        /// </summary>
        public sealed record RequiresFeature(string FeatureId) : FeatureGate
        {
            public override bool IsOpen(IFeatureGateContext ctx) =>
                ctx.IsFeatureAvailable(FeatureId);
        }

        /// <summary>Feature enters the game iff every nested gate is open.</summary>
        public sealed record AllOf(IReadOnlyList<FeatureGate> Gates) : FeatureGate
        {
            public static AllOf Of(params FeatureGate[] gates) => new(gates);

            public override bool IsOpen(IFeatureGateContext ctx)
            {
                if (Gates == null) return true;
                foreach (var g in Gates)
                    if (!g.IsOpen(ctx))
                        return false;
                return true;
            }
        }
    }

    /// <summary>
    /// Evaluation context exposed by FeatureRegistry to gates.
    /// Dependency awareness, no live config access.
    /// </summary>
    public interface IFeatureGateContext
    {
        /// <summary>True if the feature with this id has been registered AND its gate evaluated open.</summary>
        bool IsFeatureOpen(string featureId);

        /// <summary>True if the feature with this id is open and has not failed registration.</summary>
        bool IsFeatureAvailable(string featureId);
    }
}
