using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Narrative
{
    /// <summary>
    /// Read-only access to persisted story-character bindings for domains that
    /// reconcile derived runtime projections from Narrative state.
    ///
    /// Returns <see cref="Entity.Null"/> for unbound character ids. Null-object
    /// generator emits <c>NullNarrativeCharacterBindings</c> returning
    /// <c>default(Entity)</c> = <see cref="Entity.Null"/> for every lookup, so
    /// cross-feature consumers (e.g., Spotters reading Valera binding) can call
    /// uniformly through <c>TryGetOrNullObject</c> without an extra null guard
    /// on the service itself.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.NarrativeName)]
    [GenerateNullObject]
    public interface INarrativeCharacterBindings
    {
        Entity GetBoundEntity(string characterId);
    }
}
