using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Diagnostics;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using Game;
using Unity.Entities;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Core.Systems.UI
{
    /// <summary>
    /// UI command owner for the in-game crisis sweep. Cross-feature diagnostics tool with no
    /// single domain owner, so it lives in Core UI (like Toast / Settings). Registers the
    /// <c>TriggerCrisisSweep</c> trigger that hands a <see cref="CrisisSweepRequest"/> off to
    /// <c>CrisisSweepSystem</c> on the pause-safe <c>PostSimulation</c> route, and publishes the
    /// <see cref="CrisisSweepResultSingleton"/> as the <c>CrisisSweepState</c> DTO binding read by
    /// the dev panel.
    ///
    /// The trigger does NOT reject while paused — the whole point of the sweep is that the panel
    /// button works in pause (Axiom 14): the <c>EndFrameBarrier</c> hand-off reaches the consumer
    /// in <c>ModificationEnd</c>, and <c>CrisisSweepSystem</c> ticks in <c>PostSimulation</c>, both
    /// pause-safe routes.
    /// </summary>
    [ActIndependent]
    public partial class CrisisSweepUISystem : CivicUIPanelSystem
    {
        // Assumption-param defaults (MODELING ASSUMPTIONS, crisis_model.py) — the payload carries
        // only the mode; the rest are sensible defaults the panel does not yet expose as controls.
        private const float DEFAULT_UNSHEDDABLE_FRAC = 0.05f;
        private const int DEFAULT_SHOTS_PER_DRONE = 3;
        private const float DEFAULT_GAME_DAY_REAL_MINUTES = 4.0f;
        private const int DEFAULT_SEVERITY_DAYS = 180;
        private const int DEFAULT_SEVERITY_RUNS = 30;
        private const uint DEFAULT_SEED = 42u;
        private const float DEFAULT_RESERVE_FRAC = 0.10f;
        private const float DEFAULT_PATRIOTISM = 0.9f;
        private const float DEFAULT_MORALE = 0.9f;
        private const float DEFAULT_FUEL_FRACTION = 1.0f;       // ≥ BufferThreshold ⇒ no fuel penalty
        private const int DEFAULT_MAX_CONCURRENT_REPAIRS = 2;

        private const int MODE_INVARIANT = 0;
        private const int MODE_SEVERITY = 2;

        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_ResultQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_ResultQuery = GetEntityQuery(ComponentType.ReadOnly<CrisisSweepResultSingleton>());
            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(CrisisSweepState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(TriggerCrisisSweep, FeatureIds.UI, RequestResultBridge.CrisisSweep, OnTriggerCrisisSweep);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new CrisisSweepDto();
            if (m_ResultQuery.TryGetSingleton<CrisisSweepResultSingleton>(out var r))
            {
                dto.Mode = r.Mode;
                dto.HasResult = r.HasResult;
                dto.ComputedAtGameHours = r.ComputedAtGameHours;
                dto.ArchetypeId = r.ArchetypeId;
                dto.PopulationPeak = r.PopulationPeak;
                dto.WarDay = r.WarDay;
                dto.WorstCaseRecoveryBallisticOnly = r.WorstCaseRecoveryBallisticOnly;
                dto.WorstCaseRecoveryMixed = r.WorstCaseRecoveryMixed;
                dto.IsRecoverableBallisticOnly = r.IsRecoverableBallisticOnly;
                dto.IsRecoverableMixed = r.IsRecoverableMixed;
                dto.GraceWindowHours = r.GraceWindowHours;
                dto.DroneInterceptBallisticOnly = r.DroneInterceptBallisticOnly;
                dto.DroneInterceptMixed = r.DroneInterceptMixed;
                dto.FreeHeritageGrant = r.FreeHeritageGrant;
                dto.OperationalAaAtVerdict = r.OperationalAaAtVerdict;
                dto.ManpowerTotal = r.ManpowerTotal;
                dto.ManpowerUsed = r.ManpowerUsed;
                dto.ManpowerCasualties = r.ManpowerCasualties;
                dto.ManpowerAvailable = r.ManpowerAvailable;
                dto.AaHeritage = r.AaHeritage;
                dto.AaBofors = r.AaBofors;
                dto.AaGepard = r.AaGepard;
                dto.AaPatriot = r.AaPatriot;
                dto.CoveragePct = r.CoveragePct;
                dto.AreaKm2 = r.AreaKm2;
                dto.BallisticInterceptBallisticOnly = r.BallisticInterceptBallisticOnly;
                dto.BallisticInterceptMixed = r.BallisticInterceptMixed;
                dto.BallisticTargets = r.BallisticTargets;
                dto.MissilesSpentOnDrones = r.MissilesSpentOnDrones;
                dto.PatriotInterceptsDrones = r.PatriotInterceptsDrones;
                dto.CalmHours = r.CalmHours;
                dto.WavePressureAtPeak = r.WavePressureAtPeak;
                dto.SampleCount = r.SampleCount;
                dto.BlackoutProbabilityPct = r.BlackoutProbabilityPct;
                dto.MedianCollapseDay = r.MedianCollapseDay;
                dto.UnsheddableFloorMW = r.UnsheddableFloorMW;
                dto.RepairSlots = r.RepairSlots;
                dto.RepairFundingCash = r.RepairFundingCash;
                dto.RepairTier = r.RepairTier;
                dto.RepairBudgetLive = r.RepairBudgetLive;
            }

            PublishWhenComplete(CrisisSweepState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnTriggerCrisisSweep(int modeId)
        {
            if (modeId < MODE_INVARIANT || modeId > MODE_SEVERITY)
            {
                Log.Warn($"Crisis sweep rejected: invalid mode id={modeId}");
                return TriggerOutcome.Reject(ReasonIds.InternalError);
            }

            // Pause-safe (Axiom 14): EndFrameBarrier flushes in ModificationEnd, the consumer runs
            // in PostSimulation — both tick while paused. No pause reject here, by design.
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new CrisisSweepRequest
            {
                Mode = (CrisisSweepMode)modeId,
                Days = DEFAULT_SEVERITY_DAYS,
                Runs = DEFAULT_SEVERITY_RUNS,
                Seed = DEFAULT_SEED,
                UnsheddableFrac = DEFAULT_UNSHEDDABLE_FRAC,
                ShotsPerDrone = DEFAULT_SHOTS_PER_DRONE,
                GameDayRealMinutes = DEFAULT_GAME_DAY_REAL_MINUTES,
                MaxConcurrentRepairs = DEFAULT_MAX_CONCURRENT_REPAIRS,
                ReserveFrac = DEFAULT_RESERVE_FRAC,
                Patriotism = DEFAULT_PATRIOTISM,
                Morale = DEFAULT_MORALE,
                FuelFraction = DEFAULT_FUEL_FRACTION,
                IsConscription = false,
                Shed = true,
                ArchetypePreset = 1,    // balanced_town default
                RepairTier = 1,         // municipal default
            });
            Log.Info($"Crisis sweep requested: mode={modeId}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }
    }
}
