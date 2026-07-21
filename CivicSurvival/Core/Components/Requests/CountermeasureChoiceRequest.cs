using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Type of countermeasure choice.
    /// </summary>
    public enum CountermeasureChoiceType : byte
    {
        /// <summary>Investigation countermeasure choice.</summary>
        Investigation = 0,

        /// <summary>Police bribery choice.</summary>
        Police
    }

    /// <summary>
    /// Request to make a countermeasure choice.
    /// Ephemeral entity pattern - created by UI, processed by CountermeasuresSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct CountermeasureChoiceRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Type of choice (Investigation or Police).</summary>
        public CountermeasureChoiceType ChoiceType;

        /// <summary>The choice value (enum cast to int).</summary>
        public int ChoiceValue;
    }
}
