using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Lifecycle phase of a discrete PSYOPS attack.
    /// launched → <see cref="InFlight"/> (interception window open) → <see cref="Landed"/>
    /// (stratum-damage hook) → <see cref="Expired"/> (decayed; entity destroyed).
    /// </summary>
    public enum PsyOpsAttackPhase : byte
    {
        /// <summary>In transit toward its target stratum; PvE interception window is open.</summary>
        InFlight = 0,

        /// <summary>Reached the target stratum. Stratum damage is applied at this point; here it is only flagged + logged.</summary>
        Landed = 1,

        /// <summary>Decayed after landing; the launcher destroys the entity on the next tick.</summary>
        Expired = 2
    }

    /// <summary>
    /// A single in-flight enemy PSYOPS attack — a discrete "drone-time" object spawned on top
    /// of the continuous reactive IPSO field (Tier 1) by <c>PsyOpsLaunchSystem</c>. Unlike a
    /// physical wave threat it has no world position, projectile or AA intercept: the
    /// "interception window" is a cognitive concept (future player counter-operations), not a
    /// physical shoot-down. Each attack lives on its own dedicated mod entity carrying only this
    /// component.
    ///
    /// Lives in <c>Core</c> (Axiom 5): the Cognitive launcher writes it, later cognitive phases
    /// (damage, UI) read it — shared truth, no domain import.
    ///
    /// PERSISTENT: an attack caught in flight by a save must survive the round-trip (the physical
    /// in-flight precedent — <see cref="Threats.OutboundStrikePayload"/>). Because the component
    /// implements <see cref="ISerializable"/> and the entity carries no <c>Temp</c>/<c>Deleted</c>,
    /// vanilla's <c>SerializerSystem</c> persists the entity automatically (its query is
    /// <c>Any = serializable components, None = Temp/Deleted</c>), so no separate in-flight list is
    /// kept on the launcher — the attack entities round-trip themselves. The launcher persists only
    /// its dispatcher RNG. Times are absolute <c>TotalGameHours</c> stamps (monotonic, load-safe),
    /// never <c>ElapsedTime</c> (which resets on load). Leftover in-flight attacks from a previous
    /// city are purged at the load boundary by <c>ModEntityCleanupSystem.OnGamePreload</c> (parity
    /// with the other bare mod-sidecars, e.g. <see cref="PsyImpact.HouseholdPsyState"/>) so an
    /// instance-reuse load does not carry stale attacks into the new city; the save's own attacks
    /// are restored by deserialization after that purge.
    /// </summary>
    // SERIALIZE-LOCK: the entity carries only this ISerializable component and no Temp/Deleted, so vanilla SerializerSystem round-trips in-flight attacks by itself with no launcher-side list. Adding Temp/PrefabRef/a tool-preview component, or creating the entity on a tool-ECB, silently drops save-persistence of in-flight attacks (they vanish across save/load).
    public struct PsyOpsAttack : IComponentData, ISerializable
    {
        /// <summary>Carrier/flavour of this attack.</summary>
        public PsyOpsAttackType Type;

        /// <summary>Target social stratum (the seam this attack is aimed at).</summary>
        public SocialStratum Target;

        /// <summary>Attack strength, 0-1 (wave-scaled at launch). This becomes stratum damage downstream.</summary>
        public float Intensity;

        /// <summary>Absolute game-time stamp (TotalGameHours) when the attack was launched.</summary>
        public float LaunchTimeHours;

        /// <summary>Absolute game-time stamp (TotalGameHours) when the attack reaches its target (ETA → Landed).</summary>
        public float LandTimeHours;

        /// <summary>
        /// Absolute game-time stamp (TotalGameHours) until which PvE interception is possible.
        /// PvE-only — it must not feed the PvP outcome.
        /// </summary>
        public float InterceptWindowEndHours;

        /// <summary>Absolute game-time stamp (TotalGameHours) when a landed attack decays and is destroyed.</summary>
        public float ExpiryTimeHours;

        /// <summary>Lifecycle phase.</summary>
        public PsyOpsAttackPhase Phase;

        /// <summary>
        /// Resolution seed, FROZEN at launch (drawn from the launcher's persisted dispatcher RNG).
        /// Serialized so an attack caught in flight by a save replays the same resolution on load,
        /// and so a future server fed the same seed recomputes the identical outcome for PvP.
        /// </summary>
        public uint Seed;

        /// <summary>Wave number at launch (diagnostics — gated-wave attribution in logs).</summary>
        public int Wave;

        /// <summary>
        /// True for the raid's DOMINANT contact — the gap-biased main threat the player is meant to
        /// read and counter (i==0 in the raid composition). Off for the secondary off-type leak
        /// contacts. Serialized so a telegraph caught in flight by a save keeps its dominance flag
        /// (the live-response "best response" read keys on it).
        /// </summary>
        public bool IsDominant;

        // ── Rumor spread — serialized so a spreading rumor caught mid-flight by a
        //    save resumes its contagion correctly. Only meaningful for <see cref="PsyOpsAttackType.Rumor"/>.
        /// <summary>
        /// Secondary (wealth-adjacent) stratum a Rumor has spread into; <see cref="SocialStratum.Unknown"/>
        /// until the spread window elapses uncontained. Once set, the landed attack emits to BOTH strata.
        /// </summary>
        public SocialStratum SecondaryTarget;

        /// <summary>True once the Rumor has spread to <see cref="SecondaryTarget"/> (one-shot latch).</summary>
        public bool HasSpread;

        /// <summary>
        /// Absolute game-time stamp (TotalGameHours) after which an uncontained Rumor spreads to the
        /// adjacent stratum. 0 = not a spreading rumor (Propaganda/FakeVideo) or no deadline set.
        /// </summary>
        public float SpreadDeadlineHours;

        // ── Land snapshot — the historical "what hit you" verdict, FROZEN at the
        //    InFlight→Landed transition by the launcher (the component's one writer). The UI reads
        //    these for a landed contact instead of recomputing under the CURRENT hero posture, so a
        //    speaker switch after the strike can never rewrite the past. Serialized — a landed attack
        //    survives save/load and its verdict must survive with it.
        /// <summary>RPS shield of the active speaker at the moment of landing (0 when no hero was active).</summary>
        public float LandedHeroShield;

        /// <summary>Strong-branch verdict at the moment of landing (<c>shield &gt; floor + eps</c>) — "blunted".</summary>
        public bool LandedBlunted;

        /// <summary><c>(int)HeroArchetype</c> of the speaker at the moment of landing;
        /// -1 = no hero was active, or the attack has not landed yet.</summary>
        public int LandedByArchetype;

        private static readonly LogContext Log = new("PsyOpsAttack");

        // Keyed format — new fields (tgt2/spd/sdl, land snapshot lhs/lbl/lba) are read by key and
        // default in older saves, so no version bump is needed (CIVIC521; beta saves are disposable).
        // A landed attack from a pre-snapshot save degrades to "landed clean, by nobody" (-1).
        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
            // 0 would read as HeroArchetype.Voice — the sentinel for "no hero / not landed" is -1.
            LandedByArchetype = -1;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                // Inert-default guard (mirrors OutboundStrikePayload): a dedicated attack entity
                // always carries a non-zero seed, positive intensity and a positive land time, so
                // an all-inert shape can only be a default-constructed component — write an empty
                // block the keyed reader rebuilds from SetDefaults().
                if (Seed == 0 && Intensity <= 0f && LandTimeHours <= 0f)
                {
                    KeyedSerializer.WriteBlockHeader(writer, 0);
                    return;
                }

                KeyedSerializer.WriteBlockHeader(writer, 17);
                KeyedSerializer.WriteEnumByteField(writer, "type", (byte)Type);
                KeyedSerializer.WriteEnumByteField(writer, "tgt", (byte)Target);
                KeyedSerializer.WriteField(writer, "int", Intensity);
                KeyedSerializer.WriteField(writer, "lt", LaunchTimeHours);
                KeyedSerializer.WriteField(writer, "land", LandTimeHours);
                KeyedSerializer.WriteField(writer, "iw", InterceptWindowEndHours);
                KeyedSerializer.WriteField(writer, "exp", ExpiryTimeHours);
                KeyedSerializer.WriteEnumByteField(writer, "ph", (byte)Phase);
                // uint seed stored bit-preserving through the int codec (no uint field type); the
                // reader reverses with the same reinterpret.
                KeyedSerializer.WriteField(writer, "seed", unchecked((int)Seed));
                KeyedSerializer.WriteField(writer, "wave", Wave);
                KeyedSerializer.WriteField(writer, "dom", IsDominant);
                // Rumor spread state.
                KeyedSerializer.WriteEnumByteField(writer, "tgt2", (byte)SecondaryTarget);
                KeyedSerializer.WriteField(writer, "spd", HasSpread);
                KeyedSerializer.WriteField(writer, "sdl", SpreadDeadlineHours);
                // Land snapshot (frozen verdict of the strike).
                KeyedSerializer.WriteField(writer, "lhs", LandedHeroShield);
                KeyedSerializer.WriteField(writer, "lbl", LandedBlunted);
                KeyedSerializer.WriteField(writer, "lba", LandedByArchetype);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PsyOpsAttack)))
            { return; }
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
                            case "type": Type = KeyedSerializer.ReadEnumByte<TReader, PsyOpsAttackType>(reader, tag, "type", PsyOpsAttackType.Propaganda); break;
                            case "tgt": Target = KeyedSerializer.ReadEnumByte<TReader, SocialStratum>(reader, tag, "tgt", SocialStratum.Unknown); break;
                            case "int": Intensity = KeyedSerializer.ReadSafeFloat(reader, tag, "int", 0f, 1f, 0f); break;
                            case "lt": LaunchTimeHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lt", 0f); break;
                            case "land": LandTimeHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "land", 0f); break;
                            case "iw": InterceptWindowEndHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "iw", 0f); break;
                            case "exp": ExpiryTimeHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "exp", 0f); break;
                            case "ph": Phase = KeyedSerializer.ReadEnumByte<TReader, PsyOpsAttackPhase>(reader, tag, "ph", PsyOpsAttackPhase.InFlight); break;
                            case "seed": Seed = unchecked((uint)KeyedSerializer.ReadInt(reader, tag, "seed", 0)); break;
                            case "wave": Wave = KeyedSerializer.ReadInt(reader, tag, "wave", 0); break;
                            case "dom": IsDominant = KeyedSerializer.ReadBool(reader, tag, "dom", false); break;
                            case "tgt2": SecondaryTarget = KeyedSerializer.ReadEnumByte<TReader, SocialStratum>(reader, tag, "tgt2", SocialStratum.Unknown); break;
                            case "spd": HasSpread = KeyedSerializer.ReadBool(reader, tag, "spd", false); break;
                            case "sdl": SpreadDeadlineHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "sdl", 0f); break;
                            case "lhs": LandedHeroShield = KeyedSerializer.ReadSafeFloat(reader, tag, "lhs", 0f, 1f, 0f); break;
                            case "lbl": LandedBlunted = KeyedSerializer.ReadBool(reader, tag, "lbl", false); break;
                            case "lba": LandedByArchetype = KeyedSerializer.ReadInt(reader, tag, "lba", -1); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize {nameof(PsyOpsAttack)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
