using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to start power plant repair.
    /// Ephemeral entity pattern - created by UI, consumed by PlantRepairIntakeSystem
    /// in ModificationEnd so repair starts while the simulation is paused.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct PlantRepairRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Stable plant ID (entity index) for repair target.</summary>
        public int StablePlantId;

        /// <summary>Type of repair to perform.</summary>
        public RepairType RepairType;
    }
}
