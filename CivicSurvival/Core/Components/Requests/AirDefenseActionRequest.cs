using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request for spotter and district-defense actions that still share the air-defense action enum.
    /// Ephemeral entity pattern - created by UI, processed by SpotterCommandIngressSystem.
    /// Producers must hand off through TriggerOutcome.HandOffToEcs so TriggerDispatch attaches
    /// RequestMeta; SpotterCommandIngressSystem intentionally ignores payload-only entities.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct AirDefenseActionRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Action to perform.</summary>
        public AirDefenseActionType Action;

        /// <summary>Reserved payload slot; current spotter actions ignore it.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int TargetDistrictIndex;
#pragma warning restore CIVIC262
    }
}
