using Colossal.UI.Binding;
using Unity.Entities;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Serializable UI-safe reference to a Unity entity.
    /// Entity identity is Index + Version; Index alone can point at a reused slot.
    ///
    /// Implements <see cref="IJsonReadable"/> so Coherent UI's <c>ValueReaders</c>
    /// binds it directly in trigger signatures (<c>Triggers.Add&lt;EntityRef&gt;(...)</c>) —
    /// no (int index, int version) unpacking on the C# side. JSON layout matches
    /// Unity's own Entity reader in <c>Colossal.UI.Binding.UnityReaders</c>:
    /// <c>{"index":N,"version":M}</c>.
    /// </summary>
    public struct EntityRef : IJsonReadable
    {
        public int Index;
        public int Version;

        public EntityRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public static EntityRef FromEntity(Entity entity)
        {
            return new EntityRef(entity.Index, entity.Version);
        }

        public Entity ToEntity()
        {
            return new Entity { Index = Index, Version = Version };
        }

        public bool TryResolve(EntityManager entityManager, out Entity entity)
        {
            entity = ToEntity();
            if (entity == Entity.Null || !entityManager.Exists(entity))
            {
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        public Entity ResolveOrNull(EntityManager entityManager)
        {
            return TryResolve(entityManager, out var entity) ? entity : Entity.Null;
        }

        public void Read(IJsonReader reader)
        {
            Index = 0;
            Version = 0;
            ulong count = reader.ReadMapBegin();
            for (ulong i = 0; i < count; i++)
            {
                reader.ReadMapKeyValue();
                string key;
                reader.Read(out key);
                if (key == "index")
                {
                    reader.Read(out Index);
                }
                else if (key == "version")
                {
                    reader.Read(out Version);
                }
                else
                {
                    reader.SkipValue();
                }
            }
            reader.ReadMapEnd();
        }
    }
}
