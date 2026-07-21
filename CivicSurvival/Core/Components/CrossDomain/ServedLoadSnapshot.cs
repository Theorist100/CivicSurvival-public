using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct ServedLoadSnapshot : IEquatable<ServedLoadSnapshot>
    {
        public ServedLoadSnapshot(int consumptionKW)
        {
            ConsumptionKW = consumptionKW;
        }

        public int ConsumptionKW { get; }

        public static ServedLoadSnapshot Empty { get; } = new(0);

        public bool Equals(ServedLoadSnapshot other)
            => ConsumptionKW == other.ConsumptionKW;

        public override bool Equals(object? obj)
            => obj is ServedLoadSnapshot other && Equals(other);

        public override int GetHashCode()
            => ConsumptionKW;

        public static bool operator ==(ServedLoadSnapshot left, ServedLoadSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ServedLoadSnapshot left, ServedLoadSnapshot right)
            => !left.Equals(right);
    }
}
