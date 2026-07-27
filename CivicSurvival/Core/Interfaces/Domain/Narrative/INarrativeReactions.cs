using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Narrative
{
    /// <summary>
    /// Interface for narrative reaction system.
    /// Allows other domains to trigger character reactions without direct dependency.
    /// Null-object: void methods are silent no-ops when Narrative feature is closed.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.NarrativeName)]
    public interface INarrativeReactions
    {
        /// <summary>
        /// Trigger a reaction from all characters of the specified archetype.
        /// Unknown trigger keys are ignored by the implementer; callers should use
        /// constants from ReactionTriggers, not ad-hoc strings.
        /// </summary>
        /// <param name="archetype">Character archetype to react</param>
        /// <param name="trigger">Reaction trigger key (from ReactionTriggers)</param>
        void TriggerReaction(CharacterArchetype archetype, string trigger);

        /// <summary>
        /// Trigger a reaction from a specific character by name.
        /// Unknown character names or trigger keys are ignored by the implementer;
        /// callers should use registry IDs and ReactionTriggers constants.
        /// </summary>
        /// <param name="characterName">Character ID (e.g., "Kotleta")</param>
        /// <param name="trigger">Reaction trigger key</param>
        void TriggerCharacterReaction(string characterName, string trigger);
    }
}
