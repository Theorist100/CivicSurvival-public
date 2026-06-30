using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Axis-effect payload carried by a player's outbound counter-strike projectile
    /// (paired with an enabled <see cref="PlayerOutboundThreat"/>). The launch phase
    /// resolves <see cref="Axis"/> (which enemy axis the strike lowers) and
    /// <see cref="Damage"/> (how much, before the enemy's intercept roll) up front and
    /// records them here, so the deferred axis hit happens at <i>arrival</i> — when the
    /// projectile reaches the frontier — not instantly on the launch button. At arrival,
    /// <c>ThreatArrivalSystem</c> reads this payload off the projectile and emits an
    /// <see cref="OutboundArrivalSignal"/> the GridWarfare effect owner turns into a
    /// <c>ReduceAxis</c> (after the enemy's <c>InterceptChance</c> roll).
    ///
    /// Lives in <c>Core</c> (Axiom 5): the Waves launch producer writes it, the
    /// ThreatDamage arrival reader consumes it — two domains, one shared truth.
    ///
    /// PERSISTENT (C1): unlike the faction <i>tag</i> (<see cref="PlayerOutboundThreat"/>,
    /// an enableable component with no serializer that is stripped on load), this payload
    /// carries gameplay state — the axis/damage of an in-flight counter-strike and the
    /// <see cref="IsOutbound"/> fact that the projectile is the player's own. It is
    /// serialized so a counter-strike caught in flight by a save survives the round-trip:
    /// without it, the projectile would load as an inbound wave threat (faction bit gone,
    /// payload gone) and either be shot down by the player's own AA or terminalize into the
    /// city. <c>ThreatLoadRenderReinitSystem</c> reads <see cref="IsOutbound"/> on load and
    /// re-enables <see cref="PlayerOutboundThreat"/> before the first AA scan, restoring the
    /// projectile's side and the axis effect it lands on arrival.
    ///
    /// The component lives on the shared drone/ballistic render archetype, so vanilla's
    /// per-entity SerializerSystem persists it on EVERY restored threat (inbound waves
    /// included). Inbound threats carry <see cref="IsOutbound"/> == false and a zero
    /// <see cref="Damage"/> — the faction gate routes them to city terminalization and never
    /// reads the axis channel, so the inert default is harmless. <see cref="IsOutbound"/> is
    /// the authoritative outbound discriminator (not <c>Damage &gt; 0</c>): a 0-damage
    /// counter-strike is still an outbound projectile and must keep its side on load.
    /// </summary>
    public struct OutboundStrikePayload : IComponentData, ISerializable
    {
        /// <summary>Which enemy axis this strike lowers (Kinetic→Physical, Cyber→Digital, Psyops→Social).</summary>
        public AttackCategory Axis;

        /// <summary>Axis reduction applied at arrival, before the enemy's intercept roll.</summary>
        public float Damage;

        /// <summary>
        /// True for a player counter-strike projectile (paired with an enabled
        /// <see cref="PlayerOutboundThreat"/> at spawn), false for an inbound wave threat.
        /// Persisted so a mid-flight save/load can re-assert the stripped faction tag — the
        /// enableable tag itself has no serializer, so this serialized fact is the only thing
        /// that survives to tell <c>ThreatLoadRenderReinitSystem</c> which restored projectiles
        /// are the player's own.
        /// </summary>
        public bool IsOutbound;

        /// <summary>
        /// Intercept-roll seed, FROZEN at launch (drawn deterministically from the operation's
        /// stable identity + game time by <c>EnemyOperationEffectSystem</c>, NOT from a runtime
        /// <c>Random</c>). It rides the projectile to arrival, where <c>StrikeResolver.Resolve</c>
        /// turns it into the enemy's intercept verdict. Serialized so a counter-strike caught in
        /// flight by a save replays the SAME verdict on load (the session RNG it replaced did not),
        /// and so a future server fed the same launch seed recomputes the identical outcome for an
        /// offline defender (Wave3Arena Phase-40). Inbound waves carry a 0 seed and never read it
        /// (the faction gate routes them to city terminalization, not the axis channel).
        /// </summary>
        public uint Seed;

        private static readonly LogContext Log = new("OutboundStrikePayload");

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                // Save-bloat guard: this component sits on the shared threat render archetype, so
                // vanilla persists it on EVERY in-flight threat — and inbound waves (the vast
                // majority) carry the inert default. Write an empty (zero-field) block for them; the
                // keyed reader rebuilds the exact default from SetDefaults() when fc == 0. Only a
                // real outbound payload writes the four fields. Discriminate on the full inert shape,
                // not IsOutbound alone, so a 0-damage outbound strike still persists its side + seed.
                if (!IsOutbound && Damage <= 0f && Seed == 0)
                {
                    KeyedSerializer.WriteBlockHeader(writer, 0);
                    return;
                }

                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteEnumByteField(writer, "axis", (byte)Axis);
                KeyedSerializer.WriteField(writer, "dmg", Damage);
                KeyedSerializer.WriteField(writer, "outb", IsOutbound);
                // uint seed stored bit-preserving through the int codec (no uint field type); the
                // reader reverses with the same reinterpret. The launch-frozen value survives the
                // round-trip so the arrival intercept verdict is identical after load.
                KeyedSerializer.WriteField(writer, "seed", unchecked((int)Seed));
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(OutboundStrikePayload)))
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
                            case "axis": Axis = KeyedSerializer.ReadEnumByte<TReader, AttackCategory>(reader, tag, "axis", AttackCategory.Kinetic); break;
                            case "dmg": Damage = KeyedSerializer.ReadSafeFloat(reader, tag, "dmg", 0f, 100000f, 0f); break;
                            case "outb": IsOutbound = KeyedSerializer.ReadBool(reader, tag, "outb"); break;
                            case "seed": Seed = unchecked((uint)KeyedSerializer.ReadInt(reader, tag, "seed", 0)); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize {nameof(OutboundStrikePayload)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
