using Unity.Entities;
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

        /// <summary>Peak population reached during gameplay.</summary>
        public int PopulationPeak;

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
            PopulationPeak = 0,
            IsWarStarted = false,
            IsDefeated = false,
            ExodusRateOverrideFraction = 0f,
            ShownModals = ModalFlags.None,
            DonorAidReceived = 0
        };
    }
}
