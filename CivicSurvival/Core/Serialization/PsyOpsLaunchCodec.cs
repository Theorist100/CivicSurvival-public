using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted launcher state for <c>PsyOpsLaunchSystem</c>: the dispatcher RNG seed and the
    /// wealthy bank-run interval anchor. The in-flight attacks themselves persist as their own
    /// <c>PsyOpsAttack</c> entities (vanilla serializes them), so no attack list is stored here.
    ///
    /// The bank-run anchor is persisted (absolute TotalGameHours) so the drain cadence survives
    /// reload: re-anchoring it to the load moment would let a player save-scum the periodic
    /// capital-flight drain by reloading just before each deadline.
    /// </summary>
    public readonly struct PsyOpsLaunchPersistState
    {
        public PsyOpsLaunchPersistState(ulong randomState, float lastBankRunHour, bool bankRunInitialized)
        {
            RandomState = randomState;
            LastBankRunHour = lastBankRunHour;
            BankRunInitialized = bankRunInitialized;
        }

        public ulong RandomState { get; }

        /// <summary>Absolute TotalGameHours of the last bank-run interval anchor.</summary>
        public float LastBankRunHour { get; }

        /// <summary>Whether the bank-run interval has been anchored at least once (war reached).</summary>
        public bool BankRunInitialized { get; }
    }

    public static class PsyOpsLaunchCodec
    {
        public static void Write<TWriter>(in PsyOpsLaunchPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "rng", unchecked((long)state.RandomState));
            KeyedSerializer.WriteField(writer, "lbrh", state.LastBankRunHour);
            KeyedSerializer.WriteField(writer, "bri", state.BankRunInitialized);
        }

        public static void Read<TReader>(TReader reader, out PsyOpsLaunchPersistState state)
            where TReader : IReader
        {
            ulong randomState = 0;
            float lastBankRunHour = 0f;
            bool bankRunInitialized = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "rng":
                        if (KeyedSerializer.ExpectTag(reader, tag, TypeTag.I64, "rng"))
                        {
                            reader.Read(out long rawState);
                            randomState = unchecked((ulong)rawState);
                        }
                        break;
                    case "lbrh":
                        lastBankRunHour = KeyedSerializer.ReadFloat(reader, tag, "lbrh");
                        break;
                    case "bri":
                        bankRunInitialized = KeyedSerializer.ReadBool(reader, tag, "bri");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new PsyOpsLaunchPersistState(randomState, lastBankRunHour, bankRunInitialized);
        }
    }
}
