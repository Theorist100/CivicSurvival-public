using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// Credit kind reserved during AA placement detection.
    /// Used by pipeline to return credit on reject.
    /// </summary>
    public enum AAPlacementCreditKind : byte
    {
        None = 0,
        Heritage = 1,
        DonorPatriot = 2
    }

    public enum AAPlacementTerminalReason : byte
    {
        None = 0,
        Applied = 1,
        BudgetFailed = 2,
        CreditFailed = 3,
        BuildingMissingBeforeApply = 4,
        DuplicateBuilding = 5,
        PlacementFailed = 6
    }

    /// <summary>
    /// Intent entity — created by AAInstallationDetectorSystem and consumed by
    /// AAPlacementPaymentSystem + AAPlacementCommitSystem in ModificationEnd.
    ///
    /// Serializable: survives save/load so that reserved credits and pending budget
    /// deductions are never orphaned. After load, the commit system processes surviving
    /// intents based on their resolved state (BudgetResolved / CreditResolved flags).
    ///
    /// Two-phase placement transaction (save-safe):
    /// Phase 1: payment system writes BudgetResolved/BudgetSucceeded or
    ///   CreditResolved/CreditSucceeded on this entity.
    /// Phase 2: commit system applies or rejects the resolved intent and destroys it.
    ///
    /// Lifecycle:
    /// 1. Detector resolves prefab → creates intent entity.
    /// 2. Payment system resolves credit/budget directly in ModificationEnd.
    /// 3. Commit system applies or rejects based on resolved outcome → destroy intent.
    /// </summary>
    public struct AAPlacementIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        /// <summary>Placed AA object reference — a StaticObjectPrefab prop, not a vanilla
        /// building (Axiom 11: no Entity fields).</summary>
        [TxTarget]
        public BuildingRef Building;

        /// <summary>Fully resolved AA stats — pipeline does not re-resolve from prefab.</summary>
        public AAType ResolvedType;
        public float Range;
        public float InterceptChanceShahed;
        public float InterceptChanceBallistic;
        public int MaxAmmo;
        public float CooldownDuration;
        public int CrewRequired;

        /// <summary>Budget cost (from AirDefensePrefabData.Price). 0 for free placements.</summary>
        public int Cost;

        /// <summary>True if budget deduction was requested (Cost > 0 and no credit used).</summary>
        public bool RequiresBudget;

        /// <summary>Credit reserved in detector — returned on reject.</summary>
        [TxRefund]
        public AAPlacementCreditKind ReservedCreditKind;

        /// <summary>
        /// True after pipeline received budget result and wrote outcome to this entity.
        /// Serialized — survives save/load. Pipeline uses this to finalize without re-waiting.
        /// </summary>
        public bool BudgetResolved;

        /// <summary>True if budget deduction succeeded. Only valid when BudgetResolved=true.</summary>
        public bool BudgetSucceeded;

        /// <summary>
        /// True after the credit owner (AirDefenseStateSystem) resolved the reserved
        /// credit for this intent — symmetric with BudgetResolved. Serialized; survives
        /// save/load so a reserved-but-unresolved claim is processed exactly once on load.
        /// Only meaningful when ReservedCreditKind != None.
        /// </summary>
        public bool CreditResolved;

        /// <summary>
        /// True if the credit decrement actually happened (a durable singleton write).
        /// Only valid when CreditResolved=true. Reject refunds the credit ONLY when this
        /// is true (no decrement → no refund); mirrors the BudgetSucceeded refund gate.
        /// </summary>
        public bool CreditSucceeded;

        /// <summary>Bridge request id carried from the UI placement command.</summary>
        public int RequestId;

        /// <summary>
        /// Stable per-placement correlation id assigned by AAInstallationDetectorSystem
        /// (monotonic, always non-zero). Budget result is matched to THIS intent by
        /// PlacementId, not by Building — building identity is ambiguous across save/load,
        /// re-placement, or two pending intents on the same building.
        /// </summary>
        [TxId]
        public int PlacementId;

        public bool RefundResolved;
        public bool RefundSucceeded;
        public bool ApplyResolved;
        public bool ApplySucceeded;
        public AAPlacementTerminalReason TerminalReason;

        /// <summary>
        /// True once Phase 2 has emitted the terminal placement result to the UI bridge
        /// for a reject. Serialized (unlike <see cref="Applied"/>) so the player-facing
        /// "rejected" answer is emitted exactly once and stays decoupled from the deferred
        /// budget refund: the terminal emit is synchronous and pause-safe (settles the
        /// button the same tick), while the refund drains later through GameSimulation.
        /// Gating both the emit and the eventual destroy on this flag makes the reject
        /// path idempotent across the 2x-3x same-frame retries and the post-load
        /// ApplyResolved re-entry, so it cannot double-emit or double-destroy.
        /// </summary>
        public bool TerminalEmitted;

        /// <summary>
        /// Set true the moment Phase 2 decides this intent's outcome (apply OR reject),
        /// before the deferred ECB destroy plays back. CS2 barriers play back after ALL
        /// sim ticks, so at 2x-3x the intent is still alive on later ticks of the same
        /// frame — this guard makes Phase 2 idempotent per-intent (skip already-decided),
        /// so it cannot reject the very intent it just applied. Data write (not structural)
        /// → visible on the next tick immediately, unlike a deferred tag component.
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized (do not add it to Serialize). It
        /// records that the apply/destroy ECB commands were *queued*, but those commands
        /// live in the ECB until barrier playback and do NOT survive a save taken before
        /// playback. Persisting Applied=true would make a surviving intent be skipped
        /// forever after load while its AirDefenseInstallation was never created — the
        /// exact save-safe invariant this pipeline guarantees. After load Applied is
        /// default false by design, so the surviving intent is reprocessed (BudgetResolved
        /// — whose effect IS persistent — still drives the post-load outcome).
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;
        private const float DEFAULT_RANGE = 1200f;
        private const float MAX_RANGE = 5000f;
        private const float DEFAULT_COOLDOWN_DURATION = 30f;
        private const float MAX_COOLDOWN_DURATION = 600f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 23);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "aaT", (byte)ResolvedType);
                KeyedSerializer.WriteField(writer, "rng", Range);
                KeyedSerializer.WriteField(writer, "icS", InterceptChanceShahed);
                KeyedSerializer.WriteField(writer, "icB", InterceptChanceBallistic);
                KeyedSerializer.WriteField(writer, "mxAm", MaxAmmo);
                KeyedSerializer.WriteField(writer, "cdDur", CooldownDuration);
                KeyedSerializer.WriteField(writer, "crew", CrewRequired);
                KeyedSerializer.WriteField(writer, "cost", Cost);
                KeyedSerializer.WriteField(writer, "reqB", RequiresBudget);
                KeyedSerializer.WriteEnumByteField(writer, "crK", (byte)ReservedCreditKind);
                KeyedSerializer.WriteField(writer, "bRes", BudgetResolved);
                KeyedSerializer.WriteField(writer, "bOk", BudgetSucceeded);
                KeyedSerializer.WriteField(writer, "cRes", CreditResolved);
                KeyedSerializer.WriteField(writer, "cOk", CreditSucceeded);
                KeyedSerializer.WriteField(writer, "rqId", RequestId);
                KeyedSerializer.WriteField(writer, "plId", PlacementId);
                KeyedSerializer.WriteField(writer, "rRes", RefundResolved);
                KeyedSerializer.WriteField(writer, "rOk", RefundSucceeded);
                KeyedSerializer.WriteField(writer, "aRes", ApplyResolved);
                KeyedSerializer.WriteField(writer, "aOk", ApplySucceeded);
                KeyedSerializer.WriteEnumByteField(writer, "term", (byte)TerminalReason);
                KeyedSerializer.WriteField(writer, "tEmit", TerminalEmitted);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
                // Persisting it would skip a save-surviving intent forever (its
                // apply/destroy ECB never played back). See field doc.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AAPlacementIntent)))
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
                            case "aaT": ResolvedType = KeyedSerializer.ReadEnumByte<TReader, AAType>(reader, tag, "aaT", AAType.HeritageBofors); break;
                            case "rng": Range = KeyedSerializer.ReadSafeFloat(reader, tag, "rng", 1f, MAX_RANGE, DEFAULT_RANGE); break;
                            case "icS": InterceptChanceShahed = KeyedSerializer.ReadSafeFloat(reader, tag, "icS", 0f, 1f, 0f); break;
                            case "icB": InterceptChanceBallistic = KeyedSerializer.ReadSafeFloat(reader, tag, "icB", 0f, 1f, 0f); break;
                            case "mxAm": MaxAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "mxAm", 0, 10000, 0); break;
                            case "cdDur": CooldownDuration = KeyedSerializer.ReadSafeFloat(reader, tag, "cdDur", 0.1f, MAX_COOLDOWN_DURATION, DEFAULT_COOLDOWN_DURATION); break;
                            case "crew": CrewRequired = KeyedSerializer.ReadBoundedInt(reader, tag, "crew", 0, 100, 0); break;
                            case "cost": Cost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "reqB": RequiresBudget = KeyedSerializer.ReadBool(reader, tag, "reqB"); break;
                            case "crK": ReservedCreditKind = KeyedSerializer.ReadEnumByte<TReader, AAPlacementCreditKind>(reader, tag, "crK", AAPlacementCreditKind.None); break;
                            case "bRes": BudgetResolved = KeyedSerializer.ReadBool(reader, tag, "bRes"); break;
                            case "bOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "bOk"); break;
                            case "cRes": CreditResolved = KeyedSerializer.ReadBool(reader, tag, "cRes"); break;
                            case "cOk": CreditSucceeded = KeyedSerializer.ReadBool(reader, tag, "cOk"); break;
                            case "rqId": RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "rqId", 0, int.MaxValue, 0); break;
                            case "plId": PlacementId = KeyedSerializer.ReadBoundedInt(reader, tag, "plId", 0, int.MaxValue, 0); break;
                            case "rRes": RefundResolved = KeyedSerializer.ReadBool(reader, tag, "rRes"); break;
                            case "rOk": RefundSucceeded = KeyedSerializer.ReadBool(reader, tag, "rOk"); break;
                            case "aRes": ApplyResolved = KeyedSerializer.ReadBool(reader, tag, "aRes"); break;
                            case "aOk": ApplySucceeded = KeyedSerializer.ReadBool(reader, tag, "aOk"); break;
                            case "term": TerminalReason = KeyedSerializer.ReadEnumByte<TReader, AAPlacementTerminalReason>(reader, tag, "term", AAPlacementTerminalReason.None); break;
                            case "tEmit": TerminalEmitted = KeyedSerializer.ReadBool(reader, tag, "tEmit"); break;
                            // 'appl' intentionally not read — Applied is runtime-only and
                            // must be default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(AAPlacementIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
