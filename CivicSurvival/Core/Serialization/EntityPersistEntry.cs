namespace CivicSurvival.Core.Serialization
{
    public readonly struct EntityPersistEntry
    {
        public EntityPersistEntry(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public int Index { get; }
        public int Version { get; }
    }
}
