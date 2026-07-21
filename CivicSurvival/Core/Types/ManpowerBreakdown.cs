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
        public readonly float DodgerFactor;       // 1 - DodgerMaxPenalty .. 1.0
        public readonly float ConscriptionBonus;  // 0.0 or 0.5

        public readonly int TotalManpower;
        public readonly int UsedManpower;
        public readonly int AvailableManpower;
        public readonly int Casualties;
        public readonly int DodgerCount;          // heads lost to draft evasion
        public readonly int DeferralManpower;     // heads removed by the sold-deferral rate (reversible)
        public readonly int DisabilityManpower;   // heads permanently sold off as fake disability

        public readonly int WarDay;
        public readonly bool IsWarFatigued;
        public readonly bool IsConscriptionActive;

        public ManpowerBreakdown(
            int population, int basePool,
            float patriotismFactor, float moraleFactor, float fatigueFactor, float dodgerFactor, float conscriptionBonus,
            int total, int used, int available, int casualties, int dodgerCount,
            int deferralManpower, int disabilityManpower,
            int warDay, bool isFatigued, bool isConscription)
        {
            Population = population;
            BasePool = basePool;
            PatriotismFactor = patriotismFactor;
            MoraleFactor = moraleFactor;
            FatigueFactor = fatigueFactor;
            DodgerFactor = dodgerFactor;
            ConscriptionBonus = conscriptionBonus;
            TotalManpower = total;
            UsedManpower = used;
            AvailableManpower = available;
            Casualties = casualties;
            DodgerCount = dodgerCount;
            DeferralManpower = deferralManpower;
            DisabilityManpower = disabilityManpower;
            WarDay = warDay;
            IsWarFatigued = isFatigued;
            IsConscriptionActive = isConscription;
        }
    }
}
