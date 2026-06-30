using CivicSurvival.Core.Config;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer air-defense model: turns the manpower pool plus the placed AA into the
    /// two-layer leak fraction the crisis sweep uses to drive generation loss. Composes the
    /// <see cref="AALogic"/> intercept leaf (per-shot accuracy with rising evasion) and the wave
    /// leak floor — NOT new balance arithmetic. Lifted verbatim out of CrisisSweepSystem so the
    /// AA composition is single-source and verifiable against the runtime fire-control independently.
    ///
    /// Two model fidelities share one set of formulas:
    /// - ARCHETYPE (fallback): a single Heritage-Bofors type — the FREE Heritage grant crewed at the
    ///   Heritage crew cost and covering at the Heritage range. The original <see cref="OperationalAa"/>
    ///   / <see cref="Coverage"/> / <see cref="Leak"/> methods carry it unchanged so a no-live-data
    ///   verdict stays byte-identical to the pre-live model.
    /// - LIVE (mixed fleet): the real placed fleet — Heritage / Bofors / Gepard / Patriot, each with
    ///   its own config crew, range and per-shot intercept. <see cref="FleetLeak"/> generalises the same
    ///   crew-gate + coverage + per-shot loop across the type mix.
    ///
    /// variant D: same <see cref="AALogic.CalculateInterceptChance"/> the runtime intercept path
    /// calls, so a forecast leak can never diverge from the simulated intercept rate.
    /// </summary>
    public static class AirDefenseForecast
    {
        // AA range is config'd in metres; coverage geometry works in km.
        private const float METERS_PER_KM = 1000f;

        /// <summary>
        /// Operational (crewed) AA = clamp(grant placed, by pool / crew-required), floored at 0.
        /// This is the bridge between <see cref="ManpowerForecast.Pool"/> and the leak verdict: the
        /// crew-gate that decides how many of the placed launchers the manpower pool can actually man.
        /// <paramref name="aaCap"/> is the FREE Heritage grant in the invariant verdict and the placed
        /// count along the severity timeline.
        /// </summary>
        public static int OperationalAa(RemoteBalanceConfig cfg, int manpowerPool, int aaCap)
        {
            int crewRequired = math.max(cfg.AAUnits.HeritageCrewRequired, 1);
            return math.max(0, math.min(aaCap, manpowerPool / crewRequired));
        }

        /// <summary>AA coverage fraction = clamp(n·disc / area, 0, 1). Heritage disc from config range.</summary>
        public static float Coverage(RemoteBalanceConfig cfg, int nAa, float areaKm2)
        {
            if (areaKm2 <= 0f)
                return 1f;
            float rangeKm = cfg.AAUnits.HeritageRange / METERS_PER_KM;
            float disc = math.PI * rangeKm * rangeKm;
            return math.clamp(nAa * disc / areaKm2, 0f, 1f);
        }

        /// <summary>Probability a covered, interceptable drone survives the per-shot AA loop —
        /// per-shot AALogic with rising evasion (missedShots = i). Per-shot, not per-drone. Heritage
        /// base chance + Heritage ammo (archetype path).</summary>
        public static float PerDroneSurvive(RemoteBalanceConfig cfg, float baseChance, int shotsPerDrone)
            => PerDroneSurvive(baseChance, cfg.AAUnits.HeritageMaxAmmo, cfg.AAUnits.HeritageMaxAmmo, shotsPerDrone);

        /// <summary>Probability a covered drone survives the per-shot AA loop for a given type's base
        /// intercept chance and ammo (currentAmmo == maxAmmo: a forecast assumes a topped-up
        /// launcher, so the low-ammo penalty does not apply). Per-shot, not per-drone.</summary>
        // FORECAST-APPROX: the per-shot survive loop is deliberately optimistic vs runtime fire-control —
        // (1) topped-up ammo (currentAmmo==maxAmmo, no low-ammo penalty); (2) spotter/detection passed 0f
        // below (the live spotter term could be wired via LiveInputs — see scan 08/10 input-gap — but is
        // currently omitted = optimistic); (3) a FIXED shot count (shotsPerDrone) vs the runtime's unbounded
        // re-engage; (4) runtime false-positive wasted shots and the focus-cluster multiplier are not
        // modelled. Intentional aggregate coarsening — pin, do not "fix" piecemeal.
        private static float PerDroneSurvive(float baseChance, int currentAmmo, int maxAmmo, int shotsPerDrone)
        {
            int shots = math.max(shotsPerDrone, 0);
            float survive = 1f;
            for (int i = 0; i < shots; i++)
            {
                float chance = AALogic.CalculateInterceptChance(
                    baseChance, currentAmmo, maxAmmo,
                    spotterPenalty: 0f, missedShotsCount: i, detectionBonus: 0f);
                survive *= 1f - chance;
            }
            return survive;
        }

        /// <summary>Two-layer leak fraction: (1) the per-wave floor force-leaks ~(1−maxFrac) of
        /// COVERED targets regardless of AA; (2) the rest survive per the per-shot loop. Uncovered
        /// drones always leak. Heritage base chance (archetype path).</summary>
        // FORECAST-APPROX: the floor-leak (1−maxFrac) is a CONTINUOUS aggregate fraction; the runtime applies
        // a per-target BINARY leak via LeakFloorLogic's per-target hash. The continuous floor cannot reproduce
        // "this specific undefended corner always leaks" — deliberate aggregate stand-in (same for FleetLeak
        // below). Unify only with a per-target spatial model, which the forecast deliberately lacks.
        public static float Leak(RemoteBalanceConfig cfg, float fCov, float baseChance, int shotsPerDrone)
        {
            float maxFrac = math.clamp(cfg.Waves.MaxWaveInterceptFraction, 0f, 1f);
            float floorLeak = 1f - maxFrac;
            float survive = PerDroneSurvive(cfg, baseChance, shotsPerDrone);
            float coveredLeak = floorLeak + maxFrac * survive;
            return fCov * coveredLeak + (1f - fCov) * 1f;
        }

        // ════════════════════════════════════════════════════════════════════
        // LIVE mixed-fleet model — Heritage / Bofors / Gepard / Patriot
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Placed (built) fleet composition by AA type — the live counts the crisis sweep
        /// reads from the AirDefense owner. All-zero means no live fleet (archetype fallback).</summary>
        public readonly struct FleetComposition
        {
            public readonly int Heritage;
            public readonly int Bofors;
            public readonly int Gepard;
            public readonly int Patriot;

            public FleetComposition(int heritage, int bofors, int gepard, int patriot)
            {
                Heritage = heritage;
                Bofors = bofors;
                Gepard = gepard;
                Patriot = patriot;
            }

            public int Total => Heritage + Bofors + Gepard + Patriot;
        }

        /// <summary>Per-type model parameters resolved from config: crew cost (manpower per launcher),
        /// disc range (m) and per-shot intercept base chance + ammo for the survive loop.</summary>
        private readonly struct TypeParams
        {
            public readonly int Crew;
            public readonly float RangeMeters;
            public readonly float BaseChance;
            public readonly int MaxAmmo;

            public TypeParams(int crew, float rangeMeters, float baseChance, int maxAmmo)
            {
                Crew = crew;
                RangeMeters = rangeMeters;
                BaseChance = baseChance;
                MaxAmmo = maxAmmo;
            }
        }

        // All four AA types are config-exact: GepardRange / GepardInterceptShahed / GepardMaxAmmo
        // are now first-class balance fields (set on the prefab by CivicPrefabInitSystem.SetupGepard
        // from the same config), so the forecast reads them directly instead of proxying to Bofors.
        // Gepard's crew comes from cfg.Mobilization.GepardCrew.
        private static TypeParams ParamsFor(RemoteBalanceConfig cfg, AAType type)
        {
            var p = AAParams.ForType(cfg, type);
            return new TypeParams(p.CrewRequired, p.Range, p.InterceptChanceShahed, p.MaxAmmo);
        }

        /// <summary>Fractional operational fleet count = Σ(count_t · frac), where frac =
        /// min(1, pool / Σ(count·crew)) is the shared crew-gate. This is the SINGLE operational-count
        /// source: <see cref="FleetLeak"/> defends with exactly these fractional per-type counts (via
        /// <see cref="AccumulateType"/>), and the displayed verdict opAA rounds this same total — so the
        /// reported opAA can never desync from the leak the model actually applied. (The earlier per-type
        /// floor reported 0 while the leak still counted the fractional launchers as protecting.)</summary>
        public static float OperationalFleetCount(RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet)
        {
            float frac = OperationalFraction(cfg, manpowerPool, fleet);
            return math.max(0f, fleet.Total * frac);
        }

        /// <summary>Total live AA the manpower pool can crew across the mixed fleet, for the verdict's
        /// reported opAA. ONE round of the fractional fleet total <see cref="OperationalFleetCount"/>
        /// (NOT a per-type floor), so the displayed opAA == round of what <see cref="FleetLeak"/> defends
        /// with. The live counterpart of <see cref="OperationalAa"/>.</summary>
        public static int OperationalAaFleet(RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet)
            => math.max(0, (int)math.round(OperationalFleetCount(cfg, manpowerPool, fleet)));

        /// <summary>Shared operational fraction = min(1, manpowerPool / Σ(count·crew)), floored at 0.
        /// A zero crew demand (empty fleet) yields 0.</summary>
        // FORECAST-APPROX: applied per-wave against the war-day-shrinking pool, this re-derives crew manning
        // every wave — which the runtime does NOT do (runtime commits crew once and HOLDS it, sticky until a
        // launcher is destroyed; see MobilizationSystem/CrewMath + AACrewReleaseSystem). The real late-war AA
        // decline is LAUNCHER ATTRITION (waves destroy the host building → AA lost), not crew thinning. This
        // per-wave re-gate is a deliberate stand-in for that unmodeled attrition: WRONG mechanism, directionally
        // right (AA declines late-war). Do NOT "fix" it to commit-and-hold alone — that flattens late-war opAA
        // and removes the only decline proxy while attrition is unmodeled (net optimism). Replace ONLY together
        // with launcher attrition + CrewMath.CommitFleet in the "AA late-war fidelity" increment (H2a Pass 2,
        // Phase-H2a-CrewStep.md §9 Q1, queue AA-1).
        private static float OperationalFraction(RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet)
        {
            float crewDemand =
                fleet.Heritage * ParamsFor(cfg, AAType.HeritageBofors).Crew +
                fleet.Bofors * ParamsFor(cfg, AAType.Bofors40mm).Crew +
                fleet.Gepard * ParamsFor(cfg, AAType.Gepard).Crew +
                fleet.Patriot * ParamsFor(cfg, AAType.PatriotSAM).Crew;
            if (crewDemand <= 0f)
                return 0f;
            return math.clamp(math.max(0, manpowerPool) / crewDemand, 0f, 1f);
        }

        /// <summary>Live mixed-fleet two-layer leak fraction. Generalises <see cref="Leak"/> across the
        /// type mix: each type contributes a coverage share (op_count·disc(range)/area) and leaks at its
        /// own per-shot survive within that share; the floor-leak (1−maxFrac) applies to every covered
        /// drone. The uncovered remainder (1 − min(1, Σ coverage)) always leaks. With one Heritage type
        /// this is exactly the single-type <see cref="Leak"/> (covered-leak·fCov + uncovered), and the
        /// covered/uncovered split is CONTINUOUS at Σ coverage = 1 — the covered-leak is normalised by the
        /// CAPPED covered fraction, not the raw Σ coverage, so a dense overlapping fleet does not jump to
        /// an over-optimistic leak as coverage crosses 1.</summary>
        public static float FleetLeak(RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet, float areaKm2, int shotsPerDrone, bool patriotInterceptsDrones)
        {
            if (areaKm2 <= 0f)
                areaKm2 = float.Epsilon; // degenerate area ⇒ full coverage cap below
            float frac = OperationalFraction(cfg, manpowerPool, fleet);
            float maxFrac = math.clamp(cfg.Waves.MaxWaveInterceptFraction, 0f, 1f);
            float floorLeak = 1f - maxFrac;

            float totalCovered = 0f;     // Σ coverage share across types (pre-cap; can exceed 1)
            float weightedCoveredLeak = 0f; // Σ (coverage share · this type's covered-leak)

            AccumulateType(cfg, AAType.HeritageBofors, fleet.Heritage, frac, areaKm2, floorLeak, maxFrac, shotsPerDrone, ref totalCovered, ref weightedCoveredLeak);
            AccumulateType(cfg, AAType.Bofors40mm, fleet.Bofors, frac, areaKm2, floorLeak, maxFrac, shotsPerDrone, ref totalCovered, ref weightedCoveredLeak);
            AccumulateType(cfg, AAType.Gepard, fleet.Gepard, frac, areaKm2, floorLeak, maxFrac, shotsPerDrone, ref totalCovered, ref weightedCoveredLeak);
            // Patriot enters the drone-leak math ONLY when the player opts the global toggle in
            // (PatriotInterceptsDrones — default OFF). At OFF the runtime gates the Patriot out of the
            // drone targeting set at its entry (AirDefenseOrchestrator.CollectAAData) so it never fires
            // a Shahed shot; the forecast mirrors that by skipping its drone coverage/leak share. Its
            // ballistic role (BallisticIntercept below) is unaffected by the toggle.
            if (patriotInterceptsDrones)
                AccumulateType(cfg, AAType.PatriotSAM, fleet.Patriot, frac, areaKm2, floorLeak, maxFrac, shotsPerDrone, ref totalCovered, ref weightedCoveredLeak);

            // Covered fraction = min(1, Σ coverage) (overlapping discs cannot cover more than the whole
            // city). The covered share leaks at the coverage-weighted per-type covered-leak; the
            // uncovered remainder always leaks fully. Normalising weightedCoveredLeak by the SAME
            // totalCovered it was weighted with gives the per-coverage-unit covered-leak, then the result
            // is scaled back by the CAPPED coveredFraction — continuous at totalCovered = 1 (no jump), and
            // for a single Heritage type it reduces exactly to fCov·coveredLeak + (1−fCov) = Leak.
            float coveredFraction = math.min(totalCovered, 1f);
            float coveredLeakPerUnit = totalCovered > float.Epsilon ? weightedCoveredLeak / totalCovered : 0f;
            float uncovered = 1f - coveredFraction;
            return coveredFraction * coveredLeakPerUnit + uncovered * 1f;
        }

        /// <summary>Capped covered fraction of the live mixed fleet — the SAME
        /// <c>min(1, Σ coverShare)</c> that <see cref="FleetLeak"/> computes internally, surfaced for the
        /// verdict's coverage driver. Pure: identical inputs and per-type geometry as
        /// <see cref="FleetLeak"/>, it just stops at the coverage cap instead of folding the per-type
        /// survive into a leak — it does NOT feed back into the leak/recoverability math, so the verdict
        /// values stay byte-identical.</summary>
        public static float FleetCoverage(RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet, float areaKm2, bool patriotInterceptsDrones)
        {
            if (areaKm2 <= 0f)
                return 1f; // degenerate area ⇒ fully covered, matching FleetLeak's float.Epsilon floor
            float frac = OperationalFraction(cfg, manpowerPool, fleet);
            float totalCovered =
                CoverShare(cfg, AAType.HeritageBofors, fleet.Heritage, frac, areaKm2) +
                CoverShare(cfg, AAType.Bofors40mm, fleet.Bofors, frac, areaKm2) +
                CoverShare(cfg, AAType.Gepard, fleet.Gepard, frac, areaKm2) +
                // Patriot contributes drone coverage only with the toggle on — same gate FleetLeak uses,
                // so the reported coverage tracks the leak the model actually applied.
                (patriotInterceptsDrones ? CoverShare(cfg, AAType.PatriotSAM, fleet.Patriot, frac, areaKm2) : 0f);
            return math.min(totalCovered, 1f);
        }

        /// <summary>One AA type's raw coverage share = op_count·disc(range)/area (pre-cap). The geometry
        /// half of <see cref="AccumulateType"/>, factored out so <see cref="FleetCoverage"/> reuses the
        /// exact same per-type disc as the leak path.</summary>
        // FORECAST-APPROX: coverage = πr² disc-area fraction (op_count·disc/area), an aggregate geometric
        // stand-in for the runtime's per-(threat×AA) range test + line-of-sight raycast. No positions, no
        // LOS — deliberate; the forecast has no entity geometry. Archetype Coverage() above uses the same disc.
        private static float CoverShare(RemoteBalanceConfig cfg, AAType type, int placedCount, float operationalFraction, float areaKm2)
        {
            if (placedCount <= 0)
                return 0f;
            float opCount = placedCount * operationalFraction;
            if (opCount <= 0f)
                return 0f;
            var p = ParamsFor(cfg, type);
            float rangeKm = p.RangeMeters / METERS_PER_KM;
            float disc = math.PI * rangeKm * rangeKm;
            return opCount * disc / math.max(areaKm2, float.Epsilon);
        }

        /// <summary>Add one AA type's coverage share and its covered-leak weight. coverShare =
        /// op_count·disc(range)/area; coveredLeak = floorLeak + maxFrac·perShotSurvive(type).</summary>
        private static void AccumulateType(
            RemoteBalanceConfig cfg, AAType type, int placedCount, float operationalFraction, float areaKm2,
            float floorLeak, float maxFrac, int shotsPerDrone,
            ref float totalCovered, ref float weightedCoveredLeak)
        {
            if (placedCount <= 0)
                return;
            float opCount = placedCount * operationalFraction;
            if (opCount <= 0f)
                return;

            var p = ParamsFor(cfg, type);
            float rangeKm = p.RangeMeters / METERS_PER_KM;
            float disc = math.PI * rangeKm * rangeKm;
            // areaKm2 is already floored to ≥ float.Epsilon by the FleetLeak caller; max-guard the
            // division at the site so the analyzer sees the zero protection locally (CIVIC021/100).
            float coverShare = opCount * disc / math.max(areaKm2, float.Epsilon);

            float survive = PerDroneSurvive(p.BaseChance, p.MaxAmmo, p.MaxAmmo, shotsPerDrone);
            float coveredLeak = floorLeak + maxFrac * survive;

            totalCovered += coverShare;
            weightedCoveredLeak += coverShare * coveredLeak;
        }

        // ════════════════════════════════════════════════════════════════════
        // Ballistic intercept — the separate Patriot/Gepard anti-ballistic layer
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Outcome of the ballistic-intercept line for ONE branch: the fraction of the wave's
        /// ballistic missiles the anti-ballistic fleet brings down, plus how many Patriot interceptor
        /// rounds that branch diverts to drones (0 for the ballistic-only branch). The sweep evaluates
        /// this for both Patriot branches each run so the panel can show the trade-off.</summary>
        public readonly struct BallisticVerdict
        {
            /// <summary>Fraction (0..1) of the wave's ballistic missiles intercepted.</summary>
            public readonly float InterceptFraction;
            /// <summary>Ballistic missiles the runtime spawns this wave (the demand the line defends against).</summary>
            public readonly int BallisticTargets;
            /// <summary>Patriot interceptor rounds this branch diverts to drones (0 for the ballistic-only
            /// branch; the whole operational Patriot magazine for the mixed branch).</summary>
            public readonly int MissilesSpentOnDrones;

            public BallisticVerdict(float interceptFraction, int ballisticTargets, int missilesSpentOnDrones)
            {
                InterceptFraction = interceptFraction;
                BallisticTargets = ballisticTargets;
                MissilesSpentOnDrones = missilesSpentOnDrones;
            }
        }

        /// <summary>
        /// Ballistic-intercept fraction for one wave. Mirrors the runtime <c>BallisticDefenseSystem</c>:
        /// EVERY AA type with <c>InterceptChanceBallistic &gt; 0</c> engages ballistics (config-exact —
        /// Patriot 0.40 and Gepard 0.10 today; the gate is the chance, not the type), drawing from the
        /// SAME crew-gated magazine the drone path spends, with NO reservation. So when the player opts
        /// the Patriot into drone duty (<paramref name="patriotInterceptsDrones"/>), its interceptors go
        /// to Shaheds and are unavailable for ballistics — the request's core ask: "if the Patriot trades
        /// its missiles for drones, there is nothing left to down the ballistics".
        ///
        /// Model: BEST-INTERCEPTOR-FIRST, not a magazine-weighted average. The Patriot is the primary
        /// anti-ballistic shield — 4000 m range covers nearly the whole field and 0.40 per-shot — so it
        /// engages ballistics FIRST: it intercepts <c>min(patriotRounds, targets)·patriotCoverage</c> of
        /// them at 0.40. The Gepard is a weak backstop: a 900 m disc covers only a small slice of the
        /// field, so its contribution is scaled by that coverage share (the same πr²/area disc the fleet
        /// coverage uses) and it only mops up the ballistics the Patriot did not engage, at 0.10. The
        /// number of ballistics is the same <see cref="ThreatMath.CalculateBallisticCount"/> the runtime
        /// spawner uses (variant D), so the forecast demand can never drift from the simulated one.
        ///
        /// The split makes the toggle's trade-off STARK (its whole point): with the Patriot on
        /// ballistics the line intercepts ~0.40·coverage of the wave; flip the player toggle and the
        /// Patriot magazine leaves — only the Gepard backstop remains, ~0.10·(its small coverage), a few
        /// percent. An ammo-weighted mean hid this because the Gepard's 1600-round magazine swamped the
        /// Patriot's 16 (the figure barely moved on/off — the bug this replaces).
        /// </summary>
        // FORECAST-APPROX: the ballistic line still coarsens the runtime in three deliberate ways.
        // (1) DRONE DRAW: at toggle ON the Patriot magazine is treated as FULLY claimed by drones
        // (reserved = 0) — an aggregate stand-in for "the player put the Patriot on Shaheds"; the runtime
        // shares one pool continuously (a Patriot with leftover rounds after the drones could still take a
        // ballistic), but the forecast has no per-frame engagement timeline to interleave the two.
        // Gepard's ballistic share is NOT docked: its 1600-round magazine dwarfs any wave, so its drone
        // spend never starves its ballistic rounds (the runtime reality). (2) TOPPED-UP AMMO:
        // opCount·MaxAmmo assumes a full magazine (same optimism as PerDroneSurvive) — depletion across a
        // wave train is not modelled here (the Severity timeline pins that separately). (3) COVERAGE, NOT
        // PER-TARGET LOS: each type's coverage is the aggregate πr²/area disc fraction (now USED to scale
        // the Gepard backstop and cap the Patriot reach), NOT a per-(ballistic×launcher) range test or a
        // line-of-sight raycast, and each engaged ballistic still gets a single interceptor roll (no
        // re-fire on a miss). Aggregate, matching the drone path's coarsening. Pin, do not "fix" piecemeal.
        public static BallisticVerdict BallisticIntercept(
            RemoteBalanceConfig cfg, int manpowerPool, in FleetComposition fleet, float areaKm2, int cityMW, int ballisticTargets, bool patriotInterceptsDrones)
        {
            int targets = math.max(0, ballisticTargets);
            float frac = OperationalFraction(cfg, manpowerPool, fleet);
            if (areaKm2 <= 0f)
                areaKm2 = float.Epsilon; // degenerate area ⇒ full coverage cap below

            // Per-type operational launcher count (crew-gated). Patriot's drone toggle docks its whole
            // ballistic budget; Gepard's is never docked (its magazine dwarfs a wave — see pin (1)).
            float patriotOp = fleet.Patriot * frac;
            float gepardOp = fleet.Gepard * frac;

            // AAParams.ForType (not the local TypeParams) — it carries InterceptChanceBallistic, the
            // gate the runtime BallisticDefenseSystem uses to decide which types engage ballistics.
            var patriot = AAParams.ForType(cfg, AAType.PatriotSAM);
            var gepard = AAParams.ForType(cfg, AAType.Gepard);

            // City-scaled magazine — the runtime stamps ScaleMaxAmmo onto each installation at placement
            // (AAInstallationDetectorSystem), so the forecast MUST scale by the SAME formula or it
            // under-counts anti-ballistic rounds in a large city by up to AmmoMaxScaleCap× (variant D —
            // a forecast capacity can never diverge from the simulated one). cityMW is the branch's
            // production MW (the same input the ballistic COUNT uses, so magazine and target count scale
            // together). Aggregate stand-in: the runtime freezes each unit's magazine at its build-time
            // city size; here approximated by the current production MW — the same coarsening as the
            // topped-up-ammo pin above, not a piecemeal divergence.
            int patriotMaxAmmo = AAAmmoScaling.ScaleMaxAmmo(cfg, patriot.MaxAmmo, cityMW);
            int gepardMaxAmmo = AAAmmoScaling.ScaleMaxAmmo(cfg, gepard.MaxAmmo, cityMW);

            // Anti-ballistic interceptor rounds = opCount · MaxAmmo (1 round per ballistic engagement,
            // runtime ammo--). Patriot zeroed when the toggle claims it / when its chance is 0; Gepard
            // gated only on its chance > 0.
            float patriotRounds = !patriotInterceptsDrones && patriot.InterceptChanceBallistic > 0f
                ? patriotOp * patriotMaxAmmo : 0f;
            float gepardRounds = gepard.InterceptChanceBallistic > 0f ? gepardOp * gepardMaxAmmo : 0f;

            int patriotMagazine = (int)math.round(patriotOp * patriotMaxAmmo);
            int missilesSpentOnDrones = patriotInterceptsDrones ? patriotMagazine : 0;

            if (targets <= 0)
                return new BallisticVerdict(0f, 0, missilesSpentOnDrones);
            if (patriotRounds <= 0f && gepardRounds <= 0f)
                return new BallisticVerdict(0f, targets, missilesSpentOnDrones);

            // Coverage discs (πr²/area, capped at 1) — Patriot's 4000 m disc is effectively full-field;
            // Gepard's 900 m disc is a small slice. CoverShare reuses the exact geometry the fleet
            // coverage uses, so the ballistic reach matches the drone-coverage convention.
            float patriotCoverage = math.min(1f, CoverShare(cfg, AAType.PatriotSAM, fleet.Patriot, frac, areaKm2));
            float gepardCoverage = math.min(1f, CoverShare(cfg, AAType.Gepard, fleet.Gepard, frac, areaKm2));

            // BEST-INTERCEPTOR-FIRST. Patriot engages first: it can engage min(rounds, targets) of them,
            // but only over the share of the field it covers. Those are intercepted at its 0.40.
            float patriotEngaged = patriotRounds > 0f
                ? math.min(patriotRounds, targets) * patriotCoverage
                : 0f;
            float patriotInterceptedCount = patriotEngaged * patriot.InterceptChanceBallistic;

            // Gepard backstops the ballistics the Patriot did NOT engage (out of its rounds or its
            // coverage), capped by Gepard's own rounds and its small coverage, intercepted at 0.10.
            float remaining = math.max(0f, targets - patriotEngaged);
            float gepardEngaged = gepardRounds > 0f
                ? math.min(gepardRounds, remaining) * gepardCoverage
                : 0f;
            float gepardInterceptedCount = gepardEngaged * gepard.InterceptChanceBallistic;

            float interceptFraction = math.saturate((patriotInterceptedCount + gepardInterceptedCount) / targets);

            return new BallisticVerdict(interceptFraction, targets, missilesSpentOnDrones);
        }

        /// <summary>Ballistic missiles the runtime spawns for one wave at this production / wave number —
        /// the SAME <see cref="ThreatMath.CalculateBallisticCount"/> the wave spawner calls (variant D),
        /// so the forecast's ballistic demand is the simulated one. Production is MW.</summary>
        public static int BallisticTargetsForWave(RemoteBalanceConfig cfg, int productionMW, int waveNumber)
            => ThreatMath.CalculateBallisticCount(productionMW, waveNumber, cfg.Waves);
    }
}
