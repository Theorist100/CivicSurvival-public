using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to change backup power discharge policy.
    /// Ephemeral entity pattern - created by UI, processed by SettingsRequestSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct BackupPolicyRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Desired backup policy.</summary>
        public BackupPolicy Policy;
    }
}
