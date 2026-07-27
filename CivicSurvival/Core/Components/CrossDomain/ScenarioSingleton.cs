using Unity.Entities;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Read-only scenario state as ECS singleton.
    /// Updated by ScenarioStateMachine each frame.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ScenarioSingleton&gt;()
    ///
    /// Writer: ScenarioStateMachine (sole owner via [SingletonOwner], updates each frame + ExodusRateOverrideFraction via event)
    /// Readers: niche cross-domain consumers (Tutorial, Attention, Refugees, Countermeasures). For CurrentAct see CurrentActSingleton.
    ///
    /// Note: For mutations (RecordBuildingDamaged, RecordWaveDefended), use IScenarioService.
    /// </summary>
    public struct ScenarioSingleton : IComponentData
    {
        /// <summary>Scenario type based on starting population (Village/Town/City).</summary>
        public ScenarioType ScenarioType;

        /// <summary>Absolute game day from GameTimeSystem.</summary>
        public int GameDay;

        /// <summary>War-relative day; -1 before war starts.</summary>
        public int WarDay;

        /// <summary>
        /// Peak population reached during gameplay, with the measure it was reached on. The
        /// mod's single peak — the exodus damper reads this projection instead of keeping its
        /// own copy.
        /// </summary>
        public RecordedPopulation PopulationPeak;

        /// <summary>
        /// Population when the Crisis act began — the shared baseline for remaining-population
        /// thresholds in other domains (Tutorial's exodus warning, the coordinator's act exit).
        /// Consumers must ask <see cref="RecordedPopulation.TryGetOn"/> with the measure they
        /// are dividing by; a baseline that was never taken, or taken with the counter this
        /// build no longer uses, refuses and the consumer withholds its verdict.
        /// </summary>
        public RecordedPopulation CrisisStartPopulation;

        /// <summary>
        /// Whether anyone has ever lived in this city — the "died out" versus "just created"
        /// distinction a zero population cannot make. Projected from the scenario state so
        /// consumers in other domains can gate a decision on it without asking the owner.
        /// </summary>
        public bool CityEverSettled;

        /// <summary>Whether war has started.</summary>
        public bool IsWarStarted;

        /// <summary>Whether the player has been defeated.</summary>
        public bool IsDefeated;

        /// <summary>
        /// Exodus rate override fraction during Crisis act.
        /// 0 = use normal shock-based calculation, > 0 = forced rate.
        /// Written by ScenarioStateMachine via ExodusRateOverrideFractionCommand from CrisisActCoordinator.
        /// </summary>
        public float ExodusRateOverrideFraction;

        /// <summary>Bitmask of shown milestone modals (persisted via ScenarioStateMachine).</summary>
        public ModalFlags ShownModals;

        /// <summary>Count of donor aid packages received (persisted via ScenarioStateMachine).</summary>
        public int DonorAidReceived;

        /// <summary>Check if a modal has already been shown.</summary>
        public readonly bool HasShownModal(ModalFlags flag) => (ShownModals & flag) != 0;

        /// <summary>Default state.</summary>
        public static ScenarioSingleton Default => new()
        {
            ScenarioType = ScenarioType.None,
            GameDay = 0,
            WarDay = -1,
            PopulationPeak = RecordedPopulation.NotRecorded,
            CrisisStartPopulation = RecordedPopulation.NotRecorded,
            CityEverSettled = false,
            IsWarStarted = false,
            IsDefeated = false,
            ExodusRateOverrideFraction = 0f,
            ShownModals = ModalFlags.None,
            DonorAidReceived = 0
        };
    }
}
