using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.PsyImpact
{
    internal static class HouseholdPsyStateLog
    {
        public static readonly LogContext Log = new("HouseholdPsyState");
    }

    /// <summary>
    /// CONSOLIDATED: All psychological state for a household in one component.
    /// Lives on a SEPARATE mod entity (not on the vanilla Household entity).
    /// References vanilla household via HouseholdIndex/HouseholdVersion.
    /// Single Writer: MentalHealthResolverSystem
    ///
    /// Fields grouped by lifecycle:
    /// - IDENTITY (2): HouseholdIndex/Version — link to vanilla Household entity
    /// - PERSISTENT (4): Saved to file, survive reload
    /// - TRANSIENT (9): Reset to 0 every frame end
    /// - CACHED (6): Recalculated periodically (~5-30 sec) or on resolver slot update
    /// </summary>
    public struct HouseholdPsyState : IComponentData, ISerializable
    {
        // ════════════════════════════════════════════════════════════════
        // IDENTITY: Link to vanilla Household entity (Index+Version, never Entity field!)
        // ════════════════════════════════════════════════════════════════

        /// <summary>Vanilla Household entity Index</summary>
        public int HouseholdIndex;

        /// <summary>Vanilla Household entity Version</summary>
        public int HouseholdVersion;

        /// <summary>Reconstruct vanilla Household Entity from Index+Version.</summary>
        public readonly Entity GetHouseholdEntity() =>
            new Entity { Index = HouseholdIndex, Version = HouseholdVersion };

        // ════════════════════════════════════════════════════════════════
        // PERSISTENT: Saved to file
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Trauma level (0-1). Accumulated from danger exposure.
        /// Accumulated from danger exposure, decays naturally.
        /// </summary>
        public float Trauma;

        /// <summary>
        /// Blackout duration in GAME HOURS (0+).
        /// 0-4h = patience zone, 4h+ = panic zone (10x decay).
        /// Resets when power restored.
        /// </summary>
        public float BlackoutHours;

        /// <summary>
        /// Recovery inertia (0-1). Prevents instant wellbeing jump.
        /// "People are still scared" - decays slowly.
        /// </summary>
        public float RecoveryInertia;

        /// <summary>
        /// Cognitive infection level (0-1).
        /// 0 = loyal/resistant, 1 = fully compromised.
        /// </summary>
        public float InfectionLevel;

        // ════════════════════════════════════════════════════════════════
        // TRANSIENT: Written by MHR every 4 frames, reset by PsyTransientResetSystem.
        //
        // ⚠ PROTOCOL: These fields are ONLY valid on MHR fire frames.
        // Between fire frames they retain STALE values from the last fire.
        // Any consumer MUST check MentalHealthResolverSystem.DidFire (or use
        // a latch like CogAgg's m_DidFireLatch) before reading.
        // Writer: ResolveHouseholdPsyJob (inside MHR)
        // Consumers: CognitiveStatsAggregatorSystem (latch), PsyTransientResetSystem (guard)
        // Enforced by: CIVIC266
        // ════════════════════════════════════════════════════════════════

        /// <summary>Blackout stress this frame (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Pressure_Blackout;

        /// <summary>Neighbor envy stress this frame (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Pressure_Envy;

        /// <summary>Impact stress this frame (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Pressure_Impact;

        /// <summary>Enemy internet propaganda exposure (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Exposure_EnemyInternet;

        /// <summary>Enemy IPSO influence exposure (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Exposure_EnemyIPSO;

        /// <summary>State media defense (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Exposure_StateMedia;

        /// <summary>Counter-operations defense (0-1). TRANSIENT — read only when DidFire.</summary>
        public float Exposure_CounterOps;

        // ════════════════════════════════════════════════════════════════
        // CACHED: Recalculated periodically
        // ════════════════════════════════════════════════════════════════

        /// <summary>Education-based resistance (0-0.8)</summary>
        public float Resistance_Value;

        /// <summary>Elapsed game time (seconds) when resistance was last calculated</summary>
        public float Resistance_LastUpdateTime;

        /// <summary>District index for penalty lookup (-1 = unresolved)</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictLink_Index;
#pragma warning restore CIVIC262

        /// <summary>Elapsed game time (seconds) when district link was last updated</summary>
        public float DistrictLink_LastUpdateTime;

        /// <summary>Last resolver slot observed non-zero neighbor envy pressure.</summary>
        public bool HasEnvyPressure;

        /// <summary>Last resolver slot observed non-zero impact pressure.</summary>
        public bool HasImpactPressure;

        // ════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════


        /// <summary>Reset all transient fields to 0 (call at frame end)</summary>
        public void ResetTransients()
        {
            Pressure_Blackout = 0f;
            Pressure_Envy = 0f;
            Pressure_Impact = 0f;
            Exposure_EnemyInternet = 0f;
            Exposure_EnemyIPSO = 0f;
            Exposure_StateMedia = 0f;
            Exposure_CounterOps = 0f;
        }

        // ════════════════════════════════════════════════════════════════
        // SERIALIZATION (identity + persistent fields)
        // ════════════════════════════════════════════════════════════════

        private const byte SAVE_VERSION = 1;
        private const float MAX_RESISTANCE = 0.8f;
        private const float MAX_BLACKOUT_HOURS = 100_000f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                // Entity tag rides the engine m_EntityTable remap — raw idx/ver
                // ints do NOT survive load (recycled slot → foreign/lost household
                // → orphaned psy-state + reconcile duplicates).
                KeyedSerializer.WriteEntityField(writer, "hh", GetHouseholdEntity());
                KeyedSerializer.WriteField(writer, "trm", Trauma);
                KeyedSerializer.WriteField(writer, "bkH", BlackoutHours);
                KeyedSerializer.WriteField(writer, "rInr", RecoveryInertia);
                KeyedSerializer.WriteField(writer, "inf", InfectionLevel);
                KeyedSerializer.WriteField(writer, "rTime", Resistance_LastUpdateTime);
                // M65 FIX: Persist cached resistance so the 30s recalc window starts powered (not 0).
                KeyedSerializer.WriteField(writer, "rVal", Resistance_Value);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(HouseholdPsyState)))
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
                            case "trm": Trauma = KeyedSerializer.ReadSafeFloat(reader, tag, "trm", 0f, 1f, 0f); break;
                            case "bkH": BlackoutHours = KeyedSerializer.ReadSafeFloat(reader, tag, "bkH", 0f, MAX_BLACKOUT_HOURS, 0f); break;
                            case "rInr": RecoveryInertia = KeyedSerializer.ReadSafeFloat(reader, tag, "rInr", 0f, 1f, 0f); break;
                            case "inf": InfectionLevel = KeyedSerializer.ReadSafeFloat(reader, tag, "inf", 0f, 1f, 0f); break;
                            case "rTime": Resistance_LastUpdateTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "rTime", float.MinValue); break;
                            case "rVal": Resistance_Value = KeyedSerializer.ReadSafeFloat(reader, tag, "rVal", 0f, MAX_RESISTANCE, 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                    DistrictLink_Index = -1;
                    HasEnvyPressure = false;
                    HasImpactPressure = false;
                    // Use 0f sentinel (not MinValue) — ElapsedTime ≈ 0 on load, so 0 - 0 < 5 = no spike.
                    // District link refreshes naturally within 5 real seconds after load.
                    DistrictLink_LastUpdateTime = 0f;
                }
            }
            catch (System.Exception ex)
            {
                HouseholdPsyStateLog.Log.Error($"Deserialize {nameof(HouseholdPsyState)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults()
        {
            this = default;
            DistrictLink_Index = -1;
            // Force immediate recalculation on first tick after load/new-game.
            // float.MinValue ensures (currentTime - lastTime >= interval) is always true.
            // Successful deserialize intentionally uses 0f for DistrictLink_LastUpdateTime
            // to avoid a load spike; defaults need immediate first-tick binding.
            Resistance_LastUpdateTime = float.MinValue;
            DistrictLink_LastUpdateTime = float.MinValue;
        }
    }
}


