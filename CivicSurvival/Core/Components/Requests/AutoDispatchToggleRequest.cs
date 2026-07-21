using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to toggle auto-dispatch on/off.
    /// No payload - toggle inverts current state.
    /// Ephemeral entity pattern - created by UI, processed by SettingsRequestSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct AutoDispatchToggleRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
    }
}
