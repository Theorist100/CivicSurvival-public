using CivicSurvival.Core.Components.Diagnostics;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Pure (non-ECS) composer for the in-game crisis sweep — the C# replacement for
    /// <c>Tools/crisis_model.py</c>. Runs one of three models (invariant / pacing / severity) using
    /// ONLY the shared <c>Core/Logic</c> + <c>Core/Forecast</c> balance formulas plus the
    /// generation-spam axes (saturation / fuel / surplus / demand-peak), reading every live-or-archetype
    /// input from a pre-gathered <see cref="LiveInputs"/> snapshot the orchestrating
    /// <c>CrisisSweepSystem</c> assembles.
    ///
    /// variant D: the sweep and the runtime call the SAME compiled formulas, so the predicted crisis
    /// can never drift from the simulated one. The archetype shape, real↔game time scale, player policy
    /// and per-drone shot count are explicit assumption params on the request, never baked here.
    ///
    /// This class touches NO ECS: no SystemAPI, no queries, no readers — the orchestrator gathers all
    /// live data into <see cref="LiveInputs"/> (a one-shot snapshot per sweep) and the per-plant
    /// severity scratch into <see cref="CrisisSweepScratch"/>, so the composition is a deterministic
    /// function of (request, live, scratch, BalanceConfig.Current).
    /// </summary>
    public sealed class CrisisSweepRunner
    {
        // ===== Archetype presets (MODELING ASSUMPTION — map "shape", crisis_model.py:60-67) =====
        // index 0 = dense_urban, 1 = balanced_town, 2 = sprawling_agri.
        private static readonly float[] s_MwPer1k = { 50f, 40f, 25f };
        private static readonly float[] s_Km2Per1k = { 0.20f, 0.50f, 1.50f };
        private static readonly float[] s_PlantMW = { 80f, 40f, 20f };
        private const int ARCHETYPE_COUNT = 3;

        // Pacing acceptance gate: Calm must stay ≥ 25% of the cycle (crisis_model.py:516).
        private const float PACING_CALM_MIN_FRACTION = 0.25f;
        // Reference wave # for the deterministic modes (the tool's --wave default 20).
        private const int WORST_CASE_WAVE_NUMBER = 20;

        // Archetype demand is quoted per 1000 residents (crisis_model.py mw_per_1k / km2_per_1k).
        private const float RESIDENTS_PER_DEMAND_UNIT = 1000f;
        // Invariant built capacity sits 10% over demand (crisis_model.py:362 production = demand·1.10).
        private const float INVARIANT_NAMEPLATE_MARGIN = 1.10f;
        // Severity tick length in game-hours (crisis_model.py:560 dt = 0.25).
        private const float SEVERITY_TICK_HOURS = 0.25f;
        // Real-minute → second scale for the game-day clock conversion.
        private const float SECONDS_PER_MINUTE = 60f;
        // Per-plant model: nameplate is split into discrete plants (crisis_model.py:533-534) so the
        // repair timeline (per-plant damage + repair slots) matches PlantHitMath/RepairPaymentHelper.
        private const int MIN_PLANTS = 1;
        // Worst-case plant count the per-plant severity scratch is sized to. A megacity can build
        // far more than the old 40-plant cap, so the cap is generous (the orchestrator allocates the
        // scratch ONCE in OnCreate to MAX_PLANTS — CIVIC050 forbids growing it in OnUpdate). When live
        // plants exceed the cap they are clamped to it (the timeline samples a representative fleet).
        // Public so the orchestrator sizes its reused scratch against the SAME definition the severity
        // clamp uses (one source of truth shared across the split).
        public const int MAX_PLANTS = 256;
        // Severity Monte-Carlo budget caps. The timeline runs fully synchronous on the main thread
        // (PostSimulation), so a crafted request must not be able to spin it for millions of ticks: a
        // request is clamped to [1, MAX] on both axes. 365 days × 100 runs is already a generous
        // diagnostic envelope (the panel defaults are 180 / 30).
        private const int MAX_SEVERITY_DAYS = 365;
        private const int MAX_SEVERITY_RUNS = 100;
        // Repair-tier selectors on CrisisSweepRequest.RepairTier (mirror crisis_model.py tier strings).
        private const byte REPAIR_TIER_MUNICIPAL = 1;
        private const byte REPAIR_TIER_SHADOW = 2;
        // Worst-case repair bill basis for the Phase F cash-gate: a full 100%-wear repair. Plant repair is
        // billed flat per wear-percent (RepairPaymentHelper), so unit cost = this × cost-per-percent.
        private const long FULL_WEAR_PERCENT = 100L;

        // ════════════════════════════════════════════════════════════════════
        // Dispatch
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compose the verdict for one request from the pre-gathered live snapshot. <paramref name="populationPeak"/>
        /// and <paramref name="warDay"/> come from the scenario singleton (read ECS-side by the orchestrator);
        /// <paramref name="computedAtGameHours"/> from the game clock. PURE: no ECS access.
        /// </summary>
        public CrisisSweepResultSingleton RunSweep(
            in CrisisSweepRequest req, in LiveInputs live, in CrisisSweepScratch scratch,
            int populationPeak, int warDay, double computedAtGameHours)
        {
            int archetype = math.clamp(req.ArchetypePreset, 0, ARCHETYPE_COUNT - 1);

            var result = new CrisisSweepResultSingleton
            {
                Mode = (byte)req.Mode,
                HasResult = true,
                ComputedAtGameHours = computedAtGameHours,
                ArchetypeId = archetype,
                PopulationPeak = populationPeak,
                WarDay = warDay,
            };

            switch (req.Mode)
            {
                case CrisisSweepMode.Invariant:
                    RunInvariant(req, live, archetype, populationPeak, ref result);
                    break;
                case CrisisSweepMode.Pacing:
                    RunPacing(req, live, archetype, populationPeak, ref result);
                    break;
                case CrisisSweepMode.Severity:
                    RunSeverity(req, live, scratch, archetype, populationPeak, ref result);
                    break;
                default:
                    // Unknown mode — no verdict computed (the orchestrator logs it).
                    result.HasResult = false;
                    break;
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        // invariant — deterministic worst-case recoverability (no RNG, no policy)
        // ════════════════════════════════════════════════════════════════════

        private static void RunInvariant(in CrisisSweepRequest req, in LiveInputs live, int archetype, int populationPeak, ref CrisisSweepResultSingleton result)
        {
            var cfg = BalanceConfig.Current;
            float archetypeArea = (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_Km2Per1k[archetype];

            // LIVE-OR-ARCHETYPE as ONE coherent snapshot. A live power snapshot ⇒ a loaded city, so the
            // whole verdict switches base together: live production, live demand, the live war-day AND the
            // live defendable area all come from the current city, never mixed with archetype demand or a
            // deep-fatigue worst-case war-day. The verdict then means "this city's recoverability against a
            // worst-case strike at the CURRENT war-day". The archetype fallback is unchanged — archetype
            // demand + area, the deep-fatigue worst-case war-day (WarFatigueDay+1) and the archetype
            // production model — so it stays byte-identical to the pre-Tier-0 model when no city is loaded.
            bool manpowerLive = live.TryGetManpower(
                out int liveManpowerTotal, out int liveManpowerUsed, out int liveManpowerCasualties,
                out int liveManpowerAvailable, out int liveManpowerWarDay);

            int warDay;
            float demand;
            float production;
            int pool;
            float area;
            // plantMW drives the per-hit ballistic damage slice (mean plant size; the largest live plant
            // in live mode, the archetype plant size in the fallback). Resolved per branch alongside
            // production below.
            float plantMW;
            if (live.TryGetPower(out float liveProductionMW, out _, out float liveLargestPlantMW, out _, out _))
            {
                // AREA — gated on the SAME power-live condition as demand/production/war-day so the
                // invariant snapshot is all-live (Phase E). The live defendable footprint (built
                // districts, else owned tiles); falls back to archetype area if the area queries are
                // empty mid-hydration (live area is 0).
                area = live.TryGetArea(out float liveArea) ? liveArea : archetypeArea;

                // Live war-day from the manpower snapshot (the source that nets the pool against this
                // day's fatigue); fall back to the scenario war-day captured in result.WarDay when the
                // mobilization singleton is pre-war. result.WarDay already holds scenario.WarDay here.
                warDay = manpowerLive && liveManpowerWarDay >= 0 ? liveManpowerWarDay : result.WarDay;

                // Live demand, same source as pacing/severity (24h peak → instant), so production and
                // demand share a base. Fall back to archetype-pop demand only if the grid is silent.
                demand = live.TryGetDemand(out float liveDemand)
                    ? liveDemand
                    : (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];

                // production = own-city dispatchable MW (saturation/fuel/weather already folded into the
                // real output, so intermittent diversity is implicit).
                production = liveProductionMW;
                // Mean plant slice for the ballistic per-hit damage — the largest live plant MW (the same
                // N+1-buffer base the pacing/severity reads use), floored at 1 so a degenerate snapshot
                // does not zero the slice.
                plantMW = math.max(liveLargestPlantMW, 1f);

                // The pool is the live TOTAL manpower. The crew gate below
                // (OperationalFraction = min(1, pool / crewDemand)) re-applies the placed fleet's
                // crew cost, so feeding it the already-net Available DOUBLE-subtracts crew: a
                // fully-manned fleet has Used == Total ⇒ Available 0 ⇒ opAA 0 ⇒ coverage/intercept/
                // recoverable all collapse to garbage even though 13 launchers are live and firing.
                // Total is the manpower the placed fleet draws from; the gate answers "is there
                // enough to man it" correctly for both the fully-crewed and the over-demanded case.
                // Pre-war (no live manpower) falls to the recruitable pool at the live war-day.
                if (manpowerLive)
                {
                    pool = liveManpowerTotal;
                    result.ManpowerTotal = liveManpowerTotal;
                    result.ManpowerUsed = liveManpowerUsed;
                    result.ManpowerCasualties = liveManpowerCasualties;
                    result.ManpowerAvailable = liveManpowerAvailable;
                }
                else
                {
                    pool = ManpowerForecast.Pool(populationPeak, req.Patriotism, req.Morale, warDay, isConscription: false);
                }
            }
            else
            {
                // Deep-fatigue worst case: one day past the config war-fatigue threshold (was a hardcoded
                // 181; derive from config so a raised Scenario.WarFatigueDay does not silently un-worst-case
                // it). Archetype demand + archetype AREA + archetype production model — byte-identical to
                // pre-Tier-0 (no power snapshot ⇒ no city ⇒ live area would be 0 anyway, but gating area
                // on the power-live branch keeps the snapshot all-live-or-all-archetype, never mixed).
                area = archetypeArea;
                warDay = cfg.Scenario.WarFatigueDay + 1;
                demand = (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];
                float nameplate = demand * INVARIANT_NAMEPLATE_MARGIN;
                production = PowerForecast.EffectiveProduction(cfg, nameplate, demand, intermittentTypes: 0, s_PlantMW[archetype], req.FuelFraction);
                plantMW = s_PlantMW[archetype];
                pool = ManpowerForecast.Pool(populationPeak, req.Patriotism, req.Morale, warDay, isConscription: false);
            }

            result.WarDay = warDay;
            // Additive verdict driver: the footprint (km²) the verdict's coverage/leak used (live or
            // archetype), assigned from the already-resolved local — no change to any existing math.
            result.AreaKm2 = area;

            // AA — live mixed fleet (real placed Heritage/Bofors/Gepard/Patriot, each with its own
            // config crew/range/intercept) when the AirDefense owner reports a non-empty fleet;
            // otherwise the FREE Heritage grant crewed/covering at the Heritage approximation. The
            // archetype fallback path (grant + OperationalAa + Coverage + Leak) is byte-identical to
            // the pre-live model. opAa is reported for the verdict; in live mode it is the summed
            // operational fleet across types.
            int grant = HeritageGrantLogic.CalculateHeritageCount((int)production);
            float grace = GridStressLogic.ScaledCollapseThreshold(cfg.GridStress, populationPeak);
            float baseChance = cfg.AAUnits.HeritageInterceptShahed;

            int opAa;
            // Per-branch drone leak + leaked-ballistic plant damage (MW). The recovery verdict is
            // computed for BOTH branches below — no gate on the live toggle (the sweep is a DECISION
            // tool: the survivability verdict must NOT flip when the player toggles). Branch A = Patriot
            // reserved for ballistics, branch B = Patriot mixed.
            float leakA;
            float leakB;
            float ballisticDamageMwA;
            float ballisticDamageMwB;
            if (live.TryGetFleet(out var fleet))
            {
                bool patriotDrones = live.PatriotInterceptsDrones;
                opAa = AirDefenseForecast.OperationalAaFleet(cfg, pool, fleet);

                // BOTH branches computed every run (the sweep is a DECISION tool — show the trade-off,
                // do not gate on the live toggle). Branch A = Patriot reserved for ballistics
                // (patriotInterceptsDrones=false): weaker drone layer, stronger ballistic line. Branch B
                // = Patriot mixed (true): stronger drone layer, ballistic line down to the Gepard backstop.
                leakA = AirDefenseForecast.FleetLeak(cfg, pool, fleet, area, req.ShotsPerDrone, patriotInterceptsDrones: false);
                leakB = AirDefenseForecast.FleetLeak(cfg, pool, fleet, area, req.ShotsPerDrone, patriotInterceptsDrones: true);

                result.DroneInterceptBallisticOnly = 100f * (1f - leakA);
                result.DroneInterceptMixed = 100f * (1f - leakB);

                // Additive verdict drivers: the live fleet by type and the coverage of the ACTIVE branch
                // (FleetCoverage re-derives the SAME min(1, Σ coverShare) FleetLeak uses internally).
                result.AaHeritage = fleet.Heritage;
                result.AaBofors = fleet.Bofors;
                result.AaGepard = fleet.Gepard;
                result.AaPatriot = fleet.Patriot;
                result.CoveragePct = 100f * AirDefenseForecast.FleetCoverage(cfg, pool, fleet, area, patriotDrones);

                // Ballistic line for BOTH branches — worst-case wave #20 at live production. The number of
                // ballistics is the runtime spawner's own formula (variant D). Branch A keeps the Patriot
                // on the line (high); branch B sends it to drones, leaving only the Gepard backstop (low).
                int ballisticTargets = AirDefenseForecast.BallisticTargetsForWave(cfg, (int)production, WORST_CASE_WAVE_NUMBER);
                var ballisticA = AirDefenseForecast.BallisticIntercept(cfg, pool, fleet, area, (int)production, ballisticTargets, patriotInterceptsDrones: false);
                var ballisticB = AirDefenseForecast.BallisticIntercept(cfg, pool, fleet, area, (int)production, ballisticTargets, patriotInterceptsDrones: true);
                result.BallisticInterceptBallisticOnly = 100f * ballisticA.InterceptFraction;
                result.BallisticInterceptMixed = 100f * ballisticB.InterceptFraction;
                result.BallisticTargets = ballisticA.BallisticTargets;
                result.MissilesSpentOnDrones = ballisticB.MissilesSpentOnDrones;
                result.PatriotInterceptsDrones = patriotDrones;

                // Fold the leaked ballistics into the recovery: the missiles the line did NOT intercept
                // strike stations. Same per-hit MW slice the severity timeline uses (PlantHitMath via
                // RepairForecast.LossPerHit, scaled to plantMW), so the invariant and the severity speak
                // the same damage language. Branch B leaks far more ballistics (the Patriot magazine left
                // the line), so its damage term is the price of its higher drone layer.
                ballisticDamageMwA = LeakedBallisticDamageMW(cfg, ballisticTargets, ballisticA.InterceptFraction, plantMW, production);
                ballisticDamageMwB = LeakedBallisticDamageMW(cfg, ballisticTargets, ballisticB.InterceptFraction, plantMW, production);
            }
            else
            {
                opAa = AirDefenseForecast.OperationalAa(cfg, pool, grant);
                float fCov = AirDefenseForecast.Coverage(cfg, opAa, area);  // manned AA only

                // Additive coverage driver — the same fCov the archetype leak below already uses (no
                // fleet-by-type in archetype mode, so the Aa* counts stay 0).
                result.CoveragePct = 100f * fCov;

                // DEFENSE-DRIVEN loss (Phase 11): the surviving generation is the share the air
                // defense actually protects, NOT the count of plants a single wave's drones knock
                // out. leak = AirDefenseForecast.Leak(fCov, baseChance, shots) already folds the three
                // defense drivers — coverage (fCov from opAa·disc/area), crew (opAa = min(grant, pool/crew))
                // and ammo/per-shot accuracy (PerDroneSurvive over ShotsPerDrone) — plus the
                // per-wave leak floor. An undefended zone (fCov → 0 ⇒ leak → 1) is physically
                // overrun: across a deep-war campaign (worst-case wave #20) its generation is
                // driven to zero regardless of how many drones one wave carries, which is the
                // runtime reality the old drone-count·per-hit mapping understated by ~2 orders of
                // magnitude on a megacity (MaxThreats=24 capped the swarm while the plant count
                // grew linearly). So the loss is the unprotected fraction of production, and AA
                // *saves* generation only to the extent it is placed, crewed and covering. The live
                // mixed-fleet path (FleetLeak above) folds the SAME three drivers per type.
                float leak = AirDefenseForecast.Leak(cfg, fCov, baseChance, req.ShotsPerDrone);

                // Archetype fallback has no Patriot (the FREE Heritage grant only), so the two drone
                // branches are identical — report both at the single archetype leak. The ballistic
                // branches stay 0: the archetype model carries no anti-ballistic fleet, so the leaked-
                // ballistic damage is 0 for both branches too (recovery is drone-leak only here).
                leakA = leak;
                leakB = leak;
                ballisticDamageMwA = 0f;
                ballisticDamageMwB = 0f;
                result.DroneInterceptBallisticOnly = 100f * (1f - leak);
                result.DroneInterceptMixed = 100f * (1f - leak);
            }

            // Recovery for BOTH branches — drone leak + leaked-ballistic plant damage, no toggle gate. A
            // branch's post-strike production = production − (drone-leaked production + leaked-ballistic
            // damage), floored at 0. Recoverable = it survives the unsheddable floor. Mixed (B) is no
            // longer automatically better: its lower drone leak is offset by the ballistic hole.
            float unsheddable = demand * req.UnsheddableFrac;
            float afterA = math.max(0f, production - (production * leakA + ballisticDamageMwA));
            float afterB = math.max(0f, production - (production * leakB + ballisticDamageMwB));

            result.WorstCaseRecoveryBallisticOnly = production > 0f ? 100f * afterA / production : 0f;
            result.WorstCaseRecoveryMixed = production > 0f ? 100f * afterB / production : 0f;
            result.IsRecoverableBallisticOnly = afterA > 0f && afterA >= unsheddable;
            result.IsRecoverableMixed = afterB > 0f && afterB >= unsheddable;
            result.GraceWindowHours = grace;
            result.FreeHeritageGrant = grant;
            result.OperationalAaAtVerdict = opAa;
            result.UnsheddableFloorMW = (int)unsheddable;
        }

        // ════════════════════════════════════════════════════════════════════
        // pacing — deterministic phase-cycle timing (no manpower, no intercept)
        // ════════════════════════════════════════════════════════════════════

        private static void RunPacing(in CrisisSweepRequest req, in LiveInputs live, int archetype, int populationPeak, ref CrisisSweepResultSingleton result)
        {
            // The panel trigger fills one mode-agnostic payload, so the severity-only request fields
            // (Days, Runs, Seed, RepairTier, MaxConcurrentRepairs, IsConscription, Shed) arrive populated
            // here too but are deliberately INERT in pacing — pacing reads only the power + wave + surplus
            // inputs below, never the Monte-Carlo / repair / policy fields. Documented so a future reader
            // does not wire one in by accident expecting a pacing-specific value.
            var cfg = BalanceConfig.Current;
            int waveNum = WORST_CASE_WAVE_NUMBER;

            // POWER — live-or-archetype. Live: nameplate / plantMW from the real fleet, demand from the
            // 24h demand peak (instantaneous demand when the ring is still cold). Archetype fallback:
            // demand = pop·mw_per_1k, nameplate = demand·(1+reserve), plantMW = archetype plant size.
            float demand;
            float nameplate;
            float plantMW;
            if (live.TryGetPower(out _, out float liveNameplateMW, out float liveLargestPlantMW, out _, out _))
            {
                nameplate = liveNameplateMW;
                plantMW = math.max(liveLargestPlantMW, 1f);
                demand = live.TryGetDemand(out float peakMW)
                    ? peakMW
                    : (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];
            }
            else
            {
                demand = (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];
                nameplate = demand * (1f + req.ReserveFrac);
                plantMW = s_PlantMW[archetype];
            }

            // WAVE — gated on the wave axis's OWN readiness (city loaded), NOT on the power snapshot, so
            // the two axes can load independently without breaking the byte-identical contract. Live: the
            // player's AirAttacks frequency preset and the vanilla-climate season for the calm window.
            // Archetype fallback keeps DefaultFrequencyMod (the deliberate fixed-scenario assumption) and
            // the neutral GetSeasonModifier(0), so the calm window is byte-identical to today when no city
            // is loaded.
            bool waveLive = live.TryGetWave(out float liveSeasonMod, out float liveFrequencyMod);
            float frequencyMod = waveLive ? liveFrequencyMod : cfg.Waves.DefaultFrequencyMod;
            float seasonMod = waveLive ? liveSeasonMod : WaveScalingService.GetSeasonModifier(0);

            // Phase mids — randomValue = 0.5 is exactly the (Min+Max)/2 midpoint the script reads.
            float alert = WaveForecast.PhaseDuration(GamePhase.Alert, waveNum, frequencyMod, 0.5f);
            float attack = WaveForecast.PhaseDuration(GamePhase.Attack, waveNum, frequencyMod, 0.5f);
            float recovery = WaveForecast.PhaseDuration(GamePhase.Recovery, waveNum, frequencyMod, 0.5f);

            // Pacing's worst case is the over-builder (shortest calm) — surplus surcharge in scope.
            float surplusRatio = PowerForecast.SurplusRatio(cfg, nameplate, demand, plantMW);
            float calmSeconds = WaveForecast.CalmSeconds((int)nameplate, seasonMod, frequencyMod, surplusRatio);

            float cycle = calmSeconds + alert + attack + recovery;
            float calmFraction = cycle > 0f ? calmSeconds / cycle : 0f;

            result.CalmHours = calmSeconds / GameRate.SECONDS_PER_HOUR;
            result.WavePressureAtPeak = calmFraction;
            result.GraceWindowHours = GridStressLogic.ScaledCollapseThreshold(cfg.GridStress, populationPeak);
            // Pacing's recoverability is the calm-fraction gate — it has no Patriot branches (no intercept
            // math at all), so both branch fields carry the SAME pacing verdict (the UI reads either).
            bool pacingRecoverable = calmFraction >= PACING_CALM_MIN_FRACTION;
            result.IsRecoverableBallisticOnly = pacingRecoverable;
            result.IsRecoverableMixed = pacingRecoverable;
        }

        // ════════════════════════════════════════════════════════════════════
        // severity — Monte-Carlo timeline (carries the unsheddable-floor fix)
        // ════════════════════════════════════════════════════════════════════

        private static void RunSeverity(in CrisisSweepRequest req, in LiveInputs live, in CrisisSweepScratch scratch, int archetype, int populationPeak, ref CrisisSweepResultSingleton result)
        {
            var cfg = BalanceConfig.Current;
            var gs = cfg.GridStress;
            var waves = cfg.Waves;

            int simDays = math.clamp(req.Days, 1, MAX_SEVERITY_DAYS);
            int runs = math.clamp(req.Runs, 1, MAX_SEVERITY_RUNS);
            float panic = gs.WarningThresholdYellow;
            float decay = gs.StressDecayRate;
            float recovDur = gs.RecoveryDurationHours;
            float thr = GridStressLogic.ScaledCollapseThreshold(gs, populationPeak);

            // AREA — live-or-archetype (Phase E): live defendable footprint (district, else owned
            // tiles) when a city is loaded; archetype pop·km2_per_1k otherwise (byte-identical when no
            // city is loaded). NOTE: unlike the invariant, severity selects each live input (area /
            // power / wave / manpower) on its OWN readiness rather than gating all of them on the power
            // snapshot — the Monte-Carlo timeline already varies war-day/pool per tick, so a transient
            // mix (live area + cold-power archetype) is an honest per-axis sample, not an incoherent
            // single-snapshot verdict. Live area still requires a loaded city (live area > 0).
            float area = live.TryGetArea(out float liveArea)
                ? liveArea
                : (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_Km2Per1k[archetype];
            float baseChance = cfg.AAUnits.HeritageInterceptShahed;

            // AA — live mixed fleet resolved once (the placed fleet is constant across the timeline);
            // each wave crew-gates it against the recomputed pool. Empty fleet ⇒ archetype Heritage path.
            bool fleetLive = live.TryGetFleet(out var fleet);
            bool patriotDrones = live.PatriotInterceptsDrones;

            // BOTH-branch campaign accumulators (the sweep is a decision tool — every wave evaluates both
            // "Patriot reserved for ballistics" (A) and "Patriot mixed" (B), regardless of the live
            // toggle). Drone-intercept is summed over every wave; ballistic-intercept over the
            // ballistic-carrying waves only. The readout is the campaign mean. Depletion of the Patriot
            // magazine across a wave train is NOT a separate stateful budget (each wave assumes a
            // topped-up magazine, FORECAST-APPROX pin (2)); the per-wave branch split captures the trade.
            double droneInterceptSumA = 0.0; // branch A = Patriot reserved for ballistics
            double droneInterceptSumB = 0.0; // branch B = Patriot mixed (on drones)
            int droneWaveCount = 0;
            double ballisticInterceptSumA = 0.0;
            double ballisticInterceptSumB = 0.0;
            int ballisticWaveCount = 0;
            long ballisticTargetSum = 0;
            long ballisticSpentOnDronesSum = 0;

            // BOTH-branch per-wave RECOVERY accumulators (the survivability verdict, decoupled from the
            // live toggle exactly like the invariant). Each wave's post-strike production = the surviving
            // nameplate this wave − (its drone-leaked share + its leaked-ballistic plant damage). The
            // recovery readout is the campaign mean of that %, and the recoverable flag is whether the
            // mean post-strike production clears the unsheddable floor. recoveryWaveCount counts the same
            // waves the drone accumulators do (every wave evaluates both branches).
            double recoverySumA = 0.0;
            double recoverySumB = 0.0;
            double afterSumA = 0.0; // Σ post-strike MW (branch A) — mean tested against the floor
            double afterSumB = 0.0;
            int recoveryWaveCount = 0;

            // POWER — live-or-archetype. Live: nameplate / per-plant sizes from the real fleet, demand
            // from the 24h peak (instantaneous when the ring is cold), intermittent-type count from the
            // snapshot. Archetype fallback: demand = pop·mw_per_1k, nameplate = demand·(1+reserve),
            // plants discretised at the archetype plant size (equal-plant, no per-plant scratch).
            // Live mode requires a snapshot WITH at least one real plant (an empty fleet has no
            // per-plant timeline to seed and falls back to the archetype discretisation).
            bool powerLive = live.TryGetPower(
                out _, out float liveNameplateMW, out _, out int liveIntermittentTypes, out int liveNPlants);
            bool liveMode = powerLive && liveNPlants > 0;

            float demand;
            float nameplate;
            int nPlants;
            float plantMW;
            float[]? plantCaps;
            int intermittentTypes;
            if (liveMode)
            {
                nameplate = liveNameplateMW;
                demand = live.TryGetDemand(out float peakMW)
                    ? peakMW
                    : (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];
#pragma warning disable CIVIC021 // No zero divisor: live mode is gated on liveNPlants > 0, so nPlants ≥ 1.
                nPlants = liveNPlants;
                plantMW = nameplate / nPlants;             // mean plant size — perHitLoss/saturation scalar
#pragma warning restore CIVIC021
                plantCaps = scratch.PlantCapMW;            // real per-plant sizes drive the damage→MW conversion
                intermittentTypes = liveIntermittentTypes;
            }
            else
            {
                demand = (populationPeak / RESIDENTS_PER_DEMAND_UNIT) * s_MwPer1k[archetype];
                nameplate = demand * (1f + req.ReserveFrac);
                // Split nameplate into discrete plants so the repair timeline tracks per-plant damage
                // exactly (crisis_model.py:533-534): n_plants from the archetype plant size, plantMW
                // resized so the fleet sums to nameplate. perHitLoss is scaled to THIS plantMW (+ the
                // fleet-share term off nameplate), matching the reference `loss_per_hit(plant_mw, nameplate)`.
#pragma warning disable CIVIC021 // No zero divisor: s_PlantMW entries are positive constants {80,40,20}; nPlants is clamp(...,MIN_PLANTS=1,...) ≥ 1.
                nPlants = math.clamp((int)math.ceil(nameplate / s_PlantMW[archetype]), MIN_PLANTS, MAX_PLANTS);
                plantMW = nameplate / nPlants;
#pragma warning restore CIVIC021
                plantCaps = null;                          // equal-plant discretisation, no per-plant sizes
                intermittentTypes = 0;
            }

            // WAVE — gated on the wave axis's OWN readiness (city loaded), NOT on the power snapshot, so
            // the two axes load independently. Live: the player's AirAttacks frequency preset and the
            // vanilla-climate season for the calm window. Archetype fallback keeps DefaultFrequencyMod
            // (the deliberate fixed-scenario assumption) and the neutral GetSeasonModifier(0) —
            // byte-identical to today when no city is loaded.
            bool waveLive = live.TryGetWave(out float liveSeasonMod, out float liveFrequencyMod);
            float freqMod = waveLive ? liveFrequencyMod : cfg.Waves.DefaultFrequencyMod;
            float seasonMod = waveLive ? liveSeasonMod : WaveScalingService.GetSeasonModifier(0);

            // MANPOWER — the severity pool RECOMPUTES Total per-wave along the warDay fatigue ramp
            // (it must keep decaying), but in live mode subtracts the live committed offset
            // (Used + Casualties) as a starting deduction. Archetype fallback: raw recomputed Pool.
            bool manpowerLive = live.TryGetManpower(
                out int liveManpowerTotal, out int liveManpowerUsed, out int liveManpowerCasualties,
                out int liveManpowerAvailable, out _);
            int liveCommitted = manpowerLive ? liveManpowerUsed + liveManpowerCasualties : 0;
            if (manpowerLive)
            {
                result.ManpowerTotal = liveManpowerTotal;
                result.ManpowerUsed = liveManpowerUsed;
                result.ManpowerCasualties = liveManpowerCasualties;
                result.ManpowerAvailable = liveManpowerAvailable;
            }

            float perHitLoss = RepairForecast.LossPerHit(cfg, plantMW, nameplate);

            float unsheddable = demand * req.UnsheddableFrac;
            float surplusRatio = PowerForecast.SurplusRatio(cfg, nameplate, demand, plantMW);

            float ghPerSecond = GameRate.HOURS_PER_DAY / math.max(req.GameDayRealMinutes * SECONDS_PER_MINUTE, 1f);
            const float dt = SEVERITY_TICK_HOURS; // game-hours per tick
            float horizon = simDays * GameRate.HOURS_PER_DAY;

            int placed = HeritageGrantLogic.CalculateHeritageCount((int)nameplate);

            // Per-tier flat repair duration (crisis_model.py:322-330 / RepairPaymentHelper): shadow
            // = 2h, municipal = 24h, tier 0 (none) ⇒ rep_h ≤ 0 disables repair entirely.
            float repHours = req.RepairTier switch
            {
                REPAIR_TIER_SHADOW => cfg.InfrastructureRepair.ShadowOpsRepairHours,
                REPAIR_TIER_MUNICIPAL => cfg.InfrastructureRepair.MunicipalRepairHours,
                _ => 0f,
            };
            bool repairEnabled = repHours > 0f;

            // REPAIR CASH-GATE (Phase F) — how many plants the player can bankroll under repair at once,
            // derived from the live funding pot the chosen RepairTier draws on (Phase F replaces the
            // panel's manual stand-in). Plant repair is billed flat per wear-percent
            // (RepairPaymentHelper: Cost = wearPercent × costPerPercent), NOT scaled by plant size, so the
            // worst-case unit cost is a full 100%-wear repair = 100 × costPerPercent — independent of MW.
            // slots = floor(pot / unitCost), clamped to the live fleet (can't repair more plants than
            // exist). Archetype fallback (no city loaded): the request's manual MaxConcurrentRepairs.
            int maxRepairs;
            long repairFundingCash;
            bool repairBudgetLive = live.TryGetRepairBudget(out long municipalCash, out long shadowCash);
            if (repairBudgetLive)
            {
                long cash;
                long unitCost;
                switch (req.RepairTier)
                {
                    case REPAIR_TIER_SHADOW:
                        cash = shadowCash;
                        unitCost = FULL_WEAR_PERCENT * cfg.InfrastructureRepair.ShadowOpsBaseCostPerPercent;
                        break;
                    case REPAIR_TIER_MUNICIPAL:
                        cash = municipalCash;
                        unitCost = FULL_WEAR_PERCENT * cfg.InfrastructureRepair.MunicipalBaseCostPerPercent;
                        break;
                    default:
                        // Tier 0 (none) — repHours ≤ 0 disables repair anyway, so maxRepairs is inert.
                        cash = 0L;
                        unitCost = 0L;
                        break;
                }
                maxRepairs = unitCost > 0L ? (int)math.min(cash / unitCost, MAX_PLANTS) : 0;
                repairFundingCash = cash;
            }
            else
            {
                maxRepairs = math.max(req.MaxConcurrentRepairs, 0);
                repairFundingCash = 0L;
            }

            double blackoutTime = 0.0;
            double totalTime = 0.0;
            scratch.CollapseDays.Clear();

            var rng = new Unity.Mathematics.Random(req.Seed != 0u ? req.Seed : 1u);

            // Shared per-run timeline state; the per-plant arrays are the orchestrator's reused scratch
            // (no per-run allocation — CIVIC050). The composer resets the scalars + array contents each
            // run and drives the per-domain forecast steps over this one struct (by ref, no copies).
            var state = new ForecastState
            {
                NPlants = nPlants,
                PlantDamage = scratch.PlantDamage,
                RepairDone = scratch.RepairDone,
                RepairQueue = scratch.RepairQueue,
                RepairQueued = scratch.RepairQueued,
                PlantCapMW = plantCaps,   // real per-plant sizes in live mode; null ⇒ equal-plant
            };

            for (int run = 0; run < runs; run++)
            {
                state.LostMW = 0f;
                state.Stress = 0f;
                state.Collapsed = false;
                state.Recov = 0f;
                state.FirstCollapseDay = -1;
                state.ActiveRepairs = 0;

                // Per-plant damage + repair queue, reset for this run (scratch reused, not new'd).
                RepairForecast.Reset(ref state);

                state.SatFactor = PowerForecast.SaturationTarget(cfg, nameplate, demand, intermittentTypes, plantMW);
                float fuelFactor = PowerForecast.FuelFactor(cfg, req.FuelFraction);

                float nextWave = WaveForecast.CalmSeconds((int)nameplate, seasonMod, freqMod, surplusRatio) * ghPerSecond;

                float t = 0f;
                while (t < horizon)
                {
                    RepairForecast.CompleteDue(ref state, plantMW, t, repairEnabled);

                    // wave event
                    if (t >= nextWave)
                    {
                        // Day INDEX from elapsed game-hours: floor is the correct day number, so the
                        // (int)math.floor(...) form is deliberate (not a truncation bug — CIVIC177).
                        int curWarDay = (int)math.floor(t / GameRate.HOURS_PER_DAY);
                        // operational AA decays with war fatigue along the timeline. The recomputed
                        // pool keeps decaying with curWarDay; in live mode the live committed offset
                        // (Used + Casualties) is subtracted as a starting deduction (floored at 0).
                        int pool = ManpowerForecast.Pool(populationPeak, req.Patriotism, req.Morale, curWarDay, req.IsConscription);
                        pool = math.max(0, pool - liveCommitted);

                        // DEFENSE-DRIVEN per-wave loss (Phase 11): leak is the unprotected fraction of the
                        // grid this wave — coverage (op·disc/area), crew (operational fraction =
                        // pool/crewDemand) and ammo/per-shot accuracy all fold into it via the same Core
                        // formula the invariant uses, plus the per-wave leak floor (1 −
                        // MaxWaveInterceptFraction). When coverage → 0 (no crewed AA in range) leak → 1 and
                        // the wave overruns the whole fleet, driving generation to zero, which is the
                        // runtime reality the old drone-count·per-hit mapping understated (MaxThreats=24
                        // capped the swarm while plant count grew linearly). The wave is still gated by war
                        // fatigue (the recomputed pool decays with curWarDay) and amortised across runs, so
                        // a defended grid leaks only the floor each wave and repairs keep pace. perHitLoss +
                        // the per-plant repair queue are preserved. Live mode crew-gates the real mixed
                        // fleet against THIS wave's pool (FleetLeak); the archetype path uses the FREE
                        // Heritage placed count, byte-identical to the pre-live model.
                        float leak;
                        if (fleetLive)
                        {
                            // BOTH branches per wave (decision tool). The damage math uses the ACTIVE
                            // branch (live toggle); both drone layers are accumulated for the readout.
                            float leakA = AirDefenseForecast.FleetLeak(cfg, pool, fleet, area, req.ShotsPerDrone, patriotInterceptsDrones: false);
                            float leakB = AirDefenseForecast.FleetLeak(cfg, pool, fleet, area, req.ShotsPerDrone, patriotInterceptsDrones: true);
                            leak = patriotDrones ? leakB : leakA;
                            droneInterceptSumA += 1f - leakA;
                            droneInterceptSumB += 1f - leakB;
                            droneWaveCount++;

                            // Ballistic line per wave for BOTH branches — accumulated for the campaign
                            // mean. Production this wave is the surviving nameplate (nameplate − lost so
                            // far, floored at 1 — the same prodNow the calm-window scheduling uses below);
                            // the wave number is the elapsed-day index. Branch A keeps the Patriot on the
                            // ballistic line; branch B sends it to drones (only the Gepard backstop left).
                            int ballisticProdMW = (int)math.max(nameplate - state.LostMW, 1f);
                            int ballisticTargets = AirDefenseForecast.BallisticTargetsForWave(cfg, ballisticProdMW, curWarDay);
                            var ballisticA = AirDefenseForecast.BallisticIntercept(cfg, pool, fleet, area, ballisticProdMW, ballisticTargets, patriotInterceptsDrones: false);
                            var ballisticB = AirDefenseForecast.BallisticIntercept(cfg, pool, fleet, area, ballisticProdMW, ballisticTargets, patriotInterceptsDrones: true);
                            if (ballisticA.BallisticTargets > 0)
                            {
                                ballisticInterceptSumA += ballisticA.InterceptFraction;
                                ballisticInterceptSumB += ballisticB.InterceptFraction;
                                ballisticWaveCount++;
                                ballisticTargetSum += ballisticA.BallisticTargets;
                                ballisticSpentOnDronesSum += ballisticB.MissilesSpentOnDrones;
                            }

                            // Per-wave RECOVERY for both branches (decision-tool readout, toggle-free).
                            // Surviving production THIS wave = ballisticProdMW (nameplate − lost so far).
                            // post-strike = prodWave − (drone-leaked share + leaked-ballistic plant damage),
                            // floored at 0 — the same composition the invariant uses, sampled per wave.
                            float prodWave = ballisticProdMW;
                            float ballisticDmgWaveA = LeakedBallisticDamageMW(cfg, ballisticTargets, ballisticA.InterceptFraction, plantMW, prodWave);
                            float ballisticDmgWaveB = LeakedBallisticDamageMW(cfg, ballisticTargets, ballisticB.InterceptFraction, plantMW, prodWave);
                            float afterWaveA = math.max(0f, prodWave - (prodWave * leakA + ballisticDmgWaveA));
                            float afterWaveB = math.max(0f, prodWave - (prodWave * leakB + ballisticDmgWaveB));
                            recoverySumA += prodWave > 0f ? 100.0 * afterWaveA / prodWave : 0.0;
                            recoverySumB += prodWave > 0f ? 100.0 * afterWaveB / prodWave : 0.0;
                            afterSumA += afterWaveA;
                            afterSumB += afterWaveB;
                            recoveryWaveCount++;
                        }
                        else
                        {
                            int opAa = AirDefenseForecast.OperationalAa(cfg, pool, placed);
                            float fCov = AirDefenseForecast.Coverage(cfg, opAa, area);
                            leak = AirDefenseForecast.Leak(cfg, fCov, baseChance, req.ShotsPerDrone);
                            // Archetype fallback: no Patriot, both drone branches identical.
                            droneInterceptSumA += 1f - leak;
                            droneInterceptSumB += 1f - leak;
                            droneWaveCount++;

                            // Archetype recovery: drone leak only (no anti-ballistic fleet ⇒ no ballistic
                            // damage term), both branches identical. Surviving production this wave =
                            // nameplate − lost so far (the same prodNow the scheduling uses below).
                            float prodWave = math.max(nameplate - state.LostMW, 1f);
                            float afterWave = math.max(0f, prodWave - prodWave * leak);
                            double recoveryWave = prodWave > 0f ? 100.0 * afterWave / prodWave : 0.0;
                            recoverySumA += recoveryWave;
                            recoverySumB += recoveryWave;
                            afterSumA += afterWave;
                            afterSumB += afterWave;
                            recoveryWaveCount++;
                        }

                        // Each struck plant eats up to perTarget hits at the nameplate-scaled perHitLoss
                        // (capped at 1.0); a freshly damaged plant is queued for repair. Random plant
                        // selection per wave (rng advanced by ref) so the targeted set moves.
                        int perTarget = math.max(waves.MaxThreatsPerTarget, 1);
                        DamageForecast.ApplyWave(ref state, ref rng, leak, perTarget, perHitLoss, plantMW, repairEnabled);

                        float prodNow = math.max(nameplate - state.LostMW, 1f);
                        nextWave = t + WaveForecast.CalmSeconds((int)prodNow, seasonMod, freqMod, surplusRatio) * ghPerSecond;
                    }

                    // Repair dispatch, bounded by maxRepairs concurrent slots (the cash-gate proxy).
                    RepairForecast.Dispatch(ref state, maxRepairs, repHours, t, repairEnabled);

                    // effective production — step the saturation inertia statefully this tick.
                    float availNameplate = math.max(nameplate - state.LostMW, 0f);
                    state.SatFactor = PowerForecast.StepSaturation(cfg, state.SatFactor, availNameplate, demand, intermittentTypes, plantMW, dt);
                    float productionFull = availNameplate * state.SatFactor * fuelFactor;

                    totalTime += dt;

                    // GridStress / collapse step — shedding + deficit dead-zone + stress accumulation,
                    // returns whether this tick counts as blackout (composer accumulates the time).
                    if (GridStressForecast.Step(
                            ref state, dt, t,
                            productionFull, demand, unsheddable,
                            thr, panic, decay, recovDur,
                            gs.DeficitDeadZoneMinKW, gs.DeficitDeadZoneFraction, req.Shed))
                        blackoutTime += dt;

                    t += dt;
                }

                if (state.FirstCollapseDay >= 0)
                    scratch.CollapseDays.Add(state.FirstCollapseDay);
            }

            result.SampleCount = runs;
            result.BlackoutProbabilityPct = totalTime > 0.0 ? (float)(100.0 * blackoutTime / totalTime) : 0f;
            result.MedianCollapseDay = MedianOrZero(scratch.CollapseDays);
            result.UnsheddableFloorMW = (int)unsheddable;

            // Drone-layer readout — campaign mean of BOTH branches over every wave. A and B differ only
            // when a live Patriot is placed (archetype / Patriot-less fleets give A == B).
            if (droneWaveCount > 0)
            {
                result.DroneInterceptBallisticOnly = (float)(100.0 * droneInterceptSumA / droneWaveCount);
                result.DroneInterceptMixed = (float)(100.0 * droneInterceptSumB / droneWaveCount);
            }

            // Ballistic-line readout — campaign mean of BOTH branches over the ballistic-carrying waves
            // (0 when the city never reaches the production / wave thresholds, or in archetype mode where
            // the fleet path is not taken). MissilesSpentOnDrones is the per-wave mean of branch B's
            // Patriot diversion.
            if (ballisticWaveCount > 0)
            {
                result.BallisticInterceptBallisticOnly = (float)(100.0 * ballisticInterceptSumA / ballisticWaveCount);
                result.BallisticInterceptMixed = (float)(100.0 * ballisticInterceptSumB / ballisticWaveCount);
                // Per-wave means: each term is small (targets ≤ BallisticMaxPerWave, rounds bounded by the
                // fleet magazine), so the long sum / wave-count is well inside int range; the min-guard
                // makes the narrowing explicit for the analyzer (CIVIC136) without changing any value.
                result.BallisticTargets = (int)math.min(ballisticTargetSum / ballisticWaveCount, int.MaxValue);
                result.MissilesSpentOnDrones = (int)math.min(ballisticSpentOnDronesSum / ballisticWaveCount, int.MaxValue);
            }
            result.PatriotInterceptsDrones = fleetLive && patriotDrones;

            // Recovery readout — campaign mean of BOTH branches over every wave (the survivability verdict
            // decoupled from the live toggle, same as the invariant). recovery% is the mean per-wave
            // post-strike production share; recoverable is whether the MEAN post-strike production (afterSum
            // / waveCount) clears the unsheddable floor. A and B diverge only with a live Patriot — Mixed's
            // higher drone layer is offset by its leaked-ballistic damage, so it is no longer always better.
            if (recoveryWaveCount > 0)
            {
                result.WorstCaseRecoveryBallisticOnly = (float)(recoverySumA / recoveryWaveCount);
                result.WorstCaseRecoveryMixed = (float)(recoverySumB / recoveryWaveCount);
                double meanAfterA = afterSumA / recoveryWaveCount;
                double meanAfterB = afterSumB / recoveryWaveCount;
                result.IsRecoverableBallisticOnly = meanAfterA > 0.0 && meanAfterA >= unsheddable;
                result.IsRecoverableMixed = meanAfterB > 0.0 && meanAfterB >= unsheddable;
            }

            // Phase F readout — surface the cash-gate the severity timeline actually ran on so the panel
            // explains a verdict shift (live funding pot → concurrent repair slots) instead of moving
            // silently. RepairFundingCash is 0 in the archetype fallback (no city ⇒ manual MaxConcurrentRepairs).
            result.RepairSlots = maxRepairs;
            result.RepairFundingCash = repairFundingCash;
            result.RepairTier = req.RepairTier;
            result.RepairBudgetLive = repairBudgetLive;
        }

        // ════════════════════════════════════════════════════════════════════
        // Sweep helpers — combine Core formulas (NOT new balance arithmetic)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Megawatts of generation the leaked ballistics knock out this wave. The missiles the anti-
        /// ballistic line did NOT intercept (<paramref name="ballisticTargets"/> · (1 −
        /// <paramref name="interceptFraction"/>)) each strike a station for one per-hit slice, the SAME
        /// nameplate-scaled slice the severity timeline applies (<see cref="RepairForecast.LossPerHit"/>
        /// → fraction of plant nameplate, × <paramref name="plantMW"/> → MW). The total is capped at the
        /// surviving production so a barrage cannot remove more generation than exists.
        /// </summary>
        // FORECAST-APPROX: a leaked ballistic is treated as exactly one per-hit slice on a representative
        // plant — leaked·(LossPerHit·plantMW) — NOT a probabilistic strike on a specific station with its
        // own residual nameplate (the invariant holds no per-plant damage state — the severity timeline
        // does that). It mirrors the drone path's aggregate coarsening (continuous fraction, no per-target
        // spatial model). Pin, do not "fix" piecemeal: a per-plant ballistic strike model belongs in the
        // severity timeline, not this single-snapshot verdict.
        private static float LeakedBallisticDamageMW(
            RemoteBalanceConfig cfg, int ballisticTargets, float interceptFraction, float plantMW, float production)
        {
            int targets = math.max(0, ballisticTargets);
            if (targets <= 0)
                return 0f;
            float leakedBallistic = targets * (1f - math.saturate(interceptFraction));
            // Per-hit slice in MW: LossPerHit returns a fraction of the plant nameplate (fleet = production
            // as the share base, matching the severity timeline's nameplate fleet-share term), × plantMW.
            float perHitLossMW = RepairForecast.LossPerHit(cfg, plantMW, production) * plantMW;
            return math.min(leakedBallistic * perHitLossMW, math.max(production, 0f));
        }

        /// <summary>Median first-collapse day across runs; 0 when no run collapsed.</summary>
        private static int MedianOrZero(System.Collections.Generic.List<int> values)
        {
            if (values.Count == 0)
                return 0;
            values.Sort();
            int mid = values.Count / 2;
            return (values.Count & 1) == 1
                ? values[mid]
                : (values[mid - 1] + values[mid]) / 2;
        }
    }
}
