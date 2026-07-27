using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.NeighborEnvy
{
    /// <summary>
    /// Enableable tag: Building is affected by neighbor envy.
    /// Citizens in this building experience wellbeing penalty.
    ///
    /// IEnableableComponent: the structural first-add is owned EXCLUSIVELY by
    /// EnvyAffectedSetupSystem (Modification4 / ModificationBarrier4), which seeds it DISABLED
    /// onto every residential building. Doing the structural add in Modification4 keeps the
    /// archetype migration in phase with the render chunk-cache collection (render chunk-cache
    /// crash class). NeighborEnvySystem (GameSimulation) and its rebuild/incremental logic only
    /// toggle the enable-bit via SetComponentEnabled (bit flip, no structural change / chunk move).
    /// A structural AddComponent&lt;EnvyAffected&gt; anywhere else is banned by CIVIC520.
    ///
    /// Enabled when:
    /// - Building is blacked out
    /// - Has powered neighbor within 100m
    ///
    /// Shared via Core because used by both NeighborEnvy and Cognitive domains.
    /// </summary>
    public struct EnvyAffected : IComponentData, IEnableableComponent { }
}
