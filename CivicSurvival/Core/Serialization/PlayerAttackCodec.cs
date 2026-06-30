using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct PlayerOperationSlotPersistState
    {
        public PlayerOperationSlotPersistState(
            int state,
            string attackType,
            long lockedAmount,
            float prepareStartTime,
            float prepareDuration,
            string operationId,
            int executionId = 0,
            AttackCategory executionCategory = AttackCategory.Kinetic,
            float executionBaseDamage = 0f,
            float executionActualDamage = 0f,
            bool executionWasBlocked = false,
            bool executionWasVulnerable = false)
        {
            State = state;
            AttackType = attackType ?? string.Empty;
            LockedAmount = lockedAmount;
            PrepareStartTime = prepareStartTime;
            PrepareDuration = prepareDuration;
            OperationId = operationId ?? string.Empty;
            ExecutionId = executionId;
            ExecutionCategory = executionCategory;
            ExecutionBaseDamage = executionBaseDamage;
            ExecutionActualDamage = executionActualDamage;
            ExecutionWasBlocked = executionWasBlocked;
            ExecutionWasVulnerable = executionWasVulnerable;
        }

        public int State { get; }
        public string AttackType { get; }
        public long LockedAmount { get; }
        public float PrepareStartTime { get; }
        public float PrepareDuration { get; }
        public string OperationId { get; }
        public int ExecutionId { get; }
        public AttackCategory ExecutionCategory { get; }
        public float ExecutionBaseDamage { get; }
        public float ExecutionActualDamage { get; }
        public bool ExecutionWasBlocked { get; }
        public bool ExecutionWasVulnerable { get; }
    }

    public readonly struct PlayerAttackPersistState
    {
        public PlayerAttackPersistState(int nextOperationId, PlayerOperationSlotPersistState[] slots, int nextExecutionId = 1)
        {
            NextOperationId = nextOperationId;
            Slots = slots ?? Array.Empty<PlayerOperationSlotPersistState>();
            NextExecutionId = nextExecutionId;
        }

        public int NextOperationId { get; }
        public int NextExecutionId { get; }
        public IReadOnlyList<PlayerOperationSlotPersistState> Slots { get; }
    }

    public static class PlayerAttackCodec
    {
        public const int DefaultNextOperationId = 1;
        public const int DefaultNextExecutionId = 1;
        public const int MaxOperationState = 3;

        public static void Write<TWriter>(in PlayerAttackPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_NextOperationId", state.NextOperationId);
            KeyedSerializer.WriteField(writer, "m_NextExecutionId", state.NextExecutionId);

            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteBufferHeader(writer, "slots", state.Slots.Count);
            for (int i = 0; i < state.Slots.Count; i++)
            {
                var slot = state.Slots[i];
                KeyedSerializer.WriteBlockHeader(writer, 12);
                KeyedSerializer.WriteEnumIntField(writer, "state", slot.State);
                KeyedSerializer.WriteField(writer, "attackType", slot.AttackType);
                KeyedSerializer.WriteField(writer, "lockedAmount", slot.LockedAmount);
                KeyedSerializer.WriteField(writer, "prepareStartTime", slot.PrepareStartTime);
                KeyedSerializer.WriteField(writer, "prepareDuration", slot.PrepareDuration);
                KeyedSerializer.WriteField(writer, "operationId", slot.OperationId);
                KeyedSerializer.WriteField(writer, "executionId", slot.ExecutionId);
                KeyedSerializer.WriteEnumByteField(writer, "executionCategory", (byte)slot.ExecutionCategory);
                KeyedSerializer.WriteField(writer, "executionBaseDamage", slot.ExecutionBaseDamage);
                KeyedSerializer.WriteField(writer, "executionActualDamage", slot.ExecutionActualDamage);
                KeyedSerializer.WriteField(writer, "executionWasBlocked", slot.ExecutionWasBlocked);
                KeyedSerializer.WriteField(writer, "executionWasVulnerable", slot.ExecutionWasVulnerable);
            }
        }

        public static void Read<TReader>(TReader reader, int maxSlots, out PlayerAttackPersistState state)
            where TReader : IReader
        {
            int nextOperationId = DefaultNextOperationId;
            int nextExecutionId = DefaultNextExecutionId;

            int persistFieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < persistFieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_NextOperationId":
                        nextOperationId = KeyedSerializer.ReadBoundedInt(
                            reader,
                            tag,
                            "m_NextOperationId",
                            1,
                            int.MaxValue,
                            DefaultNextOperationId);
                        break;
                    case "m_NextExecutionId":
                        nextExecutionId = KeyedSerializer.ReadBoundedInt(
                            reader,
                            tag,
                            "m_NextExecutionId",
                            1,
                            int.MaxValue,
                            DefaultNextExecutionId);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            var slots = new PlayerOperationSlotPersistState[Math.Max(0, maxSlots)];
            int slotsBlockFieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < slotsBlockFieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "slots":
                        ReadSlots(reader, tag, slots);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new PlayerAttackPersistState(nextOperationId, slots, nextExecutionId);
        }

        private static void ReadSlots<TReader>(
            TReader reader,
            TypeTag tag,
            PlayerOperationSlotPersistState[] slots)
            where TReader : IReader
        {
            int slotCount = KeyedSerializer.ReadBufferCount(reader, tag, "slots", slots.Length);
            for (int i = 0; i < slotCount; i++)
            {
                int slotState = 0;
                string attackType = string.Empty;
                long lockedAmount = 0;
                float prepareStartTime = 0f;
                float prepareDuration = 0f;
                string operationId = string.Empty;
                int executionId = 0;
                AttackCategory executionCategory = AttackCategory.Kinetic;
                float executionBaseDamage = 0f;
                float executionActualDamage = 0f;
                bool executionWasBlocked = false;
                bool executionWasVulnerable = false;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "state":
                            slotState = ReadOperationState(reader, fieldTag);
                            break;
                        case "attackType":
                            attackType = KeyedSerializer.ReadString(reader, fieldTag, "attackType");
                            break;
                        case "lockedAmount":
                            lockedAmount = KeyedSerializer.ReadBoundedLong(reader, fieldTag, "lockedAmount", 0, long.MaxValue);
                            break;
                        case "prepareStartTime":
                            prepareStartTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "prepareStartTime", 0f);
                            break;
                        case "prepareDuration":
                            prepareDuration = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "prepareDuration", 0f);
                            break;
                        case "operationId":
                            operationId = KeyedSerializer.ReadString(reader, fieldTag, "operationId");
                            break;
                        case "executionId":
                            executionId = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "executionId", 0, int.MaxValue, 0);
                            break;
                        case "executionCategory":
                            executionCategory = ReadExecutionCategory(reader, fieldTag);
                            break;
                        case "executionBaseDamage":
                            executionBaseDamage = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "executionBaseDamage", 0f, 100f, 0f);
                            break;
                        case "executionActualDamage":
                            executionActualDamage = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "executionActualDamage", 0f, 100f, 0f);
                            break;
                        case "executionWasBlocked":
                            executionWasBlocked = KeyedSerializer.ReadBool(reader, fieldTag, "executionWasBlocked");
                            break;
                        case "executionWasVulnerable":
                            executionWasVulnerable = KeyedSerializer.ReadBool(reader, fieldTag, "executionWasVulnerable");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                slots[i] = new PlayerOperationSlotPersistState(
                    slotState,
                    attackType,
                    lockedAmount,
                    prepareStartTime,
                    prepareDuration,
                    operationId,
                    executionId,
                    executionCategory,
                    executionBaseDamage,
                    executionActualDamage,
                    executionWasBlocked,
                    executionWasVulnerable);
            }
        }

        private static int ReadOperationState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumInt, "state"))
                return 0;

            reader.Read(out int value);
            return value >= 0 && value <= MaxOperationState ? value : 0;
        }

        private static AttackCategory ReadExecutionCategory<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            return KeyedSerializer.ReadEnumByte<TReader, AttackCategory>(
                reader,
                tag,
                "executionCategory",
                AttackCategory.Kinetic);
        }
    }
}
