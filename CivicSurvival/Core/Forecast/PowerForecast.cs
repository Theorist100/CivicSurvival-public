using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer power model: composes the generation-saturation (<see cref="SaturationLogic"/>)
    /// and thermal-fuel (<see cref="FuelCurveLogic"/>) leaf formulas into the effective production the
    /// crisis sweep drives. NOT new balance arithmetic — lifted verbatim out of CrisisSweepSystem so
    /// the power composition is single-source and can be diffed against the runtime
    /// <c>PowerCapacityMath.ComputeEffectiveFactor</c> (the Core home both runtime and forecast
    /// reference) independently.
    ///
    /// variant D: same SaturationLogic / FuelCurveLogic the runtime resolver calls.
    /// </summary>
    public static class PowerForecast
    {
        /// <summary>N+1 unit buffer = the per-plant headroom, capped at the config cap.</summary>
        public static float UnitBuffer(RemoteBalanceConfig cfg, float plantMW)
            => math.min(plantMW, cfg.GenerationSaturation.UnitBufferCapMW);

        /// <summary>Steady-state saturation target factor for the given fleet vs demand
        /// (surplus-degradation curve). The instantaneous target, before inertia stepping.</summary>
        public static float SaturationTarget(RemoteBalanceConfig cfg, float nameplate, float demand, int intermittentTypes, float plantMW)
            => SaturationLogic.ComputeTargetFactor(
                nameplate, demand, intermittentTypes,
                cfg.GenerationSaturation.HeadroomBase, cfg.GenerationSaturation.HeadroomPerType,
                cfg.GenerationSaturation.SaturationSoftness, cfg.GenerationSaturation.SaturationFloor,
                UnitBuffer(cfg, plantMW));

        /// <summary>Thermal fuel-stockpile output factor at the given fuel fraction (sigmoid curve).</summary>
        public static float FuelFactor(RemoteBalanceConfig cfg, float fuelFraction)
            => FuelCurveLogic.ComputeFuelFactor(
                fuelFraction, cfg.FuelCurve.Enabled,
                cfg.FuelCurve.BufferThreshold, cfg.FuelCurve.MinOutputAtZero,
                cfg.FuelCurve.AnchorFrac, cfg.FuelCurve.AnchorOutput,
                cfg.FuelCurve.SteepnessLow, cfg.FuelCurve.SteepnessHigh);

        /// <summary>Effective production = nameplate · steadyState(saturation) · fuel — a deliberate
        /// subset of the runtime fold.</summary>
        // FORECAST-APPROX: deliberate subset of PowerCapacityMath.ComputeEffectiveFactor (the
        // single runtime source for the full damage × construction × saturation × fuel chain). The
        // forecast folds ONLY saturation × fuel and pins damage = 1: a city-level production
        // projection models the healthy fleet's steady state, not which individual plants an enemy
        // strike has knocked out (per-plant damage is a runtime/strike-resolution fact, not a
        // forward-looking aggregate). Same SaturationLogic / FuelCurveLogic leaf curves as runtime;
        // only the structural terms are omitted on purpose.
        // FORECAST-APPROX: construction ramp (PowerCapacityMath.ComputeConstructionProgress) is
        // omitted entirely — construction = 1. The forecast treats all plants as completed
        // nameplate; the build-window ramp is a transient that a steady-state aggregate ignores.
        public static float EffectiveProduction(RemoteBalanceConfig cfg, float nameplate, float demand, int intermittentTypes, float plantMW, float fuelFraction)
            => nameplate * SaturationTarget(cfg, nameplate, demand, intermittentTypes, plantMW) * FuelFactor(cfg, fuelFraction);

        /// <summary>surplusRatio = nameplate / (peakDemand + N+1 unit buffer), floored at 1 — the
        /// surplus surcharge base. ALWAYS from raw nameplate, never effective production, so a spam
        /// city cannot hide its over-build behind the saturation cut it caused.</summary>
        public static float SurplusRatio(RemoteBalanceConfig cfg, float nameplate, float peakDemand, float plantMW)
        {
            float denom = math.max(peakDemand + UnitBuffer(cfg, plantMW), 0.001f);
            return math.max(1f, nameplate / denom);
        }

        /// <summary>Step the saturation factor one tick with asymmetric inertia (down-instant /
        /// up-slow), from the previous factor toward the new target for the available fleet.</summary>
        public static float StepSaturation(RemoteBalanceConfig cfg, float prev, float nameplate, float demand, int intermittentTypes, float plantMW, float dt)
            => SaturationLogic.StepInertia(
                prev,
                SaturationTarget(cfg, nameplate, demand, intermittentTypes, plantMW),
                dt, cfg.GenerationSaturation.Hysteresis, cfg.GenerationSaturation.TauUpHours);
    }
}
