using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to toggle internet shutdown for a district.
    /// Ephemeral entity pattern - created by UI, processed in ModificationEnd.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct DistrictInternetToggleRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>District entity index to toggle.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district identifier (never reconstructed to Entity)
        public int DistrictEntityIndex;
#pragma warning restore CIVIC262
    }
}
