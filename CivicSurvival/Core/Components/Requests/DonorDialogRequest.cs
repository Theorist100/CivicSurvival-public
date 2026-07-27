using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Action type for donor dialog.
    /// </summary>
    public enum DonorDialogAction : byte
    {
        /// <summary>No action; fail-closed sentinel for zero-initialized stale requests.</summary>
        None = 0,

        /// <summary>Open the donor conference dialog.</summary>
        Open = 1,

        /// <summary>Close the donor conference dialog.</summary>
        Close = 2
    }

    /// <summary>
    /// Request to open/close donor conference dialog.
    /// Ephemeral entity pattern - created by UI, processed by DonorConferenceSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct DonorDialogRequest : IComponentData, ICommandRequest, IEmptySerializable
    {

        /// <summary>Action to perform (open/close).</summary>
        public DonorDialogAction Action;
    }
}
