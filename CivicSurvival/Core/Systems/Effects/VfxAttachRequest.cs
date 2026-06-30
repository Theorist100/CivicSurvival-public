using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// One owner-attach request for <see cref="VanillaVfxSystem.AttachEffectBatch"/>:
    /// inject element <see cref="EffectIndex"/> of <see cref="Owner"/>'s prefab Effect
    /// buffer (which must equal <see cref="Prefab"/>) as a live owner-attached
    /// EnabledEffectData record, seeded at <see cref="Position"/>.
    ///
    /// Lives in Core so both the interceptor exhaust controller (AirDefense) and the
    /// ballistic exhaust controller (Waves) feed the same batch entry without a
    /// cross-domain import (Axiom 5).
    /// </summary>
    public readonly struct VfxAttachRequest
    {
        public readonly Entity Owner;
        public readonly Entity Prefab;
        public readonly int EffectIndex;
        public readonly float3 Position;

        public VfxAttachRequest(Entity owner, Entity prefab, int effectIndex, float3 position)
        {
            Owner = owner;
            Prefab = prefab;
            EffectIndex = effectIndex;
            Position = position;
        }
    }
}
