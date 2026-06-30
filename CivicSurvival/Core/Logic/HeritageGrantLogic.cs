using Unity.Mathematics;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Logic
{
    public static class HeritageGrantLogic
    {
        public static int CalculateHeritageCount(int productionMW)
        {
            var cfg = BalanceConfig.Current.AAUnits;
            int mwPerAA = math.max(cfg.MwPerAA, 1);
            int count = cfg.HeritageBaseCount + (productionMW / mwPerAA);
            int clampMin = math.min(cfg.HeritageMinCount, cfg.HeritageMaxCount);
            int clampMax = math.max(cfg.HeritageMinCount, cfg.HeritageMaxCount);
            return math.clamp(count, clampMin, clampMax);
        }
    }
}
