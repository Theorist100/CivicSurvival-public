using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Config;
using Colossal.Serialization.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Pure value codec for the EnemyState save block — Layer 3.
    ///
    /// Persists the mirror enemy's three axes (physical/digital/social) plus its
    /// intercept defence. NO World / EntityManager / RNG / Mod state — that glue stays
    /// in EnemySimulationSystem. The keyed wire format tolerates missing keys
    /// (<c>default: Skip</c>), so a save written before the three-axis migration loads
    /// with axes defaulted to the cap (older "pressure"/"stance"/"phase" keys are skipped).
    /// </summary>
    public static class EnemyStateCodec
    {
        // Persisted-axis range is the LIVE balance config
        // (BalanceConfig.Current.GridWarfare.PressureFloor / PressureCap) — remote-tunable.
        // Reading a stale const here would silently clamp a legitimately saved axis when the
        // cap is tuned above the old default. MaxAxisSanity is only a hard ceiling against
        // corrupt/garbage saves; the real bounds come from config below.
        private const float MaxAxisSanity = 200f;
        private const float DefaultRegenRate = 5f;

        public static void Write<TWriter>(in EnemyState s, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 10);
            KeyedSerializer.WriteField(writer, "physicalAxis", s.PhysicalAxis);
            KeyedSerializer.WriteField(writer, "digitalAxis", s.DigitalAxis);
            KeyedSerializer.WriteField(writer, "socialAxis", s.SocialAxis);
            KeyedSerializer.WriteField(writer, "regenRate", s.RegenRatePerHour);
            KeyedSerializer.WriteField(writer, "interceptChance", s.InterceptChance);
            // Respite + act-objective state (Phase 3.6.3). Absolute game-hour expiries (stable
            // across save/load); the objective latch + collapse counter keep the loot exactly-once.
            KeyedSerializer.WriteField(writer, "respitePhysical", s.RespiteUntilPhysical);
            KeyedSerializer.WriteField(writer, "respiteDigital", s.RespiteUntilDigital);
            KeyedSerializer.WriteField(writer, "respiteSocial", s.RespiteUntilSocial);
            KeyedSerializer.WriteField(writer, "objectiveCollapseCount", s.ObjectiveCollapseCount);
            KeyedSerializer.WriteField(writer, "objectiveClaimed", s.ObjectiveClaimed);
        }

        public static void Read<TReader>(TReader reader, out EnemyState state)
            where TReader : IReader
        {
            // Live balance bounds (remote-tunable). Read ceiling = max(sanity, cap) so a
            // tuned-up cap is not truncated before the final clamp below.
            var gw = BalanceConfig.Current.GridWarfare;
            float cfgFloor = gw.PressureFloor;
            float cfgCap = gw.PressureCap;
            float readCeiling = math.max(MaxAxisSanity, cfgCap);

            // Missing axes default to the cap (full health) — matches EnemyState.Default and
            // keeps a pre-migration save (no axis keys) from loading a crippled enemy.
            float physical = cfgCap;
            float digital = cfgCap;
            float social = cfgCap;
            float regenRate = DefaultRegenRate;
            float interceptChance = EnemyState.DefaultInterceptChance;
            // Respite + objective default to "no respite / nothing claimed" — a pre-3.6.3 save
            // (no keys) loads with windows closed and the loot latch open, which is the correct
            // neutral state for a just-loaded enemy.
            float respitePhysical = 0f;
            float respiteDigital = 0f;
            float respiteSocial = 0f;
            int objectiveCollapseCount = 0;
            bool objectiveClaimed = false;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "physicalAxis": physical = KeyedSerializer.ReadSafeFloat(reader, tag, "physicalAxis", 0f, readCeiling, cfgCap); break;
                    case "digitalAxis": digital = KeyedSerializer.ReadSafeFloat(reader, tag, "digitalAxis", 0f, readCeiling, cfgCap); break;
                    case "socialAxis": social = KeyedSerializer.ReadSafeFloat(reader, tag, "socialAxis", 0f, readCeiling, cfgCap); break;
                    case "regenRate": regenRate = KeyedSerializer.ReadSafeFloat(reader, tag, "regenRate", 0f, 100f, DefaultRegenRate); break;
                    case "interceptChance": interceptChance = KeyedSerializer.ReadSafeFloat(reader, tag, "interceptChance", 0f, 1f, EnemyState.DefaultInterceptChance); break;
                    // Legacy "randomState" key (pre-StrikeResolver seeded RNG) is no longer
                    // consumed — strike rolls are seeded deterministically elsewhere. Skip for
                    // backward tolerance with saves that still carry it.
                    case "randomState": KeyedSerializer.Skip(reader, tag); break;
                    // Respite expiries are absolute game-hours (sane up to a very long session) —
                    // bound generously and let the runtime treat any past value as already expired.
                    case "respitePhysical": respitePhysical = KeyedSerializer.ReadSafeFloat(reader, tag, "respitePhysical", 0f, float.MaxValue, 0f); break;
                    case "respiteDigital": respiteDigital = KeyedSerializer.ReadSafeFloat(reader, tag, "respiteDigital", 0f, float.MaxValue, 0f); break;
                    case "respiteSocial": respiteSocial = KeyedSerializer.ReadSafeFloat(reader, tag, "respiteSocial", 0f, float.MaxValue, 0f); break;
                    case "objectiveCollapseCount": objectiveCollapseCount = KeyedSerializer.ReadBoundedInt(reader, tag, "objectiveCollapseCount", 0, int.MaxValue, 0); break;
                    case "objectiveClaimed": objectiveClaimed = KeyedSerializer.ReadBool(reader, tag, "objectiveClaimed", false); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new EnemyState
            {
                PhysicalAxis = math.clamp(physical, cfgFloor, cfgCap),
                DigitalAxis = math.clamp(digital, cfgFloor, cfgCap),
                SocialAxis = math.clamp(social, cfgFloor, cfgCap),
                RegenRatePerHour = regenRate,
                InterceptChance = math.clamp(interceptChance, 0f, 1f),
                RespiteUntilPhysical = respitePhysical,
                RespiteUntilDigital = respiteDigital,
                RespiteUntilSocial = respiteSocial,
                ObjectiveCollapseCount = objectiveCollapseCount,
                ObjectiveClaimed = objectiveClaimed
            };
        }
    }
}
