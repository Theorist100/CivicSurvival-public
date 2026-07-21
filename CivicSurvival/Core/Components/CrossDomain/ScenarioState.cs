using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Managed scenario state.
    /// Persists across save/load (via ScenarioStateMachine serialization).
    ///
    /// Owned by Scenario domain and projected to ScenarioSingleton for ECS readers.
    /// Moved to Core to comply with Axiom 5 (no cross-domain imports).
    /// </summary>
    public struct ScenarioState
    {
        // ===== Scenario Type =====
        public ScenarioType Type;
        public Act CurrentAct;

        // ===== Time Tracking =====
        /// <summary>Days since war started. Negative = pre-war (Village).</summary>
        public int WarDay;
        /// <summary>Absolute game time (cumulative hours) when war began.</summary>
        public float WarStartTime;

        // ===== Population Tracking =====
        /// <summary>Population when scenario started.</summary>
        public int OriginalPopulation;
        /// <summary>Highest population reached.</summary>
        public int PeakPopulation;
        /// <summary>Total refugees received (Village scenario).</summary>
        public int RefugeesReceived;
        /// <summary>Total citizens who left (City scenario).</summary>
        public int CitizensLeft;

        // ===== Modal Tracking =====
        /// <summary>Bitmask of shown modals (prevent duplicates).</summary>
        public ModalFlags ShownModals;

        // ===== Act Progress =====
        /// <summary>0-1 progress through current act.</summary>
        public float ActProgress;
        /// <summary>Number of attack waves successfully defended.</summary>
        public int WavesDefended;
        /// <summary>Count of donor aid packages received.</summary>
        public int DonorAidReceived;
        /// <summary>Forced exodus rate fraction during Crisis; 0 uses normal shock-based calculation.</summary>
        public float ExodusRateOverrideFraction;

        // ===== Additional Statistics (Victory Conditions) =====
        /// <summary>Total missiles intercepted by air defense.</summary>
        public int MissilesIntercepted;
        /// <summary>Total blackout recovery events.</summary>
        public int BlackoutRecoveries;
        /// <summary>Total buildings damaged by threats.</summary>
        public int BuildingsDamaged;

        // ===== Defeat State =====
        /// <summary>Whether the player has been defeated.</summary>
        public bool IsDefeated;
        /// <summary>Reason for defeat.</summary>
        public DefeatCause DefeatCause;
        /// <summary>Whether the current defeat modal was dismissed by the player.</summary>
        public bool DefeatDismissed;

        // ===== Post-Victory =====
        /// <summary>Player's choice after victory (None, OneMoreYear, Endless).</summary>
        public PostVictoryMode PostVictoryMode;

        // ===== Settings (copied from mod settings) =====
        /// <summary>Player chose to skip intro sequence.</summary>
        public bool SkipIntro;

        /// <summary>
        /// Initialize default state for new game.
        /// </summary>
        public static ScenarioState CreateDefault()
        {
            return new ScenarioState
            {
                Type = ScenarioType.None,
                CurrentAct = Act.PreWar,
                WarDay = 0,
                WarStartTime = 0,
                OriginalPopulation = 0,
                PeakPopulation = 0,
                RefugeesReceived = 0,
                CitizensLeft = 0,
                ShownModals = ModalFlags.None,
                ActProgress = 0f,
                WavesDefended = 0,
                DonorAidReceived = 0,
                ExodusRateOverrideFraction = 0f,
                MissilesIntercepted = 0,
                BlackoutRecoveries = 0,
                BuildingsDamaged = 0,
                IsDefeated = false,
                DefeatCause = DefeatCause.None,
                DefeatDismissed = false,
                PostVictoryMode = PostVictoryMode.None,
                SkipIntro = false
            };
        }

        /// <summary>
        /// Check if a specific modal has been shown.
        /// </summary>
        public readonly bool HasShownModal(ModalFlags flag)
        {
            return (ShownModals & flag) != 0;
        }

        /// <summary>
        /// Mark a modal as shown.
        /// </summary>
        public void MarkModalShown(ModalFlags flag)
        {
            ShownModals |= flag;
        }

    }
}


