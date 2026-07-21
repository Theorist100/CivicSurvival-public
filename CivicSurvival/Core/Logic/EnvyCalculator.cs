using Unity.Burst;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: neighbor envy pressure.
    /// Extracted from NeighborEnvySystem.WritePressureJob.
    ///
    /// Note: Spatial search (who has envied neighbors) is done by NeighborEnvySystem.
    /// This calculator only converts EnvyAffected tag to pressure value.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class EnvyCalculator
    {
        /// <summary>
        /// Calculate envy pressure.
        /// </summary>
        /// <param name="hasPower">True if household has power (no envy possible)</param>
        /// <param name="isEnvyAffected">True if building tagged as EnvyAffected (neighbors have power)</param>
        /// <param name="envyStress">Configured envy stress scalar (0-1)</param>
        /// <returns>Pressure value (0 or configured envy stress)</returns>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static float Calculate(bool hasPower, bool isEnvyAffected, float envyStress)
        {
            // If has power, no envy
            if (hasPower)
                return 0f;

            // If tagged as EnvyAffected (neighbors have power), apply stress
            return isEnvyAffected ? math.saturate(envyStress) : 0f;
        }
    }
}
