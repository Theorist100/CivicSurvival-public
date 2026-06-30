using Game;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Infrastructure.Audio;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatUI.Audio
{
    /// <summary>
    /// Threat-specific audio orchestration system.
    ///
    /// Responsibilities (WHEN to play sounds):
    /// - Drone buzz: volume/pitch based on threat proximity to camera
    /// - Air raid siren: auto start/stop based on game phase (Alert/Attack)
    /// - Siren force control: for intro cinematics
    ///
    /// Uses generic IEffectPlaybackService for HOW to play sounds.
    ///
    /// Sound design:
    /// - Drone buzz: Continuous 3D spatial audio, louder when close, higher pitch as approach
    /// - Siren: Custom .ogg via Unity AudioSource (2D global, loop)
    ///
    /// Implements IThreatAudioService for cross-domain access via ServiceRegistry.
    ///
    /// PERF: Throttled to 10Hz — audio doesn't need 60fps updates.
    /// </summary>
    [ActIndependent]
    public partial class ThreatAudioOrchestrator : ThrottledSystemBase, IThreatAudioService
    {
        // ===== Audio constants =====
        private const int AUDIO_UPDATE_INTERVAL = 6;
        private const float DRONE_BUZZ_MAX_DISTANCE = 2000f;
        private const float SIREN_VOLUME = 0.7f;
        private const int BALLISTIC_THUNDER_OFFSET = 50;
        // Phase-driven siren plays a short burst at the start of each Alert/Attack episode,
        // then goes quiet — a continuous loop for the whole war is fatiguing. The clip is ~14s,
        // so 2 plays ≈ 28s of warning, matching a real air-raid "alert announced" cue rather
        // than wailing through the whole attack.
        private const int SIREN_BURST_COUNT = 2;

        private static readonly LogContext Log = new("ThreatAudioOrchestrator");

        // Dependencies (self-wired in OnStartRunning)
        private VanillaVfxSystem? m_VfxSystem;
        // AudioPlaybackService inlined — m_AudioManager used directly
        private AudioManager? m_AudioManager;
        private ModSettings? m_Settings;

        // Siren control
        private bool m_SirenActive;
        private bool m_SirenForced;
        private AudioClip? m_SirenClip;
        private AudioSource? m_SirenSource;

        // Phase-driven siren burst: edge-track the current Alert/Attack episode and replay
        // the clip a fixed number of times instead of looping for the entire war.
        // Transient runtime audio state — reset in OnGameLoaded/OnStopRunning, never persisted.
        [System.NonSerialized] private bool m_SirenEpisodeActive;
        [System.NonSerialized] private int m_SirenPlaysRemaining;

        // Audio state tracking
        private int m_ActiveThreatCount;
        private float m_ClosestThreatDistance;
        private float3 m_ClosestThreatPos;

        // Custom audio clips (loaded asynchronously from AudioManager)
        private AudioClip? m_DroneBuzzClip;
        private AudioClip? m_AAFireClip;
        private AudioSource? m_DroneBuzzSource;

        // Cached queries
        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_ThreatStatsQuery;
        private EntityQuery m_CameraProximityQuery;

        // PERF: 10Hz throttle — audio smoothing hides the gap
        protected override int UpdateInterval => AUDIO_UPDATE_INTERVAL; // 60fps / 6 = 10Hz


        protected override void OnCreate()
        {
            base.OnCreate();

            // Cache EntityQueries
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_ThreatStatsQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatStatsSingleton>());
            m_CameraProximityQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatCameraProximitySingleton>());

            // L16 FIX: Register service in OnCreate (CIVIC422 — Register must live in OnCreate).
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IThreatAudioService>(this);

            Log.Info(" Created (ThrottledSystemBase 10Hz)");
        }

        private void InitializeAudio()
        {
            if (m_AudioManager != null) return;
            m_AudioManager = ServiceRegistry.Instance.Require<AudioManager>();

            // Load threat-specific audio clips
            m_AudioManager.LoadClipAsync("Drone sound in the night.ogg", clip =>
            {
                m_DroneBuzzClip = clip;
                if (clip != null)
                {
                    Log.Info($" Loaded drone buzz: {clip.length:F1}s");
                }
                else
                {
                    Log.Warn(" Failed to load 'Drone sound in the night.ogg' - drone buzz audio DISABLED");
                }
            });

            m_AudioManager.LoadClipAsync("aa_fire.ogg", clip =>
            {
                m_AAFireClip = clip;
                if (clip != null)
                {
                    Log.Info($" Loaded AA fire: {clip.length:F1}s");
                }
                else
                {
                    Log.Warn(" Failed to load 'aa_fire.ogg' - AA fire audio DISABLED");
                }
            });

            m_AudioManager.LoadClipAsync("air-siren.ogg", clip =>
            {
                m_SirenClip = clip;
                if (clip != null)
                {
                    Log.Info($" Loaded siren: {clip.length:F1}s");
                    // If ForceStartSiren was called before clip loaded, start now
                    if (m_SirenForced && !m_SirenActive)
                        StartSiren();
                }
                else
                {
                    Log.Warn(" Failed to load 'air-siren.ogg' - siren audio DISABLED");
                }
            });

            // Create 3D spatial audio source for drone buzz
            m_DroneBuzzSource = m_AudioManager.CreateAudioSource(
                name: "DroneBuzz",
                loop: true,
                volume: 0.5f,
                spatial3D: true,
                minDistance: 100f,
                maxDistance: DRONE_BUZZ_MAX_DISTANCE
            );

            // Create 2D global audio source for siren (heard everywhere)
            m_SirenSource = m_AudioManager.CreateAudioSource(
                name: "AirRaidSiren",
                loop: true,
                volume: SIREN_VOLUME,
                spatial3D: false
            );
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            StopLoopingAudioSources();
            // H4 FIX: load resets ephemeral flags after the real loop resources are stopped.
            // Without reset, HandleSirenState sees m_SirenActive=true → skips StartSiren().
            m_SirenActive = false;
            m_SirenForced = false;
            m_SirenEpisodeActive = false;
            m_SirenPlaysRemaining = 0;
            // FIX M14: Same class of bug — drone buzz state not reset. GetAudioState() returns
            // stale values for ~6 frames until first throttled update.
            m_ActiveThreatCount = 0;
            m_ClosestThreatDistance = float.MaxValue;
            m_ClosestThreatPos = default;
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            InitializeAudio();

            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();

            m_VfxSystem = World.GetExistingSystemManaged<VanillaVfxSystem>();

            if (m_VfxSystem == null)
                Log.Warn(" VanillaVfxSystem not found — explosion SFX disabled");

            Log.Info(" Self-wired");
        }

        protected override void OnStopRunning()
        {
            StopLoopingAudioSources();
            m_SirenActive = false;
            m_SirenForced = false;
            m_SirenEpisodeActive = false;
            m_SirenPlaysRemaining = 0;
            m_ActiveThreatCount = 0;
            m_ClosestThreatDistance = float.MaxValue;
            m_ClosestThreatPos = default;
            base.OnStopRunning();
        }

        protected override void OnThrottledUpdate()
        {
            // ECS-Pure: Read phase from WaveStateSingleton
            var currentPhase = (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws)
                    ? ws : WaveStateSingleton.Default)
                .CurrentPhase;

            // Orchestrate siren based on phase
            HandleSirenState(currentPhase);

            // Orchestrate drone buzz based on threat proximity
            UpdateDroneBuzzOrchestration();
        }

        /// <summary>
        /// Control air raid siren based on game phase.
        /// Entering Alert/Attack fires a limited siren burst (SIREN_BURST_COUNT plays), then
        /// goes quiet for the rest of the episode. A fresh Alert/Attack episode re-arms the burst.
        /// Forced (intro) siren has its own continuous-loop lifecycle and is left untouched here.
        /// </summary>
        private void HandleSirenState(GamePhase phase)
        {
            // Alert category muted → keep the siren silent and re-armed for when it is unmuted.
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Alert))
            {
                if (m_SirenActive)
                    StopSiren();
                m_SirenEpisodeActive = false;
                return;
            }

            // Intro/cinematic siren is driven entirely by ForceStartSiren/ForceStopSiren.
            if (m_SirenForced) return;

            bool inAlertEpisode = phase == GamePhase.Alert || phase == GamePhase.Attack;

            if (inAlertEpisode && !m_SirenEpisodeActive)
            {
                // Episode just began → play the burst.
                m_SirenEpisodeActive = true;
                StartSirenBurst();
            }
            else if (!inAlertEpisode && m_SirenEpisodeActive)
            {
                // Episode ended → silence and re-arm for the next Alert/Attack.
                m_SirenEpisodeActive = false;
                StopSiren();
            }
            else if (inAlertEpisode && m_SirenActive)
            {
                // Burst in progress → replay the clip until the count is exhausted.
                AdvanceSirenBurst();
            }
        }

        /// <summary>
        /// Begin a phase-driven siren burst: play the clip once and queue the remaining repeats.
        /// Non-looping so AdvanceSirenBurst can stop after SIREN_BURST_COUNT plays.
        /// </summary>
        private void StartSirenBurst()
        {
            if (m_SirenSource == null || m_SirenClip == null) return;
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Alert)) return;

            m_SirenPlaysRemaining = SIREN_BURST_COUNT - 1;
            m_SirenSource.loop = false;
            m_SirenSource.clip = m_SirenClip;
            m_SirenSource.Play();
            m_SirenActive = true;
            Log.Info($" Siren burst started ({SIREN_BURST_COUNT}x)");
        }

        /// <summary>
        /// Drive the burst at throttle rate: when the current play finishes, start the next one;
        /// once the queue is empty, fall silent for the rest of the episode.
        /// </summary>
        private void AdvanceSirenBurst()
        {
            if (m_SirenSource == null) return;
            if (m_SirenSource.isPlaying) return; // current cycle still sounding

            if (m_SirenPlaysRemaining > 0)
            {
                m_SirenPlaysRemaining--;
                m_SirenSource.Play();
            }
            else
            {
                m_SirenActive = false;
                Log.Info(" Siren burst finished");
            }
        }

        /// <summary>
        /// Start air raid siren in continuous-loop mode (intro/cinematic only).
        /// </summary>
        private void StartSiren()
        {
            if (m_SirenSource == null || m_SirenClip == null) return;
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Alert)) return;

            m_SirenSource.loop = true;
            m_SirenSource.clip = m_SirenClip;
            m_SirenSource.Play();
            m_SirenActive = true;
            Log.Info(" Siren started");
        }

        private void StopSiren()
        {
            if (m_SirenSource != null && m_SirenSource.isPlaying)
            {
                m_SirenSource.Stop();
            }
            m_SirenActive = false;
            m_SirenPlaysRemaining = 0;
            Log.Info(" Siren stopped");
        }

        private void StopLoopingAudioSources()
        {
            bool stopped = false;

            if (m_SirenSource != null && m_SirenSource.isPlaying)
            {
                m_SirenSource.Stop();
                stopped = true;
            }

            if (m_DroneBuzzSource != null && m_DroneBuzzSource.isPlaying)
            {
                m_DroneBuzzSource.Stop();
                stopped = true;
            }

            if (stopped)
            {
                Log.Info(" Stopped looping audio sources");
            }
        }

        /// <summary>
        /// Orchestrate drone buzz audio based on active threats.
        /// Volume increases with threat count and proximity.
        /// Pitch increases as threats approach (Doppler-like effect).
        /// PERF: Reads from ThreatStatsSingleton + ThreatCameraProximitySingleton (no iteration needed).
        /// ThreatMovementSystem computes closest-to-camera during radar update.
        /// </summary>
        private void UpdateDroneBuzzOrchestration()
        {
            // Drone category muted → keep the loop silent; nothing restarts it until unmuted.
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Drone))
            {
                m_ActiveThreatCount = 0;
                m_ClosestThreatDistance = float.MaxValue;
                if (m_DroneBuzzSource != null && m_DroneBuzzSource.isPlaying)
                    m_DroneBuzzSource.Stop();
                return;
            }

            // PERF PROFILING: Find the bottleneck
            using (PerformanceProfiler.Measure("Audio.1_GetSingleton"))
            {
#pragma warning disable CIVIC070 // Audio orchestration — 1-frame lag inaudible
                if (!m_ThreatStatsQuery.TryGetSingleton<ThreatStatsSingleton>(out var stats))
#pragma warning restore CIVIC070
                {
                    m_ActiveThreatCount = 0;
                    m_ClosestThreatDistance = float.MaxValue;
                    return;
                }

                m_ActiveThreatCount = stats.TotalActiveCount;

                // NO_MIGRATE: proximity fallback is custom distance/position state, not a feature-owned default.
                if (m_CameraProximityQuery.TryGetSingleton<ThreatCameraProximitySingleton>(out var proximity))
                {
                    m_ClosestThreatDistance = proximity.ClosestDistance;
                    m_ClosestThreatPos = proximity.ClosestPosition;
                }
                else
                {
                    m_ClosestThreatDistance = float.MaxValue;
                    m_ClosestThreatPos = float3.zero;
                }
            }

            float3 closestThreatPos = m_ClosestThreatPos;

            // No threats = stop drone buzz (fast path)
            if (m_ActiveThreatCount == 0 || m_DroneBuzzSource == null || m_DroneBuzzClip == null)
            {
                m_ClosestThreatDistance = float.MaxValue;

                if (m_DroneBuzzSource != null && m_DroneBuzzSource.isPlaying)
                {
                    m_DroneBuzzSource.Stop();
                }
                return;
            }

            if (m_ClosestThreatDistance >= float.MaxValue * 0.5f ||
                float.IsNaN(m_ClosestThreatDistance) ||
                float.IsInfinity(m_ClosestThreatDistance))
            {
                m_ClosestThreatDistance = float.MaxValue;
                if (m_DroneBuzzSource.isPlaying)
                    m_DroneBuzzSource.Stop();
                return;
            }

            float volume, pitch;
            using (PerformanceProfiler.Measure("Audio.2_Calculate"))
            {
                float closestDist = m_ClosestThreatDistance;
                volume = CalculateVolume(m_ActiveThreatCount, closestDist);
                pitch = CalculatePitch(closestDist);
            }

            using (PerformanceProfiler.Measure("Audio.3_SetPosition"))
            {
                Vector3 closestPos = new Vector3(closestThreatPos.x, closestThreatPos.y, closestThreatPos.z);
                if (m_DroneBuzzSource.gameObject != null)
                {
                    m_DroneBuzzSource.transform.position = closestPos;
                }
            }

            using (PerformanceProfiler.Measure("Audio.4_SetParams"))
            {
                m_DroneBuzzSource.volume = math.clamp(volume, 0f, 1f);
                m_DroneBuzzSource.pitch = math.clamp(pitch, 0.5f, 2f);
            }

            using (PerformanceProfiler.Measure("Audio.5_PlayControl"))
            {
                if (volume > 0.01f)
                {
                    if (!m_DroneBuzzSource.isPlaying)
                    {
                        m_DroneBuzzSource.clip = m_DroneBuzzClip;
                        m_DroneBuzzSource.Play();
                    }
                }
                else
                {
                    if (m_DroneBuzzSource.isPlaying)
                    {
                        m_DroneBuzzSource.Stop();
                    }
                }
            }
        }

        /// <summary>
        /// Calculate drone buzz volume based on threat count and proximity.
        /// More threats = louder, closer threats = louder.
        /// </summary>
        private float CalculateVolume(int threatCount, float distance)
        {
            // Base volume from threat count
            float countVolume = math.min(1f, threatCount * Engine.Audio.VOLUME_PER_THREAT);

            // Distance modifier (closer = louder)
            float distanceVolume = 1f;
            if (distance > Engine.Audio.VOLUME_FULL_DISTANCE)
            {
                distanceVolume = math.max(
                    Engine.Audio.VOLUME_MIN,
                    1f - ((distance - Engine.Audio.VOLUME_FULL_DISTANCE) / Engine.Audio.VOLUME_FADE_DISTANCE)
                );
            }

            return countVolume * distanceVolume;
        }

        /// <summary>
        /// Calculate drone buzz pitch based on proximity.
        /// Closer threats = higher pitch (Doppler-like effect).
        /// </summary>
        private float CalculatePitch(float distance)
        {
            if (distance > Engine.Audio.PITCH_FAR_DISTANCE) return Engine.Audio.PITCH_BASE;
            if (distance < Engine.Audio.PITCH_CLOSE_DISTANCE) return Engine.Audio.PITCH_MAX;

            float pitchRange = Engine.Audio.PITCH_MAX - Engine.Audio.PITCH_BASE;
            float distanceRange = Engine.Audio.PITCH_FAR_DISTANCE - Engine.Audio.PITCH_CLOSE_DISTANCE;

            // Guard against division by zero
            if (distanceRange <= 0f) return Engine.Audio.PITCH_BASE;

            return Engine.Audio.PITCH_BASE + (pitchRange * (1f - (distance - Engine.Audio.PITCH_CLOSE_DISTANCE) / distanceRange));
        }

        /// <summary>
        /// Force start siren (for intro/cinematic sequences).
        /// Overrides automatic phase-based control.
        /// </summary>
        public void ForceStartSiren()
        {
            m_SirenForced = true;
            if (!m_SirenActive)
            {
                StartSiren();
            }
        }

        /// <summary>
        /// Force stop siren (for skip intro).
        /// </summary>
        public void ForceStopSiren()
        {
            m_SirenForced = false;
            if (m_SirenActive)
            {
                StopSiren();
            }
        }

        /// <summary>
        /// Get audio state for UI visualization.
        /// </summary>
        public (int threatCount, float closestDistance, bool sirenActive) GetAudioState()
        {
            return (m_ActiveThreatCount, m_ClosestThreatDistance, m_SirenActive);
        }

        /// <summary>
        /// Immediately silence any looping source whose category is now muted.
        /// Called synchronously on the UI thread by the settings handler so a mute
        /// toggle stops playing audio at once — including while the game is paused,
        /// where the throttled GameSimulation gate would not run (Axiom 14).
        /// One-shot SFX (intercept/explosion) need no stop — they fire-and-forget,
        /// and the play gate prevents new ones.
        /// </summary>
        public void ApplyAudioMuteState()
        {
            // UI-toggle path (not boot): if ModSettings is not registered yet there is nothing
            // to apply, so resolve via TryGet and no-op instead of throwing. Unlike OnStartRunning
            // (boot, where Require asserts the dependency), this can be hit from a UI trigger.
            m_Settings ??= ServiceRegistry.TryGet<ModSettings>();
            if (m_Settings == null) return;

            if (m_Settings.IsAudioMuted(AudioCategory.Drone)
                && m_DroneBuzzSource != null && m_DroneBuzzSource.isPlaying)
            {
                m_DroneBuzzSource.Stop();
            }

            bool alertMuted = m_Settings.IsAudioMuted(AudioCategory.Alert);

            if (alertMuted && m_SirenActive)
            {
                StopSiren();
                m_SirenEpisodeActive = false;
            }
            else if (!alertMuted && m_SirenForced && !m_SirenActive)
            {
                // Symmetric restore: a forced (intro) siren that StartSiren silently gated while
                // Alert was muted has m_SirenForced=true / m_SirenActive=false. HandleSirenState
                // bails on m_SirenForced, so this sync UI-thread path is the only restart route
                // (pause-safe, Axiom 14). StartSiren re-checks the mute gate, so this is a no-op
                // if Alert is still muted.
                StartSiren();
            }
        }

        /// <summary>
        /// Play intercept sound effect (AA fire + explosion).
        /// Called by ThreatMovementSystem on successful intercept.
        /// Combines custom AA fire clip with native CS2 explosion SFX.
        /// </summary>
        public void PlayInterceptSound(float3 position)
        {
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Combat)) return;

            if (Log.IsDebugEnabled) Log.Debug($" Intercept sound at {position}");

            // AA fire - custom AudioClip (no CS2 equivalent)
            if (m_AudioManager != null && m_AAFireClip != null)
            {
                m_AudioManager.PlayOneShot3D(m_AAFireClip, new UnityEngine.Vector3(position.x, position.y, position.z), Engine.Audio.AA_FIRE_VOLUME);
            }

            // Explosion SFX - via vanilla EnabledEffectData pipeline
            PlayExplosionSFX(position);
        }

        /// <summary>
        /// Play impact sound effect (building collapse + explosion).
        /// Called by ThreatDamageSystem on threat impact.
        /// Uses native CS2 BuildingCollapseSFX + LightningSFX combo.
        /// </summary>
        // PERF: PlaySoundEffect blocks main thread 10-30ms per call.
        // Throttle impact sounds to max 1 per second — serial hits are indistinguishable.
        private const float IMPACT_SOUND_COOLDOWN = 1.0f;
        private float m_LastImpactSoundTime = float.NegativeInfinity;

        public void PlayImpactSound(float3 position, bool isBallistic)
        {
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Combat)) return;

            float now = UnityEngine.Time.time;
            if (now - m_LastImpactSoundTime < IMPACT_SOUND_COOLDOWN) return;
            m_LastImpactSoundTime = now;

            if (Log.IsDebugEnabled)
            {
                string soundType = isBallistic ? "heavy explosion" : "impact";
                Log.Debug($" {soundType} sound at {position}");
            }

            // Explosion combo via vanilla EnabledEffectData pipeline
            PlayExplosionSFX(position);

            // Ballistic gets extra delayed thunder for "heavy" feel
            if (isBallistic)
            {
                float3 delayedPos = position + new float3(BALLISTIC_THUNDER_OFFSET, 0, BALLISTIC_THUNDER_OFFSET);
                m_VfxSystem?.RequestSfx(EffectNames.LIGHTNING_SFX, delayedPos);
            }
        }

        /// <summary>
        /// Queue native CS2 explosion combo (BuildingCollapseSFX + LightningSFX)
        /// via EnabledEffectData pipeline. Zero structural changes, worker-thread culling.
        /// </summary>
        private void PlayExplosionSFX(float3 position)
        {
#pragma warning disable CIVIC256 // Null = logged in OnStartRunning, silent skip is intentional
            if (m_VfxSystem == null) return;
#pragma warning restore CIVIC256
            m_VfxSystem.RequestSfx(EffectNames.COLLAPSE_SFX, position);
            m_VfxSystem.RequestSfx(EffectNames.LIGHTNING_SFX, position);
        }

        protected override void OnDestroy()
        {
            // Clean up siren
            if (m_SirenActive)
            {
                StopSiren();
            }
            if (m_SirenSource != null)
            {
                m_AudioManager?.DestroyAudioSource(m_SirenSource);
                m_SirenSource = null;
            }

            // Clean up drone buzz audio source
            if (m_DroneBuzzSource != null)
            {
                if (m_DroneBuzzSource.isPlaying)
                {
                    m_DroneBuzzSource.Stop();
                }
                m_AudioManager?.DestroyAudioSource(m_DroneBuzzSource);
                m_DroneBuzzSource = null;
            }

            // Unregister from ServiceRegistry
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IThreatAudioService>(this);
            }

            // Clear service references
            m_VfxSystem = null;
            m_AudioManager = null;

            Log.Info(" Destroyed");
            base.OnDestroy();
        }
    }
}

