using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.ThreatDamage
{
    /// <summary>
    /// Fire-ignition intent — created by mod fire producers (ThreatDamageSystem,
    /// CounterfeitBatteryFireSystem, BackupPowerEffectsSystem,
    /// PlantWearSimulation/PlantExplosionService) in GameSimulation, consumed by
    /// <c>ModFireApplySystem</c> in ModificationEnd.
    ///
    /// Mirrors the vanilla split <c>FireSimulationSystem</c> (producer, GameSimulation,
    /// creates an Ignite event) → <c>IgniteSystem</c> (consumer, Modification4, does the
    /// <c>OnFire</c>+<c>BatchesUpdated</c> structural add). The producer only CREATES this
    /// intent entity — legal from any phase, because creating a new entity is not a
    /// structural change on the vanilla building. The archetype migration on the building
    /// (adding the render tag) is done by the consumer from ModificationEnd, the phase where
    /// the vanilla render batch pipeline expects it (<c>RequiredBatchesSystem</c>,
    /// <c>PreCullingSystem</c>, <c>BatchInstanceSystem</c> all run later in the same
    /// MainLoop). Doing the structural add from GameSimulation (LateUpdate, end of frame)
    /// instead lands it out of phase with the render pass and can crash a vanilla Burst batch
    /// job on a null chunk pointer.
    ///
    /// Not <c>Game.Events.Ignite</c>: the mod must not construct vanilla Ignite entities
    /// (analyzer <c>CIVIC474</c>), so this is the mod's own intent carrying the same
    /// producer/consumer split. The consumer builds a real <c>OnFire.m_Event</c> from the
    /// vanilla fire <c>FireData</c>-prefab so vanilla <c>FireSimulationSystem</c> drives
    /// escalation/spread (and building damage) off this fire.
    ///
    /// Serializable: a save taken in the 1-frame window between producer (frame N) and
    /// consumer (frame N+1) keeps the intent, and the consumer reprocesses it after load —
    /// the fire is neither lost nor leaked as an orphan entity.
    /// </summary>
    public struct ModFireIntent : IComponentData, ISerializable
    {
        /// <summary>Target vanilla entity (Index+Version — Axiom 11, no Entity fields).
        /// A building when <see cref="Kind"/> is <c>Building</c>, a wild tree when
        /// <c>WildTree</c>; <see cref="BuildingRef"/> is mechanically an Index+Version
        /// holder, reused here for both.</summary>
        public BuildingRef Target;

        /// <summary>Which vanilla fire-event prefab the consumer backs this fire with
        /// (building vs wild-tree). Drives forest-fire spread params for tree targets.</summary>
        public FireTargetKind Kind;

        /// <summary>Fire intensity written into <c>OnFire.m_Intensity</c>.</summary>
        public float Intensity;

        /// <summary>When true, an already-burning building has its fire merged (intensity
        /// raised if the new value is strictly greater, existing <c>m_Event</c> preserved);
        /// when false, an already-burning building is left untouched.</summary>
        public bool AllowExistingFire;

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
        private const float MAX_INTENSITY = 100f;
        private const float DEFAULT_INTENSITY = 0.5f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteEntityField(writer, "tgt", Target.ToEntity());
                KeyedSerializer.WriteField(writer, "int", Intensity);
                KeyedSerializer.WriteField(writer, "aef", AllowExistingFire);
                KeyedSerializer.WriteField(writer, "knd", (int)Kind);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ModFireIntent)))
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
                            case "tgt": Target = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "tgt")); break;
                            case "int": Intensity = KeyedSerializer.ReadSafeFloat(reader, tag, "int", 0f, MAX_INTENSITY, DEFAULT_INTENSITY); break;
                            case "aef": AllowExistingFire = KeyedSerializer.ReadBool(reader, tag, "aef"); break;
                            // Explicit map (no unchecked int→enum cast): any unknown value
                            // degrades to Building, the safe default.
                            case "knd": Kind = KeyedSerializer.ReadInt(reader, tag, "knd", (int)FireTargetKind.Building) == (int)FireTargetKind.WildTree
                                ? FireTargetKind.WildTree : FireTargetKind.Building; break;
                            // 'Applied' intentionally not read — runtime-only, default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(ModFireIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
