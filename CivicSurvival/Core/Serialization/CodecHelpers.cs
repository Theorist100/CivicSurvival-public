using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Serialization
{
    internal static class CodecMath
    {
        internal static int SnapToPreset(int value, IReadOnlyList<int> presets)
        {
            if (presets.Count == 0)
                return value;

            int best = presets[0];
            long bestDistance = Math.Abs((long)value - best);
            for (int i = 1; i < presets.Count; i++)
            {
                long distance = Math.Abs((long)value - presets[i]);
                if (distance < bestDistance)
                {
                    best = presets[i];
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
