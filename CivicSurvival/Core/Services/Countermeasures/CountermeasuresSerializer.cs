using System.Text;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services.Countermeasures
{
    /// <summary>
    /// Pure serialization helpers for the 4 countermeasures components.
    /// Keyed format for forward compatibility (unknown fields skipped on load).
    ///
    /// Writer: CountermeasuresUpdateSystem.Serialize/Deserialize (sole caller)
    /// </summary>
    public static class CountermeasuresSerializer
    {
        private static readonly LogContext Log = new("CountermeasuresSerializer");
        private const int LAST_CHOICE_UTF8_MAX_BYTES = 124;
        private const int JOURNALIST_UTF8_MAX_BYTES = 60;

        public static void WriteAll<TWriter>(
            TWriter writer,
            in CountermeasuresCoreFsm core,
            in CmInvestigationState inv,
            in CmPoliceState police,
            in CmProtestState protest)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 27);

            // Main FSM state (10)
            KeyedSerializer.WriteEnumByteField(writer, "phase", (byte)core.CurrentPhase);
            KeyedSerializer.WriteField(writer, "corr", core.CorruptionScore);
            KeyedSerializer.WriteField(writer, "tCorr", core.TargetCorruption);
            KeyedSerializer.WriteField(writer, "heat", core.Heat);
            KeyedSerializer.WriteField(writer, "charges", core.ChargesCount);
            KeyedSerializer.WriteField(writer, "lastChoice", core.LastChoiceResult.ToString());
            KeyedSerializer.WriteField(writer, "gHour", core.GameHour);
            KeyedSerializer.WriteField(writer, "nxtEvt", core.NextEventHour);
            KeyedSerializer.WriteField(writer, "arrSeized", core.ArrestedAssetsSeized);
            KeyedSerializer.WriteField(writer, "arrWalletAfter", core.ArrestedWalletAfter);

            // Investigation state (8)
            KeyedSerializer.WriteField(writer, "invActive", inv.Active);
            KeyedSerializer.WriteField(writer, "invProg", inv.Progress);
            KeyedSerializer.WriteField(writer, "invMile", inv.LastMilestone);
            KeyedSerializer.WriteField(writer, "invStart", inv.StartHour);
            KeyedSerializer.WriteField(writer, "invJourn", inv.Journalist.ToString());
            KeyedSerializer.WriteField(writer, "invBribe", inv.BribeCost);
            KeyedSerializer.WriteField(writer, "invWait", inv.WaitingForChoice);
            KeyedSerializer.WriteField(writer, "invRng", unchecked((int)inv.RngState));

            // Police state (5)
            KeyedSerializer.WriteField(writer, "polActive", police.Active);
            KeyedSerializer.WriteField(writer, "polStart", police.StartHour);
            KeyedSerializer.WriteField(writer, "polWait", police.WaitingForChoice);
            KeyedSerializer.WriteField(writer, "polCharges", police.ChargesCount);
            KeyedSerializer.WriteField(writer, "polRng", unchecked((int)police.RngState));

            // Protest state (4)
            KeyedSerializer.WriteField(writer, "prtActive", protest.ActiveProtests);
            KeyedSerializer.WriteField(writer, "prtCool", protest.CooldownSeconds);
            KeyedSerializer.WriteField(writer, "prtDecay", protest.DecaySeconds);
            KeyedSerializer.WriteField(writer, "prtRng", unchecked((int)protest.RngState));

        }

        public static void ReadAll<TReader>(
            TReader reader,
            out CountermeasuresCoreFsm core,
            out CmInvestigationState inv,
            out CmPoliceState police,
            out CmProtestState protest)
            where TReader : IReader
        {
            core = new CountermeasuresCoreFsm();
            inv = new CmInvestigationState();
            police = new CmPoliceState();
            protest = new CmProtestState();

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    // Core FSM
                    case "phase": core.CurrentPhase = KeyedSerializer.ReadEnumByte<TReader, CountermeasuresPhase>(reader, tag, "phase", CountermeasuresPhase.Idle); break;
                    case "corr": core.CorruptionScore = KeyedSerializer.ReadSafeFloat(reader, tag, "corr", 0f, 100f, 0f); break;
                    case "tCorr": core.TargetCorruption = KeyedSerializer.ReadSafeFloat(reader, tag, "tCorr", 0f, 100f, 0f); break;
                    case "heat": core.Heat = KeyedSerializer.ReadSafeFloat(reader, tag, "heat", 0f, 100f, 0f); break;
                    case "charges": core.ChargesCount = KeyedSerializer.ReadClampedInt(reader, tag, "charges", 0, int.MaxValue); break;
                    case "lastChoice": { string s = KeyedSerializer.ReadString(reader, tag, "lastChoice"); core.LastChoiceResult = ToFixedString128(s, "lastChoice"); } break;
                    case "gHour": core.GameHour = KeyedSerializer.ReadSafeFloat(reader, tag, "gHour", 0f, 1_000_000f, 0f); break;
                    case "nxtEvt": core.NextEventHour = KeyedSerializer.ReadSafeFloat(reader, tag, "nxtEvt", 0f, 1_000_000f, 0f); break;
                    case "arrSeized": core.ArrestedAssetsSeized = KeyedSerializer.ReadBoundedLong(reader, tag, "arrSeized", 0, long.MaxValue); break;
                    case "arrWalletAfter": core.ArrestedWalletAfter = KeyedSerializer.ReadBoundedLong(reader, tag, "arrWalletAfter", 0, long.MaxValue); break;

                    // Investigation
                    case "invActive": inv.Active = KeyedSerializer.ReadBool(reader, tag, "invActive"); break;
                    case "invProg": inv.Progress = KeyedSerializer.ReadBoundedInt(reader, tag, "invProg", 0, 100, 0); break;
                    case "invMile": inv.LastMilestone = KeyedSerializer.ReadBoundedInt(reader, tag, "invMile", 0, 100, 0); break;
                    case "invStart": inv.StartHour = KeyedSerializer.ReadSafeFloat(reader, tag, "invStart", 0f, 1_000_000f, 0f); break;
                    case "invJourn": { string s = KeyedSerializer.ReadString(reader, tag, "invJourn"); inv.Journalist = ToFixedString64(s, "invJourn"); } break;
                    case "invBribe": inv.BribeCost = KeyedSerializer.ReadBoundedInt(reader, tag, "invBribe", 0, int.MaxValue, 0); break;
                    case "invWait": inv.WaitingForChoice = KeyedSerializer.ReadBool(reader, tag, "invWait"); break;
                    case "invRng": { int v = KeyedSerializer.ReadBoundedInt(reader, tag, "invRng", int.MinValue, int.MaxValue, 0); inv.RngState = unchecked((uint)v); } break;

                    // Police
                    case "polActive": police.Active = KeyedSerializer.ReadBool(reader, tag, "polActive"); break;
                    case "polStart": police.StartHour = KeyedSerializer.ReadSafeFloat(reader, tag, "polStart", 0f, 1_000_000f, 0f); break;
                    case "polWait": police.WaitingForChoice = KeyedSerializer.ReadBool(reader, tag, "polWait"); break;
                    case "polCharges": police.ChargesCount = KeyedSerializer.ReadClampedInt(reader, tag, "polCharges", 0, int.MaxValue); break;
                    case "polRng": { int v = KeyedSerializer.ReadBoundedInt(reader, tag, "polRng", int.MinValue, int.MaxValue, 0); police.RngState = unchecked((uint)v); } break;

                    // Protest
                    case "prtActive": protest.ActiveProtests = KeyedSerializer.ReadClampedInt(reader, tag, "prtActive", 0, int.MaxValue); break;
                    case "prtCool": protest.CooldownSeconds = KeyedSerializer.ReadSafeFloat(reader, tag, "prtCool", 0f, GameRate.SECONDS_PER_DAY, 0f); break;
                    case "prtDecay": protest.DecaySeconds = KeyedSerializer.ReadSafeFloat(reader, tag, "prtDecay", 0f, GameRate.SECONDS_PER_DAY, 0f); break;
                    case "prtRng": { int v = KeyedSerializer.ReadBoundedInt(reader, tag, "prtRng", int.MinValue, int.MaxValue, 0); protest.RngState = unchecked((uint)v); } break;

                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            // Consistency: StartHour must not exceed GameHour (corrupt save → frozen investigation/police)
            // Unconditional clamp — covers Active=false corrupt saves that later activate
            inv.StartHour = math.clamp(inv.StartHour, 0f, math.max(0f, core.GameHour));
            police.StartHour = math.clamp(police.StartHour, 0f, math.max(0f, core.GameHour));

            // Ensure handler RNG states are valid
            if (inv.RngState == 0)
            {
                Log.Warn("[Countermeasures] Investigation RngState was 0 on load — recovered to canonical seed");
                inv.RngState = 0x494E5645u; // "INVE"
            }
            if (police.RngState == 0)
            {
                Log.Warn("[Countermeasures] Police RngState was 0 on load — recovered to canonical seed");
                police.RngState = 0x504F4C49u; // "POLI"
            }
            if (protest.RngState == 0)
            {
                Log.Warn("[Countermeasures] Protest RngState was 0 on load — recovered to canonical seed");
                protest.RngState = 0x50524F54u; // "PROT"
            }
        }

        private static FixedString128Bytes ToFixedString128(string? value, string fieldName)
            => new FixedString128Bytes(TrimUtf8(value, LAST_CHOICE_UTF8_MAX_BYTES, fieldName));

        private static FixedString64Bytes ToFixedString64(string? value, string fieldName)
            => new FixedString64Bytes(TrimUtf8(value, JOURNALIST_UTF8_MAX_BYTES, fieldName));

        private static string TrimUtf8(string? value, int maxBytes, string fieldName)
        {
            string result = value ?? "";
            if (Encoding.UTF8.GetByteCount(result) <= maxBytes)
                return result;

            while (result.Length > 0 && Encoding.UTF8.GetByteCount(result) > maxBytes)
                result = result.Substring(0, result.Length - 1);

            Log.Warn($"[Countermeasures] {fieldName} exceeded FixedString capacity on load — truncated");
            return result;
        }
    }
}
