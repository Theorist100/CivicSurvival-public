using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct OperationSlotsSnapshot : IEquatable<OperationSlotsSnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        private readonly OperationSlotSnapshot[]? m_Slots;

        public OperationSlotsSnapshot(OperationSlotSnapshot[] slots)
        {
            m_Slots = slots ?? Array.Empty<OperationSlotSnapshot>();
        }

        public IReadOnlyList<OperationSlotSnapshot> Slots => m_Slots ?? Array.Empty<OperationSlotSnapshot>();

        public int SlotCount => (m_Slots ?? Array.Empty<OperationSlotSnapshot>()).Length;

        public OperationSlotSnapshot GetSlot(int index)
            => (m_Slots ?? Array.Empty<OperationSlotSnapshot>())[index];

        public void CopySlotsTo(OperationSlotSnapshot[] destination, int count)
            => Array.Copy(m_Slots ?? Array.Empty<OperationSlotSnapshot>(), destination, count);

        public static OperationSlotsSnapshot Empty { get; } = new(Array.Empty<OperationSlotSnapshot>());

        public bool Equals(OperationSlotsSnapshot other)
        {
            var slots = m_Slots ?? Array.Empty<OperationSlotSnapshot>();
            var otherSlots = other.m_Slots ?? Array.Empty<OperationSlotSnapshot>();
            if (ReferenceEquals(slots, otherSlots)) return true;
            if (slots.Length != otherSlots.Length) return false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Equals(otherSlots[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
            => obj is OperationSlotsSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                var slots = m_Slots ?? Array.Empty<OperationSlotSnapshot>();
                for (int i = 0; i < slots.Length; i++)
                    hash = (hash * HashMultiplier) + slots[i].GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(OperationSlotsSnapshot left, OperationSlotsSnapshot right)
            => left.Equals(right);

        public static bool operator !=(OperationSlotsSnapshot left, OperationSlotsSnapshot right)
            => !left.Equals(right);
    }

    public readonly struct OperationSlotSnapshot : IEquatable<OperationSlotSnapshot>
    {
        private const float FloatEqualityTolerance = 0.0001f;
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public OperationSlotSnapshot(
            string attackType,
            int state,
            long lockedAmount,
            float prepareStartTime,
            float prepareDuration,
            string operationId)
        {
            AttackType = attackType ?? string.Empty;
            State = state;
            LockedAmount = lockedAmount;
            PrepareStartTime = prepareStartTime;
            PrepareDuration = prepareDuration;
            OperationId = operationId ?? string.Empty;
        }

        public string AttackType { get; }

        public int State { get; }

        public long LockedAmount { get; }

        public float PrepareStartTime { get; }

        public float PrepareDuration { get; }

        public string OperationId { get; }

        public float GetProgress(float currentTime)
        {
            const int PreparingState = 1;
            const int ReadyState = 2;

            if (State != PreparingState)
                return State == ReadyState ? 1f : 0f;
            if (PrepareDuration <= 0f)
                return 1f;

            float progress = (currentTime - PrepareStartTime) / PrepareDuration;
            if (progress <= 0f) return 0f;
            if (progress >= 1f) return 1f;
            return progress;
        }

        public bool Equals(OperationSlotSnapshot other)
            => string.Equals(AttackType, other.AttackType, StringComparison.Ordinal)
                && State == other.State
                && LockedAmount == other.LockedAmount
                && Math.Abs(PrepareStartTime - other.PrepareStartTime) <= FloatEqualityTolerance
                && Math.Abs(PrepareDuration - other.PrepareDuration) <= FloatEqualityTolerance
                && string.Equals(OperationId, other.OperationId, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is OperationSlotSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + StringComparer.Ordinal.GetHashCode(AttackType ?? "");
                hash = (hash * HashMultiplier) + State;
                hash = (hash * HashMultiplier) + LockedAmount.GetHashCode();
                hash = (hash * HashMultiplier) + QuantizeForHash(PrepareStartTime);
                hash = (hash * HashMultiplier) + QuantizeForHash(PrepareDuration);
                hash = (hash * HashMultiplier) + StringComparer.Ordinal.GetHashCode(OperationId ?? "");
                return hash;
            }
        }

        private static int QuantizeForHash(float value)
            => (int)Math.Round(value / FloatEqualityTolerance);

        public static bool operator ==(OperationSlotSnapshot left, OperationSlotSnapshot right)
            => left.Equals(right);

        public static bool operator !=(OperationSlotSnapshot left, OperationSlotSnapshot right)
            => !left.Equals(right);
    }
}
