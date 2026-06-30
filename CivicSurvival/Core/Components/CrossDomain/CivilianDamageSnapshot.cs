using System;
using System.Collections.Generic;
using CivicSurvival.Core.Types;

#nullable enable

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct CivilianDamageSnapshot : IEquatable<CivilianDamageSnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public CivilianDamageSnapshot(
            IReadOnlyList<CivilianBuildingDamageDto> buildings,
            int damagedCount,
            int repairingCount)
        {
            if (buildings == null || buildings.Count == 0)
            {
                Buildings = Array.Empty<CivilianBuildingDamageDto>();
            }
            else
            {
                var copy = new CivilianBuildingDamageDto[buildings.Count];
                for (int i = 0; i < buildings.Count; i++)
                    copy[i] = buildings[i];
                Buildings = copy;
            }
            DamagedCount = damagedCount;
            RepairingCount = repairingCount;
        }

        public IReadOnlyList<CivilianBuildingDamageDto> Buildings { get; }
        public int DamagedCount { get; }
        public int RepairingCount { get; }

        public static CivilianDamageSnapshot Empty { get; } =
            new(Array.Empty<CivilianBuildingDamageDto>(), 0, 0);

        public bool Equals(CivilianDamageSnapshot other)
            => DamagedCount == other.DamagedCount
               && RepairingCount == other.RepairingCount
               && CivilianBuildingDamageDtoComparer.ListEquals(Buildings, other.Buildings);

        public override bool Equals(object? obj)
            => obj is CivilianDamageSnapshot other && Equals(other);

        public static bool operator ==(CivilianDamageSnapshot left, CivilianDamageSnapshot right)
            => left.Equals(right);

        public static bool operator !=(CivilianDamageSnapshot left, CivilianDamageSnapshot right)
            => !left.Equals(right);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + DamagedCount;
                hash = (hash * HashMultiplier) + RepairingCount;
                for (int i = 0; i < Buildings.Count; i++)
                    hash = (hash * HashMultiplier) + CivilianBuildingDamageDtoComparer.GetHashCode(Buildings[i]);
                return hash;
            }
        }
    }

    internal static class CivilianBuildingDamageDtoComparer
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;
        private const float FloatEpsilon = 0.0001f;

        public static bool ListEquals(
            IReadOnlyList<CivilianBuildingDamageDto> left,
            IReadOnlyList<CivilianBuildingDamageDto> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!Equals(left[i], right[i]))
                    return false;
            }

            return true;
        }

        public static bool Equals(CivilianBuildingDamageDto left, CivilianBuildingDamageDto right)
            => left.Building.Index == right.Building.Index
               && left.Building.Version == right.Building.Version
               && left.HitCount == right.HitCount
               && left.MaxHits == right.MaxHits
               && Math.Abs(left.DamagePercent - right.DamagePercent) <= FloatEpsilon
               && left.IsRepairing == right.IsRepairing
               && Math.Abs(left.RepairHoursLeft - right.RepairHoursLeft) <= FloatEpsilon
               && left.RepairTypeByte == right.RepairTypeByte;

        public static int GetHashCode(CivilianBuildingDamageDto value)
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + value.Building.Index;
                hash = (hash * HashMultiplier) + value.Building.Version;
                hash = (hash * HashMultiplier) + value.HitCount;
                hash = (hash * HashMultiplier) + value.MaxHits;
                hash = (hash * HashMultiplier) + Math.Round(value.DamagePercent, 4).GetHashCode();
                hash = (hash * HashMultiplier) + value.IsRepairing.GetHashCode();
                hash = (hash * HashMultiplier) + Math.Round(value.RepairHoursLeft, 4).GetHashCode();
                hash = (hash * HashMultiplier) + value.RepairTypeByte.GetHashCode();
                return hash;
            }
        }
    }
}

#nullable restore
