using System;
using Unity.Entities;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Typed reference to a vanilla district entity.
    /// Identity is Index + Version — districts can be removed via drawing tool,
    /// so the slot can be reused; bare int index is reuse-unsafe.
    /// Replaces raw int districtIndex fields/params with a single canonical type.
    /// </summary>
    public struct DistrictRef : IEquatable<DistrictRef>
    {
        public int Index;
        public int Version;

        public DistrictRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public static DistrictRef FromEntity(Entity entity)
        {
            return new DistrictRef(entity.Index, entity.Version);
        }

        public readonly Entity ToEntity()
        {
            return new Entity { Index = Index, Version = Version };
        }

        /// <summary>8-byte packed form: high 32 bits = Index, low 32 bits = Version.
        /// Matches existing codebase convention.</summary>
        public readonly long Packed => ((long)Index << 32) | (uint)Version;

        public static DistrictRef FromPacked(long packed)
        {
            return new DistrictRef((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        }

        public readonly bool IsNull => Index == 0 && Version == 0;

        public readonly bool TryResolve(EntityManager entityManager, out Entity entity)
        {
            entity = ToEntity();
            if (entity == Entity.Null || !entityManager.Exists(entity))
            {
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        public readonly Entity ResolveOrNull(EntityManager entityManager)
        {
            return TryResolve(entityManager, out var entity) ? entity : Entity.Null;
        }

        /// <summary>Standard small prime used for combining two int hash components without bias.</summary>
        private const int HashCombinePrime = 397;

        public readonly bool Equals(DistrictRef other) => Index == other.Index && Version == other.Version;
        public override readonly bool Equals(object obj) => obj is DistrictRef other && Equals(other);
        public override readonly int GetHashCode() => unchecked((Index * HashCombinePrime) ^ Version);
        public override readonly string ToString() => $"District({Index}:{Version})";

        public static bool operator ==(DistrictRef a, DistrictRef b) => a.Equals(b);
        public static bool operator !=(DistrictRef a, DistrictRef b) => !a.Equals(b);

        public static readonly DistrictRef Null = new DistrictRef(0, 0);
    }
}
