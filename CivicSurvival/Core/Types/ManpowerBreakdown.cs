namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Detailed manpower breakdown for UI display.
    /// </summary>
    public readonly struct ManpowerBreakdown
    {
        public readonly int Population;
        public readonly int BasePool;

        public readonly float PatriotismFactor;      // 0.5 - 1.0
        public readonly float MoraleFactor;       // 0.5 - 1.0
        public readonly float FatigueFactor;      // 0.85 or 1.0
        public readonly float ConscriptionBonus;  // 0.0 or 0.5

        public readonly int TotalManpower;
        public readonly int UsedManpower;
        public readonly int AvailableManpower;
        public readonly int Casualties;

        public readonly int WarDay;
        public readonly bool IsWarFatigued;
        public readonly bool IsConscriptionActive;

        public ManpowerBreakdown(
            int population, int basePool,
            float patriotismFactor, float moraleFactor, float fatigueFactor, float conscriptionBonus,
            int total, int used, int available, int casualties,
            int warDay, bool isFatigued, bool isConscription)
        {
            Population = population;
            BasePool = basePool;
            PatriotismFactor = patriotismFactor;
            MoraleFactor = moraleFactor;
            FatigueFactor = fatigueFactor;
            ConscriptionBonus = conscriptionBonus;
            TotalManpower = total;
            UsedManpower = used;
            AvailableManpower = available;
            Casualties = casualties;
            WarDay = warDay;
            IsWarFatigued = isFatigued;
            IsConscriptionActive = isConscription;
        }
    }
}
