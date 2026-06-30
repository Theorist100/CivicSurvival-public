using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to set the Buckwheat procurement level.
    /// Cognitive-owned counterpart to corruption scheme settings.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct BuckwheatProcurementLevelRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        public int Percent;
    }
}
