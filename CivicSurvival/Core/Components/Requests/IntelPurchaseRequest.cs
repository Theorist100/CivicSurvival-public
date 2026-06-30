using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Type of intel purchase.
    /// </summary>
    public enum IntelPurchaseType : byte
    {
        /// <summary>No purchase; fail-closed sentinel for zero-initialized stale requests.</summary>
        None = 0,

        /// <summary>Purchase insider information (AirDefense).</summary>
        Insider = 1,

        /// <summary>Purchase intel upgrade (GridWarfare).</summary>
        Upgrade = 2
    }

    /// <summary>
    /// Request to purchase intelligence.
    /// Ephemeral entity pattern - created by UI, processed by IntelPurchaseSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct IntelPurchaseRequest : IComponentData, ICommandRequest, IEmptySerializable
    {

        /// <summary>Type of intel to purchase.</summary>
        public IntelPurchaseType PurchaseType;

        /// <summary>
        /// Final price shown by the originating UI.
        /// Purchase systems reject if the resolved price changes before processing.
        /// </summary>
        public long ExpectedCost;
    }
}
