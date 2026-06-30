using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    internal readonly struct AirDefenseUiStatsSnapshot : IEquatable<AirDefenseUiStatsSnapshot>
    {
        /// <summary>Number of AAType enum values; sizes the per-type ammo arrays.</summary>
        public const int TypeCount = 4;

        public static AirDefenseUiStatsSnapshot Empty => new(
            0, 0, 0, 0, 0, 0, 0,
            new int[TypeCount],
            new int[TypeCount]);

        public readonly int AaStations;
        public readonly int AaAmmo;
        public readonly int AaMaxAmmo;
        public readonly int HeritageBoforsCount;
        public readonly int BoforsCount;
        public readonly int GepardCount;
        public readonly int PatriotCount;

        // Per-AAType ammo, indexed by (int)AAType. Sum of all entries equals AaAmmo/AaMaxAmmo.
        // Each AA type spends its own unit (Patriot missiles, Bofors/Heritage rounds), so the
        // panel shows one bar per present type instead of one merged number.
        private readonly int[] m_AmmoByType;
        private readonly int[] m_MaxAmmoByType;

        public AirDefenseUiStatsSnapshot(
            int aaStations,
            int aaAmmo,
            int aaMaxAmmo,
            int heritageBoforsCount,
            int boforsCount,
            int gepardCount,
            int patriotCount,
            int[] ammoByType,
            int[] maxAmmoByType)
        {
            AaStations = aaStations;
            AaAmmo = aaAmmo;
            AaMaxAmmo = aaMaxAmmo;
            HeritageBoforsCount = heritageBoforsCount;
            BoforsCount = boforsCount;
            GepardCount = gepardCount;
            PatriotCount = patriotCount;
            m_AmmoByType = ammoByType;
            m_MaxAmmoByType = maxAmmoByType;
        }

        public int GetAmmo(AAType type)
        {
            int index = (int)type;
            return m_AmmoByType != null && (uint)index < (uint)m_AmmoByType.Length ? m_AmmoByType[index] : 0;
        }

        public int GetMaxAmmo(AAType type)
        {
            int index = (int)type;
            return m_MaxAmmoByType != null && (uint)index < (uint)m_MaxAmmoByType.Length ? m_MaxAmmoByType[index] : 0;
        }

        public bool Equals(AirDefenseUiStatsSnapshot other)
        {
            if (AaStations != other.AaStations
                || AaAmmo != other.AaAmmo
                || AaMaxAmmo != other.AaMaxAmmo
                || HeritageBoforsCount != other.HeritageBoforsCount
                || BoforsCount != other.BoforsCount
                || GepardCount != other.GepardCount
                || PatriotCount != other.PatriotCount)
            {
                return false;
            }

            for (int i = 0; i < TypeCount; i++)
            {
                var t = (AAType)i;
                if (GetAmmo(t) != other.GetAmmo(t) || GetMaxAmmo(t) != other.GetMaxAmmo(t))
                    return false;
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
            hash.Add(HeritageBoforsCount);
            hash.Add(BoforsCount);
            hash.Add(GepardCount);
            hash.Add(PatriotCount);
            for (int i = 0; i < TypeCount; i++)
            {
                var t = (AAType)i;
                hash.Add(GetAmmo(t));
                hash.Add(GetMaxAmmo(t));
            }
            return hash.ToHashCode();
        }
    }
}
