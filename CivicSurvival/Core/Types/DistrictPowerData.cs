using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Power consumption data for a district.
    /// Stored in kW internally, MW values pre-computed for display.
    /// </summary>
    public struct DistrictPowerData
    {
        // Internal: stored in kW for precision
        public int TotalKW;
        public int ResidentialKW;
        public int CommercialKW;
        public int IndustrialKW;
        public int OfficeKW;
        public int ServicesKW;
        public int BuildingCount;

        // Display: pre-computed MW values (call ComputeMW() after setting KW)
        public int TotalMW;
        public int ResidentialMW;
        public int CommercialMW;
        public int IndustrialMW;
        public int OfficeMW;
        public int ServicesMW;

        /// <summary>
        /// Pre-compute MW values from KW. Call once after data collection.
        /// </summary>
        public void ComputeMW()
        {
            // TotalMW is the canonical rounded figure. Category MW are apportioned from
            // it with the largest-remainder method so the breakdown always sums to
            // TotalMW exactly. Rounding each field independently makes Σ round(kw_i)
            // drift from round(Σ kw_i) by up to ±(N/2) MW — and the UI drilldown
            // (sum of categories) and the district row (TotalMW) must agree.
            TotalMW = RoundKwToMw(TotalKW);

            Span<int> kw = stackalloc int[5] { ResidentialKW, CommercialKW, IndustrialKW, OfficeKW, ServicesKW };
            Span<int> mw = stackalloc int[5];
            Span<int> remainder = stackalloc int[5];

            int floorSum = 0;
            for (int i = 0; i < 5; i++)
            {
                int v = kw[i];
                int floorMw = v / 1000;
                mw[i] = floorMw;
                remainder[i] = v - floorMw * 1000;
                floorSum += floorMw;
            }

            // Distribute the rounding budget (TotalMW − Σ floor) to the largest
            // remainders first. Budget is bounded by the category count, so this is
            // O(N²) over 5 fixed buckets — Burst-safe, no allocation.
            int budget = TotalMW - floorSum;
            while (budget > 0)
            {
                int best = -1;
                int bestRemainder = -1;
                for (int i = 0; i < 5; i++)
                {
                    if (remainder[i] > bestRemainder)
                    {
                        bestRemainder = remainder[i];
                        best = i;
                    }
                }
                if (best < 0) break;
                mw[best]++;
                remainder[best] = -1; // consume this bucket so it is not picked twice
                budget--;
            }

            ResidentialMW = mw[0];
            CommercialMW = mw[1];
            IndustrialMW = mw[2];
            OfficeMW = mw[3];
            ServicesMW = mw[4];
        }

        private static int RoundKwToMw(int kw)
        {
            return (kw + (kw >= 0 ? 500 : -500)) / 1000;
        }
    }
}
