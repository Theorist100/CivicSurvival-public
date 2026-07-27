using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to start civilian building repair.
    /// Ephemeral entity pattern - created by UI trigger, processed by CivilianRepairDetectorSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct CivilianRepairRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Type of repair to perform.</summary>
        public RepairType RepairType;
    }
}
