namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// PascalCase wire reference to a Unity entity for embedding inside
    /// contract DTOs. Distinct from Core.Types.EntityRef which uses the
    /// camelCase {index, version} wire layout dictated by Coherent UI's
    /// trigger ValueReaders.
    /// </summary>
    public partial struct EntityRefDto
    {
        public int Index;
        public int Version;

        public EntityRefDto(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public static EntityRefDto FromEntity(Unity.Entities.Entity entity)
            => new(entity.Index, entity.Version);
    }
}
