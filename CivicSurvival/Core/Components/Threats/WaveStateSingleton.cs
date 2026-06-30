using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Wave state singleton — single source of truth for wave execution.
    ///
    /// Access: SystemAPI.GetSingleton&lt;WaveStateSingleton&gt;()
    ///
    /// Single Writer: WaveExecutor (maintains all fields)
    /// Readers: UI panels, audio systems, intel systems (15+ cross-domain consumers)
    ///
    /// ORDERING NOTE: Cross-domain readers operate with up to 1-frame lag on phase transitions.
    /// This is accepted — transitions are infrequent (~1/min), consumers are resilient.
    /// For strict ordering, add RegisterAfter(typeof(WaveExecutor))] or introduce WaveReadyMarker.
    ///
    /// Stats are read from dedicated singletons (Single Writer pattern):
    /// - InterceptStatsSingleton → intercept count
    /// - ThreatOutcomeStatsSingleton → hits/crashed count
    /// - ThreatStatsSingleton → active threat count
    /// </summary>
    public struct WaveStateSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>Current game phase (Calm, Alert, Attack, Recovery).</summary>
        public GamePhase CurrentPhase;

        /// <summary>Current wave number.</summary>
        public int WaveNumber;

        /// <summary>Current wave role (None, Intro, Regular).</summary>
        public WaveRole WaveRole;

        /// <summary>Threats expected in current wave (set during Alert).</summary>
        public int ThreatsExpected;

        /// <summary>Threats spawned this wave.</summary>
        public int ThreatsSpawned;

        // NOTE: Stats fields removed — read directly from dedicated singletons:
        // - ThreatsRemaining → ThreatStatsSingleton.TotalActiveCount
        // - ThreatsIntercepted → InterceptStatsSingleton.InterceptedCount
        // - ThreatsHit/Crashed → ThreatOutcomeStatsSingleton.HitsCount/CrashedCount
        // This eliminates data duplication and copy delay

        /// <summary>Current wave type (Harassment or MassiveStrike).</summary>
        public WaveType CurrentWaveType;

        /// <summary>Whether currently under attack.</summary>
        public bool IsUnderAttack;

        /// <summary>Seconds until next phase change.</summary>
        public float SecondsUntilPhaseChange;

        /// <summary>Time elapsed in current phase (seconds).</summary>
        public float TimeInPhase;

        /// <summary>Total duration of current phase (seconds).</summary>
        public float PhaseEndTime;

        /// <summary>Whether scenario has started (IntroCompleteEvent received).</summary>
        public bool ScenarioStarted;

        /// <summary>
        /// Calm-фаза истекла, но запуск отложен до окна рассвета/заката
        /// (WaveScheduler ждёт dawn/dusk). В этом состоянии обратный отсчёт фазы
        /// не имеет смысла (таймер истёк), UI показывает статус ожидания вместо нуля.
        /// Пишется единственным источником — derive WaveScheduler через
        /// <see cref="CivicSurvival.Core.Events.WaveLaunchWindowWaitEvent"/>.
        /// </summary>
        public bool WaitingForLaunchWindow;

        /// <summary>Default state.</summary>
        public static WaveStateSingleton Default => new()
        {
            CurrentPhase = GamePhase.Calm,
            WaveNumber = 0,
            WaveRole = WaveRole.None,
            ThreatsExpected = 0,
            ThreatsSpawned = 0,
            CurrentWaveType = WaveType.Harassment,
            IsUnderAttack = false,
            SecondsUntilPhaseChange = 0,
            TimeInPhase = 0,
            PhaseEndTime = 0,
            ScenarioStarted = false,
            WaitingForLaunchWindow = false
        };

        public void SetDefaults()
        {
            this = WaveStateSingleton.Default;
        }

        // IEmptySerializable marker: WaveExecutor is the canonical serialization
        // path for wave state; this singleton carries no per-component payload.
    }
}


