using System;
using Unity.Entities;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Typed reference to a vanilla building entity.
    /// Identity is Index + Version — Index alone can point at a reused slot.
    /// Replaces paired int BuildingIndex + int BuildingVersion fields and
    /// packed-long encoding sites with a single canonical type.
    /// </summary>
    public struct BuildingRef : IEquatable<BuildingRef>
    {
        public int Index;
        public int Version;

        public BuildingRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public static BuildingRef FromEntity(Entity entity)
        {
            return new BuildingRef(entity.Index, entity.Version);
        }

        public readonly Entity ToEntity()
        {
            return new Entity { Index = Index, Version = Version };
        }

        /// <summary>8-byte packed form: high 32 bits = Index, low 32 bits = Version.
        /// Matches existing codebase convention (PackBuildingKey).</summary>
        public readonly long Packed => ((long)Index << 32) | (uint)Version;

        public static BuildingRef FromPacked(long packed)
        {
            return new BuildingRef((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
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

        public readonly bool Equals(BuildingRef other) => Index == other.Index && Version == other.Version;
        public override readonly bool Equals(object obj) => obj is BuildingRef other && Equals(other);
        public override readonly int GetHashCode() => unchecked((Index * HashCombinePrime) ^ Version);
        public override readonly string ToString() => $"Building({Index}:{Version})";

        public static bool operator ==(BuildingRef a, BuildingRef b) => a.Equals(b);
        public static bool operator !=(BuildingRef a, BuildingRef b) => !a.Equals(b);

        public static readonly BuildingRef Null = new BuildingRef(0, 0);
    }
}
