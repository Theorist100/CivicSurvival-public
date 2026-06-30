using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to set corruption scheme percentage.
    /// Ephemeral entity pattern - created by UI, processed by respective scheme system.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct CorruptionSchemeRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Type of scheme to modify.</summary>
        public CorruptionSchemeType SchemeType;

        /// <summary>New percentage value (0-100).</summary>
        public int Percent;
    }
}
