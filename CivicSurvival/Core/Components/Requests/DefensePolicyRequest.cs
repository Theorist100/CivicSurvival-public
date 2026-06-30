using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to change air defense policy.
    /// Ephemeral entity pattern - created by UI, processed by AirDefenseSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct DefensePolicyRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>New defense policy to set.</summary>
        public DefensePolicy Policy;
    }
}
