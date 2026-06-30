using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to perform a mobilization action.
    /// Ephemeral entity pattern - created by UI, processed by MobilizationSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct MobilizationRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Action to perform (ActivateConscription, DeactivateConscription, CallToArms).</summary>
        public MobilizationActionType Action;
    }
}
