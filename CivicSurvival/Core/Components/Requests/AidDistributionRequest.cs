using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to distribute humanitarian aid to a district.
    /// Ephemeral entity pattern - created by UI, processed by HumanitarianAidSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct AidDistributionRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>District index to distribute aid to.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Observable request id for UI lifecycle tracking.</summary>
    }
}
