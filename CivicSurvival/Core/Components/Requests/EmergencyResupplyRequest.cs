using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    public enum EmergencyResupplyKind : byte
    {
        None = 0,
        Emergency = 1,
        /// <summary>Restock every gun type (Bofors/Gepard/Heritage) at once in one batch, paying
        /// the summed flat per-type cost. <see cref="EmergencyResupplyRequest.Target"/> is ignored.
        /// Patriot is never part of this group — it uses its own single-type Emergency request.</summary>
        EmergencyGuns = 2
    }

    /// <summary>
    /// Request to perform emergency AA resupply.
    /// Separated from AirDefenseActionRequest (S24-A2) to eliminate dual-ownership:
    /// - This component → AirDefenseActionRequestSystem (sole owner)
    /// - AirDefenseActionRequest → SpotterCommandIngressSystem (sole owner)
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct EmergencyResupplyRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>Discriminator; zero-initialized stale requests fail closed.</summary>
        public EmergencyResupplyKind Kind;

        /// <summary>
        /// Which AA type to resupply (single-type <see cref="EmergencyResupplyKind.Emergency"/>
        /// path only; ignored for <see cref="EmergencyResupplyKind.EmergencyGuns"/>). The request
        /// pays that type's own cost and refills only installations of this type. Transient
        /// (consumed same/next frame), so it rides the IEmptySerializable transient-input
        /// lifecycle without persistence. Zero-init defaults to HeritageBofors — a valid type,
        /// fail-safe for a stale entity.
        /// </summary>
        public AAType Target;
    }
}
