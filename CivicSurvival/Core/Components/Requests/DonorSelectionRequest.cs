using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to select a donor package.
    /// Ephemeral entity pattern - created by UI, processed by DonorConferenceSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct DonorSelectionRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Type of package to select (Funds, Power, Defense).</summary>
        public DonorSelectionType SelectionType;
    }
}
