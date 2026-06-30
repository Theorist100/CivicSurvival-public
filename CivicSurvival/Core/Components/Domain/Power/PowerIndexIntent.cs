using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Power-index materialisation intent — created by <c>PowerCapacityIndexSystem</c>
    /// (producer, GameSimulation) when a grid plant first needs its index/modifier
    /// components, consumed by <c>PowerIndexApplySystem</c> (consumer, ModificationEnd)
    /// which performs the archetype migration on the vanilla plant.
    ///
    /// Mirrors the vanilla split <c>FireSimulationSystem</c> (producer) → <c>IgniteSystem</c>
    /// (consumer) and the mod's own <c>ModFireIntent</c> / <c>AAPlacementIntent</c> pattern.
    /// The producer only CREATES this intent entity — legal from GameSimulation, because
    /// creating a new entity is not a structural change on the vanilla plant. The first add
    /// of the index components (<c>PlantBaseCapacity</c>, <c>PowerPlantKind</c>, the grid
    /// modifiers, <c>PowerCapacityIndexState</c>) migrates the plant archetype and MUST land
    /// in ModificationEnd, the phase the vanilla render batch pipeline expects
    /// (<c>RequiredBatchesSystem</c>, <c>PreCullingSystem</c>, <c>BatchInstanceSystem</c> all
    /// run later in the same MainLoop). Doing the structural add from GameSimulation
    /// (LateUpdate, end of frame) instead lands it out of phase with the render pass and can
    /// crash a vanilla Burst batch job on a null chunk pointer (dump 1138).
    ///
    /// Only the producer COMPUTES the values (it reads grid lookups in GameSimulation, the
    /// phase where they are valid); the consumer is a dumb <c>AddComponent</c>. This keeps
    /// all grid-state reads in GameSimulation and avoids moving the whole index system to a
    /// pause-ticking phase.
    ///
    /// Serializable, like <c>ModFireIntent</c> / <c>AAPlacementIntent</c>: a save taken in
    /// the 1-frame window between producer (frame N) and consumer (frame N+1) keeps the
    /// intent and the consumer reprocesses it after load, so the new plant is neither left
    /// unindexed nor leaked as an orphan entity. The index itself is re-derivable on load
    /// (<c>ValidateAfterLoad</c> immediate pass), so a surviving intent whose plant is
    /// already indexed is simply destroyed by the consumer's already-indexed guard.
    /// </summary>
    public struct PowerIndexIntent : IComponentData, ISerializable
    {
        /// <summary>Target vanilla grid plant (Index+Version — Axiom 11, no Entity fields).</summary>
        public BuildingRef Building;

        /// <summary>Plant classification computed by the producer.</summary>
        public PlantKind Kind;

        /// <summary>Original (nameplate) capacity in kW for <c>PlantBaseCapacity</c>.</summary>
        public int OriginalCapacityKW;

        /// <summary>
        /// True for a fully classified plant — the consumer adds the full index set
        /// (base capacity, kind, the five grid modifiers, index state). False for an
        /// unclassified producer — the consumer adds only <c>PowerPlantKind</c>=Unclassified
        /// (mirrors the producer's unclassified early-out, which never materialises capacity
        /// or index state).
        /// </summary>
        public bool Classified;

        /// <summary>
        /// Initial <c>ConstructionModifier.IsUnderConstruction</c> the consumer seeds on the
        /// plant. Carries the producer's <c>ConstructionDelayEnabled</c> read so the consumer
        /// stays a dumb materialiser: feature-on ⇒ a new plant starts under-construction (0 MW
        /// until ConstructionDelaySystem classifies and ramps it); feature-off ⇒ false ⇒ full
        /// MW immediately with no dependence on CDS/the classification gate.
        /// </summary>
        public bool ConstructionPending;

        /// <summary>Fully computed index state — the producer does not re-derive on the consumer side.</summary>
        public PowerCapacityIndexState IndexState;

        /// <summary>
        /// Runtime-only idempotency guard: set when the consumer queues this intent's
        /// apply+destroy into the deferred ECB. CS2 barriers play back after all sim ticks,
        /// so at 2x-3x the intent is still alive on later ticks of the same frame — this
        /// stops a double-apply. INTENTIONALLY NOT serialized: a save taken before playback
        /// must reprocess the surviving intent (the queued ECB commands do not survive the
        /// save), so after load this is default false by design.
        /// </summary>
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;
        private const int MAX_CAPACITY_KW = 100_000_000;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 10);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "kind", (byte)Kind);
                KeyedSerializer.WriteField(writer, "cap", OriginalCapacityKW);
                KeyedSerializer.WriteField(writer, "cls", Classified);
                KeyedSerializer.WriteField(writer, "cPnd", ConstructionPending);
                KeyedSerializer.WriteField(writer, "pIdx", IndexState.PrefabIndex);
                KeyedSerializer.WriteField(writer, "pVer", IndexState.PrefabVersion);
                KeyedSerializer.WriteField(writer, "uHsh", IndexState.UpgradeHash);
                KeyedSerializer.WriteField(writer, "hHsh", IndexState.HydroShapeHash);
                KeyedSerializer.WriteEnumByteField(writer, "chan", (byte)IndexState.Channel);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PowerIndexIntent)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "kind": Kind = KeyedSerializer.ReadEnumByte<TReader, PlantKind>(reader, tag, "kind", PlantKind.Unclassified); break;
                            case "cap": OriginalCapacityKW = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, MAX_CAPACITY_KW, 0); break;
                            case "cls": Classified = KeyedSerializer.ReadBool(reader, tag, "cls"); break;
                            case "cPnd": ConstructionPending = KeyedSerializer.ReadBool(reader, tag, "cPnd"); break;
                            case "pIdx": IndexState.PrefabIndex = KeyedSerializer.ReadBoundedInt(reader, tag, "pIdx", int.MinValue, int.MaxValue, 0); break;
                            case "pVer": IndexState.PrefabVersion = KeyedSerializer.ReadBoundedInt(reader, tag, "pVer", int.MinValue, int.MaxValue, 0); break;
                            case "uHsh": IndexState.UpgradeHash = KeyedSerializer.ReadBoundedInt(reader, tag, "uHsh", int.MinValue, int.MaxValue, 0); break;
                            case "hHsh": IndexState.HydroShapeHash = KeyedSerializer.ReadBoundedInt(reader, tag, "hHsh", int.MinValue, int.MaxValue, 0); break;
                            case "chan": IndexState.Channel = KeyedSerializer.ReadEnumByte<TReader, CapacityChannel>(reader, tag, "chan", CapacityChannel.GridProducer); break;
                            // 'Applied' intentionally not read — runtime-only, default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(PowerIndexIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
