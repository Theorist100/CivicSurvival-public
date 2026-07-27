using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Domains.Refugees.Data
{
    /// <summary>
    /// Marker component for newly spawned refugee households.
    /// Added during spawn, disabled after PropertySeeker is disabled.
    /// IEnableableComponent: no structural change on consume — SetComponentEnabled(false) instead of RemoveComponent.
    /// Enables efficient query - only process new refugees, not all homeless.
    /// </summary>
    public struct PendingRefugeeProcess : IComponentData, IEnableableComponent, ISerializable
    {
        public bool NeedsPropertySeekerDisable;

        public void SetDefaults()
        {
            NeedsPropertySeekerDisable = true;
        }

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = CivicSurvival.Core.Serialization.SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                CivicSurvival.Core.Serialization.KeyedSerializer.WriteBlockHeader(writer, 1);
                CivicSurvival.Core.Serialization.KeyedSerializer.WriteField(writer, "needsDisable", NeedsPropertySeekerDisable);
            }
            finally
            {
                CivicSurvival.Core.Serialization.SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!CivicSurvival.Core.Serialization.SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PendingRefugeeProcess)))
            { SetDefaults(); return; }

            try
            {
                SetDefaults();
                if (version >= 1)
                {
                    int fc = CivicSurvival.Core.Serialization.KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = CivicSurvival.Core.Serialization.KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "needsDisable":
                                NeedsPropertySeekerDisable = CivicSurvival.Core.Serialization.KeyedSerializer.ReadBool(reader, tag, "needsDisable");
                                break;
                            default:
                                CivicSurvival.Core.Serialization.KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            finally
            {
                CivicSurvival.Core.Serialization.SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Permanent marker for refugee households.
    /// Added at spawn, NEVER removed - tracks refugee status for budget calculations.
    /// Used by RefugeeSupportCostSystem to calculate monthly support payments.
    /// Implements IEmptySerializable so the marker's presence round-trips through
    /// save/load (payload-less tag — nothing to serialize).
    /// </summary>
#pragma warning disable CIVIC272 // Marker component — no fields to reset
    public struct RefugeeHousehold : IComponentData, IEmptySerializable
#pragma warning restore CIVIC272
    {
        // IEmptySerializable marker: payload-less permanent tag; only its presence
        // round-trips through save/load.
    }

    /// <summary>
    /// Presence gate marker: this refugee household needs relocation to a live park.
    /// PRESENT = m_TempHome points at a border OutsideConnection (fresh border spawn)
    ///           OR at a destroyed/missing park (orphaned).
    /// ABSENT  = m_TempHome points at a live park (relocation done).
    ///
    /// Presence, NOT an enabled bit: RequireForUpdate uses ShouldRunSystem →
    /// IsEmptyIgnoreFilter → GetMatchingChunkCache().Length == 0, which counts matching
    /// chunks by archetype and IGNORES enableable enabled-bits (verified in decompiled
    /// Unity.Entities EntityQueryImpl). An enableable tag left present-but-disabled keeps
    /// a non-empty chunk, so RequireForUpdate would never close. A plain tag that is
    /// Added on border-spawn/orphaning and Removed on relocation does close the gate:
    /// once every refugee sits in a live park the marker is absent everywhere, the
    /// matching chunk drains, and RefugeeMigrationSystem stops being scheduled.
    ///
    /// IEmptySerializable: payload-less tag whose PRESENCE round-trips save/load
    /// (Colossal strips IComponentData without a serializer entirely on load). Presence
    /// IS the gate state, so it persists directly. RefugeeMigrationSystem.ValidateAfterLoad
    /// reconciles it against the durable HomelessHousehold.m_TempHome (Add where a refugee
    /// is at the border/orphaned, Remove where it is in a live park) to repair any save
    /// taken mid-destruction before the orphan scan ran.
    /// </summary>
#pragma warning disable CIVIC272 // Marker component — no fields to reset
    public struct NeedsRefugeeRelocation : IComponentData, IEmptySerializable
#pragma warning restore CIVIC272
    {
        // Payload-less. Gate state is carried by presence/absence, not a field.
    }
}
