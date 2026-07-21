using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    internal readonly struct AirDefenseUiStatsSnapshot : IEquatable<AirDefenseUiStatsSnapshot>
    {
        /// <summary>Number of AAType enum values; sizes the per-type ammo arrays. Derived, not
        /// counted by hand — a new AA type must not need this edited to be tracked.</summary>
        public static readonly int TypeCount = AATypes.All.Count;

        public static AirDefenseUiStatsSnapshot Empty => new(
            0, 0, 0,
            new int[TypeCount],
            new int[TypeCount],
            new int[TypeCount]);

        public readonly int AaStations;
        public readonly int AaAmmo;
        public readonly int AaMaxAmmo;

        // Per-AAType ammo, indexed by (int)AAType. Sum of all entries equals AaAmmo/AaMaxAmmo.
        // Kept per-type because that is how the counter drains (each installation's magazine is
        // its own); the panel groups these into munition KINDS on the way out — see GetAmmo(AmmoKind).
        private readonly int[] m_AmmoByType;
        private readonly int[] m_MaxAmmoByType;

        // Live installations, indexed by (int)AAType. Sum of all entries equals AaStations. Kept as
        // an array for the same reason the ammo is: a new AA type must not need a case added here to
        // be counted.
        private readonly int[] m_CountByType;

        public AirDefenseUiStatsSnapshot(
            int aaStations,
            int aaAmmo,
            int aaMaxAmmo,
            int[] ammoByType,
            int[] maxAmmoByType,
            int[] countByType)
        {
            AaStations = aaStations;
            AaAmmo = aaAmmo;
            AaMaxAmmo = aaMaxAmmo;
            m_AmmoByType = ammoByType;
            m_MaxAmmoByType = maxAmmoByType;
            m_CountByType = countByType;
        }

        /// <summary>Live installations of one AA type.</summary>
        public int GetCount(AAType type)
        {
            int index = (int)type;
            return m_CountByType != null && (uint)index < (uint)m_CountByType.Length ? m_CountByType[index] : 0;
        }

        public int GetAmmo(AAType type)
        {
            int index = (int)type;
            return m_AmmoByType != null && (uint)index < (uint)m_AmmoByType.Length ? m_AmmoByType[index] : 0;
        }

        /// <summary>
        /// Ammo summed over every AA type that eats this munition kind — the number the panel shows
        /// and the restock button prices. Sums the enum rather than naming types, so a new AA type
        /// joins its kind by its weapon class alone, with no line to add here.
        /// </summary>
        public int GetAmmo(AmmoKind kind) => SumByKind(kind, max: false);

        public int GetMaxAmmo(AAType type)
        {
            int index = (int)type;
            return m_MaxAmmoByType != null && (uint)index < (uint)m_MaxAmmoByType.Length ? m_MaxAmmoByType[index] : 0;
        }

        /// <summary>Capacity counterpart of <see cref="GetAmmo(AmmoKind)"/>.</summary>
        public int GetMaxAmmo(AmmoKind kind) => SumByKind(kind, max: true);

        private int SumByKind(AmmoKind kind, bool max)
        {
            int sum = 0;
            for (int i = 0; i < TypeCount; i++)
            {
                var type = (AAType)i;
                if (type.AmmoKind() != kind)
                    continue;
                sum += max ? GetMaxAmmo(type) : GetAmmo(type);
            }
            return sum;
        }

        public bool Equals(AirDefenseUiStatsSnapshot other)
        {
            if (AaStations != other.AaStations
                || AaAmmo != other.AaAmmo
                || AaMaxAmmo != other.AaMaxAmmo)
            {
                return false;
            }

            for (int i = 0; i < TypeCount; i++)
            {
                var t = (AAType)i;
                if (GetAmmo(t) != other.GetAmmo(t)
                    || GetMaxAmmo(t) != other.GetMaxAmmo(t)
                    || GetCount(t) != other.GetCount(t))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is AirDefenseUiStatsSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(AaStations);
            hash.Add(AaAmmo);
            hash.Add(AaMaxAmmo);
            for (int i = 0; i < TypeCount; i++)
            {
                var t = (AAType)i;
                hash.Add(GetAmmo(t));
                hash.Add(GetMaxAmmo(t));
                hash.Add(GetCount(t));
            }
            return hash.ToHashCode();
        }
    }
}
