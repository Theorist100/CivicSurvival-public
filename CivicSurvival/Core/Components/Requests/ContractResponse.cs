using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Response type for maintenance contract offers.
    /// </summary>
    public enum ContractResponseType : byte
    {
        /// <summary>No response; fail-closed sentinel for zero-initialized stale requests.</summary>
        None = 0,

        /// <summary>Accept official (legal) contract.</summary>
        AcceptOfficial = 1,

        /// <summary>Accept shady (shadow) contract.</summary>
        AcceptShady = 2,

        /// <summary>Decline the offer.</summary>
        Decline = 3
    }

    /// <summary>
    /// Request to respond to a maintenance contract offer.
    /// Ephemeral entity pattern - created by UI, processed by MaintenanceContractSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct ContractResponse : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Building entity index the contract is for.</summary>
        public int BuildingEntityIndex;

        /// <summary>Building entity version for exact entity match.</summary>
        public int BuildingEntityVersion;

        /// <summary>Type of response (accept official, accept shady, decline).</summary>
        public ContractResponseType ResponseType;

        /// <summary>Price displayed to the user when accepting; 0 for decline.</summary>
        public long ExpectedPrice;
    }
}
