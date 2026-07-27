using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.PsyImpact
{
    internal static class PsyHouseholdLinkLog
    {
        public static readonly LogContext Log = new("PsyHouseholdLink");
    }

    /// <summary>
    /// IDENTITY of a psy sidecar: the link to its vanilla Household entity.
    ///
    /// Lives in its OWN component (not inside <see cref="HouseholdPsyState"/>) so that
    /// readers of the link alone — most importantly the orphan scan in
    /// ModEntityCleanupSystem — do not register a dependency on HouseholdPsyState and
    /// therefore never wait on the psy resolver job chains (ResolveHouseholdPsyJob /
    /// ResolveWellbeingJob write HouseholdPsyState every frame; the link is written once
    /// at sidecar creation and never mutated afterwards).
    ///
    /// Written ONLY by FillPsyModEntitiesJob at sidecar creation. Never mutated after.
    /// </summary>
    public struct PsyHouseholdLink : IComponentData, ISerializable
    {
        /// <summary>Vanilla Household entity Index (Axiom 11: never an Entity field).</summary>
        public int HouseholdIndex;

        /// <summary>Vanilla Household entity Version.</summary>
        public int HouseholdVersion;

        /// <summary>Reconstruct vanilla Household Entity from Index+Version.</summary>
        public readonly Entity GetHouseholdEntity() =>
            new Entity { Index = HouseholdIndex, Version = HouseholdVersion };

        /// <summary>
        /// 0:0 — the "never filled / lost household" sentinel; the orphan scan in
        /// ModEntityCleanupSystem collects sidecars whose link deserialized to this.
        /// </summary>
        public void SetDefaults()
        {
            HouseholdIndex = 0;
            HouseholdVersion = 0;
        }

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                // Entity tag rides the engine m_EntityTable remap — raw idx/ver
                // ints do NOT survive load (recycled slot → foreign/lost household
                // → orphaned psy-state + reconcile duplicates).
                KeyedSerializer.WriteEntityField(writer, "hh", GetHouseholdEntity());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PsyHouseholdLink)))
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
                            case "hh": { var e = KeyedSerializer.ReadEntity(reader, tag, "hh"); HouseholdIndex = e.Index; HouseholdVersion = e.Version; break; }
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PsyHouseholdLinkLog.Log.Error($"Deserialize {nameof(PsyHouseholdLink)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
