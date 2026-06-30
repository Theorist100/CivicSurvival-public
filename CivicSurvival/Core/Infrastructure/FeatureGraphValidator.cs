using System;
using System.Collections.Generic;
using System.Linq;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Static validation of the feature dependency graph. Used by
    /// <see cref="FeatureRegistry"/> at registration time and by tests
    /// directly.
    ///
    /// Validation rules:
    ///   1. Feature ids are unique.
    ///   2. Every <see cref="IDependentFeatureModule"/> dependency target exists.
    ///   3. No dependency cycles.
    ///   4. Every <see cref="FeatureGate.RequiresFeature"/> target exists AND has
    ///      strictly lower priority than the dependent. Registry processes features
    ///      in ascending priority order; a gate that points at a higher-priority
    ///      feature evaluates against an empty <c>m_OpenFeatures</c> set and silently
    ///      closes — leaving the dependent's systems unregistered, which surfaces
    ///      later as a runtime <c>FeatureRegistry.Require&lt;T&gt;()</c> failure.
    /// </summary>
    public static class FeatureGraphValidator
    {
        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> if any rule fails.
        /// </summary>
        public static void Validate(IReadOnlyList<IFeatureModule>? features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));

            ValidateUniqueIds(features);
            ValidateDependencyTargetsExist(features);
            ValidateNoCycles(features);
            ValidateGatePriorityOrdering(features);
            ValidateDependencyPriorityOrdering(features);
        }

        private static void ValidateUniqueIds(IReadOnlyList<IFeatureModule> features)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in features)
            {
                if (!seen.Add(f.Name))
                    throw new InvalidOperationException($"Duplicate feature id: {f.Name}");
            }
        }

        private static void ValidateDependencyTargetsExist(IReadOnlyList<IFeatureModule> features)
        {
            var ids = new HashSet<string>(features.Select(f => f.Name), StringComparer.Ordinal);
            foreach (var f in features)
            {
                if (f is IDependentFeatureModule dep && dep.Dependencies != null)
                {
                    foreach (var depId in dep.Dependencies)
                    {
                        if (!ids.Contains(depId))
                            throw new InvalidOperationException(
                                $"Feature '{f.Name}' depends on unknown feature '{depId}'");
                    }
                }
            }
        }

        private static void ValidateNoCycles(IReadOnlyList<IFeatureModule> features)
        {
            var byId = features.ToDictionary(f => f.Name, StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (var f in features)
            {
                if (f is IDependentFeatureModule)
                    DfsCycleCheck(f.Name, byId, visiting, visited);
            }
        }

        private static void DfsCycleCheck(
            string id,
            IReadOnlyDictionary<string, IFeatureModule> byId,
            HashSet<string> visiting,
            HashSet<string> visited)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
                throw new InvalidOperationException($"Feature dependency cycle detected involving '{id}'");

            if (byId.TryGetValue(id, out var feature)
                && feature is IDependentFeatureModule dep
                && dep.Dependencies != null)
            {
                foreach (var depId in dep.Dependencies)
                {
                    if (byId.ContainsKey(depId))
                        DfsCycleCheck(depId, byId, visiting, visited);
                }
            }

            visiting.Remove(id);
            visited.Add(id);
        }

        private static void ValidateGatePriorityOrdering(IReadOnlyList<IFeatureModule> features)
        {
            // Safe after ValidateUniqueIds: each Name maps to exactly one feature.
            var byId = features.ToDictionary(f => f.Name, StringComparer.Ordinal);

            foreach (var f in features)
            {
                if (f is not IGatedFeatureModule gated || gated.Gate == null)
                    continue;

                foreach (var requiredId in CollectRequiredFeatureIds(gated.Gate))
                {
                    if (!byId.TryGetValue(requiredId, out var required))
                        throw new InvalidOperationException(
                            $"Feature '{f.Name}' gate requires unknown feature '{requiredId}'");

                    if (f.Priority <= required.Priority)
                        throw new InvalidOperationException(
                            $"Feature '{f.Name}' (priority {f.Priority}) gates on RequiresFeature('{requiredId}') " +
                            $"with priority {required.Priority}; required feature must have strictly lower " +
                            $"priority so it is registered first and visible to the gate. " +
                            $"Bump '{f.Name}' priority above {required.Priority}.");
                }
            }
        }

        private static void ValidateDependencyPriorityOrdering(IReadOnlyList<IFeatureModule> features)
        {
            // Safe after ValidateUniqueIds: each Name maps to exactly one feature.
            var byId = features.ToDictionary(f => f.Name, StringComparer.Ordinal);

            foreach (var f in features)
            {
                if (f is not IDependentFeatureModule dep || dep.Dependencies == null)
                    continue;

                foreach (var depId in dep.Dependencies)
                {
                    if (!byId.TryGetValue(depId, out var depFeature))
                        continue; // ValidateDependencyTargetsExist already reports missing targets.

                    if (f.Priority <= depFeature.Priority)
                        throw new InvalidOperationException(
                            $"Feature '{f.Name}' (priority {f.Priority}) declares Dependencies on '{depId}' " +
                            $"with priority {depFeature.Priority}; dependent feature must have strictly higher " +
                            $"priority so the dep target is evaluated first and visible in m_OpenFeatures. " +
                            $"Bump '{f.Name}' priority above {depFeature.Priority}.");
                }
            }
        }

        private static IEnumerable<string> CollectRequiredFeatureIds(FeatureGate gate)
        {
            switch (gate)
            {
                case FeatureGate.RequiresFeature req:
                    yield return req.FeatureId;
                    break;
                case FeatureGate.AllOf all when all.Gates != null:
                    foreach (var nested in all.Gates)
                        foreach (var id in CollectRequiredFeatureIds(nested))
                            yield return id;
                    break;
                // AlwaysOpen — no inter-feature priority requirement.
            }
        }
    }
}
