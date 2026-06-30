using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Services;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Controls whether BRS destroys the request entity or retains it with a result component.
    /// Default (FireAndForget) = existing behavior: BRS destroys after processing.
    /// RetainResult = BRS writes BudgetDeductResult on entity, consumer owns cleanup.
    /// </summary>
    public enum BudgetResultMode : byte
    {
        FireAndForget = 0,
        RetainResult = 1
    }

    public enum BudgetIncomeKind : byte
    {
        RecurringRevenue = 0,
        Refund = 1,
        DebtMovement = 2,
        DonorOrEmergencyCredit = 3,
        Kickback = 4,
        OneOffCredit = 5
    }

    /// <summary>
    /// Marker added when a retained deduct request is expired by post-load cleanup
    /// instead of resolved by live budget processing. Domain consumers can decide
    /// whether expiry is terminal or should be retried from their durable owner.
    /// </summary>
    public struct BudgetDeductExpiredOnLoad : IComponentData, IEmptySerializable
    {
    }

    /// <summary>
    /// ECB request to deduct from city budget.
    /// Ephemeral entity pattern — created by any system, processed by BudgetResolutionSystem.
    /// Sorted by Priority (ascending) before processing: PlayerAction first, Damage last.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterMetaCreatedTime)]
    public struct BudgetDeductRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Amount to deduct (positive).</summary>
        public long Amount;

        /// <summary>Budget expense category (BudgetCategory constant).</summary>
        public FixedString32Bytes Category;

        /// <summary>Priority tier — lower = higher priority (BudgetPriority constants).</summary>
        public byte Priority;

        /// <summary>Source system name (for logging/tracing).</summary>
        public FixedString64Bytes Source;

        /// <summary>
        /// Non-empty = on failure, pay what's available from budget and add remainder as debt.
        /// Value is the debt category (DebtCategory constant).
        /// Empty = no debt fallback (failure written to BudgetDeductResult or entity destroyed).
        /// </summary>
        public FixedString32Bytes DebtFallbackCategory;

        /// <summary>
        /// Controls post-resolution lifecycle.
        /// FireAndForget (default): BRS destroys entity after processing.
        /// RetainResult: BRS writes BudgetDeductResult on entity, consumer owns cleanup.
        /// </summary>
        public BudgetResultMode ResultMode;

        /// <summary>
        /// Same-frame affordability reservation owned by this request.
        /// Zero means no pending counter reservation was registered.
        /// </summary>
        public long ReservationAmount;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 3;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "cat", Category.ToString());
                // Keep "pri" as I32 to match ReadBoundedInt below.
                KeyedSerializer.WriteField(writer, "pri", (int)Priority);
                KeyedSerializer.WriteField(writer, "src", Source.ToString());
                KeyedSerializer.WriteField(writer, "debt", DebtFallbackCategory.ToString());
                KeyedSerializer.WriteEnumByteField(writer, "mode", (byte)ResultMode);
                KeyedSerializer.WriteField(writer, "res", ReservationAmount);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BudgetDeductRequest)))
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
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "cat": { string s = KeyedSerializer.ReadString(reader, tag, "cat"); Category = new FixedString32Bytes(s ?? ""); } break;
                            case "pri": Priority = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "pri", 0, 255, 0); break;
                            case "src": { string s = KeyedSerializer.ReadString(reader, tag, "src"); Source = new FixedString64Bytes(s ?? ""); } break;
                            case "debt": { string s = KeyedSerializer.ReadString(reader, tag, "debt"); DebtFallbackCategory = new FixedString32Bytes(s ?? ""); } break;
                            case "mode": ResultMode = KeyedSerializer.ReadEnumByte<TReader, BudgetResultMode>(reader, tag, "mode", BudgetResultMode.FireAndForget); break;
                            case "res": ReservationAmount = KeyedSerializer.ReadBoundedLong(reader, tag, "res", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Result of budget deduction, added by BRS to retained-result request entities.
    /// Consumer queries this component to determine outcome, then destroys the entity.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.ReconciledOutcome)]
    public struct BudgetDeductResult : IComponentData, ICommandRequest, ISerializable
    {
        public bool Succeeded;
        public long Amount;
        public long PaidAmount;
        public long DebtAmount;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteField(writer, "ok", Succeeded);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "paid", PaidAmount);
                KeyedSerializer.WriteField(writer, "debt", DebtAmount);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BudgetDeductResult)))
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
                            case "ok": Succeeded = KeyedSerializer.ReadBool(reader, tag, "ok"); break;
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "paid": PaidAmount = KeyedSerializer.ReadBoundedLong(reader, tag, "paid", 0, long.MaxValue); break;
                            case "debt": DebtAmount = KeyedSerializer.ReadBoundedLong(reader, tag, "debt", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Exact owner metadata for retained Intel upgrade payments.
    /// BudgetDeductRequest.Source is diagnostic text only; this component carries
    /// the target level that may be applied after BudgetDeductResult succeeds.
    /// </summary>
    public struct IntelUpgradeBudgetIntent : IComponentData, ISerializable
    {
        public int TargetLevel;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "target", TargetLevel);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out _, out var block, nameof(IntelUpgradeBudgetIntent)))
            {
                SetDefaults();
                return;
            }

            try
            {
                int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int i = 0; i < fc; i++)
                {
                    var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "target":
                            TargetLevel = KeyedSerializer.ReadBoundedInt(reader, tag, "target", 0, int.MaxValue, 0);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, tag);
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                RequestDeserializeLog.Log.Error($"Deserialize {nameof(IntelUpgradeBudgetIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// ECB request to add funds to city budget.
    /// Ephemeral/retained entity pattern — created by any system, processed by BudgetResolutionSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct BudgetAddFundsRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Amount to add (positive).</summary>
        public long Amount;

        /// <summary>Income source (BudgetSource constant).</summary>
        public FixedString32Bytes Source;

        public BudgetIncomeKind IncomeKind;

        /// <summary>
        /// Controls post-resolution lifecycle.
        /// FireAndForget (default): BRS destroys entity after processing.
        /// RetainResult: BRS writes BudgetAddFundsResult on entity, consumer owns cleanup.
        /// </summary>
        public BudgetResultMode ResultMode;

        /// <summary>
        /// Stable owner operation key for retained refunds/grants.
        /// Empty for legacy fire-and-forget income.
        /// </summary>
        public FixedString128Bytes OperationKey;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 3;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "src", Source.ToString());
                KeyedSerializer.WriteField(writer, "kind", (int)IncomeKind);
                KeyedSerializer.WriteEnumByteField(writer, "mode", (byte)ResultMode);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BudgetAddFundsRequest)))
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
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "src": { string s = KeyedSerializer.ReadString(reader, tag, "src"); Source = new FixedString32Bytes(s ?? ""); } break;
                            case "kind": IncomeKind = ReadIncomeKind(reader, tag, "kind"); break;
                            case "mode": ResultMode = KeyedSerializer.ReadEnumByte<TReader, BudgetResultMode>(reader, tag, "mode", BudgetResultMode.FireAndForget); break;
                            case "op": { string s = KeyedSerializer.ReadString(reader, tag, "op"); OperationKey = new FixedString128Bytes(s ?? ""); } break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }

        private static BudgetIncomeKind ReadIncomeKind<TReader>(TReader reader, TypeTag tag, string fieldName)
            where TReader : IReader
        {
            int value = KeyedSerializer.ReadBoundedInt(reader, tag, fieldName, 0, 5, 0);
            return value switch
            {
                0 => BudgetIncomeKind.RecurringRevenue,
                1 => BudgetIncomeKind.Refund,
                2 => BudgetIncomeKind.DebtMovement,
                3 => BudgetIncomeKind.DonorOrEmergencyCredit,
                4 => BudgetIncomeKind.Kickback,
                5 => BudgetIncomeKind.OneOffCredit,
                _ => BudgetIncomeKind.RecurringRevenue
            };
        }
    }

    /// <summary>
    /// Result of budget add-funds, added by BRS to retained-result request entities.
    /// Consumer queries this component to confirm refunds/grants before terminal cleanup.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.ReconciledOutcome)]
    public struct BudgetAddFundsResult : IComponentData, ICommandRequest, ISerializable
    {
        public bool Succeeded;
        public long Amount;
        public long AppliedAmount;
        public BudgetResult Result;
        public FixedString128Bytes OperationKey;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "ok", Succeeded);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "applied", AppliedAmount);
                KeyedSerializer.WriteEnumByteField(writer, "result", (byte)Result);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BudgetAddFundsResult)))
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
                            case "ok": Succeeded = KeyedSerializer.ReadBool(reader, tag, "ok"); break;
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "applied": AppliedAmount = KeyedSerializer.ReadBoundedLong(reader, tag, "applied", 0, long.MaxValue); break;
                            case "result": Result = KeyedSerializer.ReadEnumByte<TReader, BudgetResult>(reader, tag, "result", BudgetResult.None); break;
                            case "op": { string s = KeyedSerializer.ReadString(reader, tag, "op"); OperationKey = new FixedString128Bytes(s ?? ""); } break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// ECB request to process monthly debt payment.
    /// Created by CityDebtTrackingSystem, processed by BudgetResolutionSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct BudgetDebtPaymentRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Monthly payment rate (0.10 = 10%).</summary>
        public float Rate;

        /// <summary>Minimum payment amount.</summary>
        public long Minimum;

        /// <summary>Interest rate on missed/partial payments.</summary>
        public float InterestRate;

        /// <summary>Debt-to-income ratio threshold for UI warning.</summary>
        public float WarningRatio;

        /// <summary>Debt-to-income ratio threshold for auto-restructure.</summary>
        public float RestructureRatio;

        /// <summary>Reduced interest rate during restructure.</summary>
        public float RestructuredRate;

        /// <summary>Billing day this request represents; used to collapse stale backlog.</summary>
        public int BillingDay;

        /// <summary>Debt total observed when the billing request was queued.</summary>
        public long DecisionDebt;

        /// <summary>Recurring income earned during the billing period being closed.</summary>
        public long PeriodIncome;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 9);
                KeyedSerializer.WriteField(writer, "rate", Rate);
                KeyedSerializer.WriteField(writer, "min", Minimum);
                KeyedSerializer.WriteField(writer, "iRate", InterestRate);
                KeyedSerializer.WriteField(writer, "wRat", WarningRatio);
                KeyedSerializer.WriteField(writer, "rRat", RestructureRatio);
                KeyedSerializer.WriteField(writer, "rsRate", RestructuredRate);
                KeyedSerializer.WriteField(writer, "day", BillingDay);
                KeyedSerializer.WriteField(writer, "dDebt", DecisionDebt);
                KeyedSerializer.WriteField(writer, "pInc", PeriodIncome);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BudgetDebtPaymentRequest)))
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
                            case "rate": Rate = KeyedSerializer.ReadSafeFloat(reader, tag, "rate", 0f, 1f, 0f); break;
                            case "min": Minimum = KeyedSerializer.ReadBoundedLong(reader, tag, "min", 0, long.MaxValue); break;
                            case "iRate": InterestRate = KeyedSerializer.ReadSafeFloat(reader, tag, "iRate", 0f, 1f, 0f); break;
                            case "wRat": WarningRatio = KeyedSerializer.ReadSafeFloat(reader, tag, "wRat", 0f, 100f, 3f); break;
                            case "rRat": RestructureRatio = KeyedSerializer.ReadSafeFloat(reader, tag, "rRat", 0f, 100f, 5f); break;
                            case "rsRate": RestructuredRate = KeyedSerializer.ReadSafeFloat(reader, tag, "rsRate", 0f, 1f, 0f); break;
                            case "day": BillingDay = KeyedSerializer.ReadBoundedInt(reader, tag, "day", 0, 100000, 0); break;
                            case "dDebt": DecisionDebt = KeyedSerializer.ReadBoundedLong(reader, tag, "dDebt", 0, long.MaxValue); break;
                            case "pInc": PeriodIncome = KeyedSerializer.ReadBoundedLong(reader, tag, "pInc", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Priority tiers for budget deduction requests.
    /// Lower value = higher priority (processed first).
    /// </summary>
    public static class BudgetPriority
    {
        /// <summary>User-initiated actions (AA placement, hero deploy, repair).</summary>
        public const byte PlayerAction = 10;

        /// <summary>Automatic operations (ammo resupply, wave-triggered).</summary>
        public const byte Operational = 50;

        /// <summary>Recurring daily costs (refugee support, counter-OSINT).</summary>
        public const byte DailyCost = 100;

        /// <summary>Damage costs — has debt fallback (always "succeeds").</summary>
        public const byte Damage = 150;

    }
}
