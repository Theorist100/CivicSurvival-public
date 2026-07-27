using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Placement
{
    /// <summary>
    /// Terminal outcome of a building-placement transaction; the per-kind handler maps
    /// these onto its own player-facing reason ids.
    /// </summary>
    public enum BuildingPlacementTerminalReason : byte
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
    /// Generic building-placement intent — created by <c>BuildingPlacementDetectorSystem</c>
    /// and consumed by <c>BuildingPlacementPaymentSystem</c> + <c>BuildingPlacementCommitSystem</c>
    /// in ModificationEnd. Carries only the transaction state that is common to every placed
    /// mod building; the building-specific payload (stats to bake into the created entity) lives
    /// in a sibling component that the kind's handler adds to the same entity and reads at commit.
    ///
    /// Transaction doctrine (save-safe): a save taken between detection and commit survives,
    /// and the resolved/succeeded flags drive the post-load outcome so a reserved credit or
    /// paid budget is never orphaned.
    ///
    /// Two-phase placement transaction (save-safe):
    /// Phase 1: payment system writes BudgetResolved/BudgetSucceeded or CreditResolved/CreditSucceeded.
    /// Phase 2: commit system applies (handler creates the mod entity) or rejects and destroys it.
    /// </summary>
    public struct BuildingPlacementIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        /// <summary>Placed object reference — a StaticObjectPrefab prop, not a vanilla building
        /// (Axiom 11: no Entity fields).</summary>
        [TxTarget]
        public BuildingRef Building;

        /// <summary>Which building kind this intent belongs to — selects the handler.</summary>
        public BuildingPlacementKind Kind;

        /// <summary>Handler-interpreted placement mode carried from the pending signal.</summary>
        public byte Mode;

        /// <summary>Budget cost. 0 for free placements.</summary>
        public int Cost;

        /// <summary>True if a budget deduction was requested (Cost &gt; 0 and no credit used).</summary>
        public bool RequiresBudget;

        /// <summary>Handler-interpreted reserved credit kind (0 = none) — returned on reject.</summary>
        [TxRefund]
        public byte ReservedCreditKind;

        /// <summary>
        /// Which purse pays when <see cref="RequiresBudget"/> — the deduction and any reject
        /// refund go through the SAME purse (<see cref="PlacementPurse"/>). Orthogonal to
        /// <see cref="ReservedCreditKind"/>: a credit consumes a counter, a purse moves money.
        /// Persisted so the choice survives a save taken between detection and payment.
        /// </summary>
        public byte PayingPurse;

        /// <summary>True after the budget result was written to this entity. Serialized — survives save/load.</summary>
        public bool BudgetResolved;

        /// <summary>True if the budget deduction succeeded. Only valid when BudgetResolved=true.</summary>
        public bool BudgetSucceeded;

        /// <summary>True after the credit owner resolved the reserved credit. Serialized.</summary>
        public bool CreditResolved;

        /// <summary>True if the credit decrement actually happened. Only valid when CreditResolved=true.</summary>
        public bool CreditSucceeded;

        /// <summary>Bridge request id carried from the UI placement command.</summary>
        public int RequestId;

        /// <summary>
        /// Stable per-placement correlation id assigned by the detector (monotonic, always non-zero).
        /// Budget/credit results are matched to THIS intent by PlacementId, not by Building — building
        /// identity is ambiguous across save/load, re-placement, or two pending intents on one building.
        /// </summary>
        [TxId]
        public int PlacementId;

        public bool RefundResolved;
        public bool RefundSucceeded;
        public bool ApplyResolved;
        public bool ApplySucceeded;
        public BuildingPlacementTerminalReason TerminalReason;

        /// <summary>
        /// True once Phase 2 has emitted the terminal placement result to the UI bridge for a reject.
        /// Serialized (unlike <see cref="Applied"/>) so the player-facing "rejected" answer is emitted
        /// exactly once and stays decoupled from a deferred refund.
        /// </summary>
        public bool TerminalEmitted;

        /// <summary>
        /// Set true the moment Phase 2 decides this intent's outcome, before the deferred ECB destroy
        /// plays back — makes Phase 2 idempotent per-intent within a frame (barriers play back after all
        /// sim ticks; at 2x-3x the intent is still alive on later ticks).
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized. It records that the apply/destroy ECB commands
        /// were *queued*, but those commands do not survive a save taken before barrier playback. After
        /// load it is default-false by design, so a surviving intent is reprocessed and the persisted
        /// *Resolved/*Succeeded (whose effect IS persistent) drives the post-load outcome.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 19);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "kind", (byte)Kind);
                KeyedSerializer.WriteField(writer, "mode", (int)Mode);
                KeyedSerializer.WriteField(writer, "cost", Cost);
                KeyedSerializer.WriteField(writer, "reqB", RequiresBudget);
                KeyedSerializer.WriteField(writer, "crK", (int)ReservedCreditKind);
                KeyedSerializer.WriteField(writer, "prse", (int)PayingPurse);
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
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BuildingPlacementIntent)))
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
                            case "kind": Kind = KeyedSerializer.ReadEnumByte<TReader, BuildingPlacementKind>(reader, tag, "kind", BuildingPlacementKind.None); break;
                            case "mode": Mode = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "mode", 0, 255, 0); break;
                            case "cost": Cost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "reqB": RequiresBudget = KeyedSerializer.ReadBool(reader, tag, "reqB"); break;
                            case "crK": ReservedCreditKind = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "crK", 0, 255, 0); break;
                            case "prse": PayingPurse = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "prse", 0, 255, 0); break;
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
                            case "term": TerminalReason = KeyedSerializer.ReadEnumByte<TReader, BuildingPlacementTerminalReason>(reader, tag, "term", BuildingPlacementTerminalReason.None); break;
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
                Mod.Log.Error($"Deserialize {nameof(BuildingPlacementIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
