using Unity.Entities;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Canonical runtime key for building identity maps that cannot store Entity directly.
    /// High 32 bits = Entity.Index, low 32 bits = Entity.Version.
    /// </summary>
    public static class BuildingIdentityKey
    {
        public static long Pack(Entity entity) => Pack(entity.Index, entity.Version);

        public static long Pack(int index, int version) => ((long)index << 32) | (uint)version;
    }
}
