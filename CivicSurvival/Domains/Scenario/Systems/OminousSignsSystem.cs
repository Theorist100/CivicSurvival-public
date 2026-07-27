using Game;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Effects;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Pre-War atmosphere system for the Village scenario.
    ///
    /// The village is "below the enemy's radar" purely because it is small, so this phase is
    /// atmosphere and nothing else:
    /// - Ominous signs (chirps, gameplay penalties, thunder) escalate across the last
    ///   milestone before war, tracked by XP progress toward <see cref="ScenarioConfig.WarStartMilestone"/>.
    /// - When the settlement outgrows <see cref="ScenarioConfig.VillageMaxPop"/> residents it
    ///   stops being a village, ScenarioStateMachine reclassifies it, and this phase closes.
    ///
    /// This system does not start the war and does not decide when it comes. It used to: war
    /// fired here once the achieved milestone reached the war milestone and the population was
    /// past the village boundary. That path skipped the entire city entry — no cold open, no
    /// first strike, no air-defense quest — for every settlement that grew into a city instead
    /// of starting as one, because the classification was fixed on the first tick and never
    /// revisited. Now the boundary crossing is a change of scenario type, and the war enters
    /// through the cold open, the same way it does for a city that was born big.
    ///
    /// The milestone is not a size proxy and never was. It measures unlocks, not people: the
    /// city option "unlock all milestones" hands out the maximum on the very first tick, and
    /// the achieved milestone is monotonic, so it never falls back when a city empties out.
    /// It drives the signs (one step above <see cref="ScenarioConfig.RefugeeStartMilestone"/>,
    /// so refugees arrive first), nothing else.
    ///
    /// Population comes from <see cref="ICityPopulationReader"/>, the one owner of that measure,
    /// never from a raw citizen count: the raw one adds tourists and commuters to a settlement
    /// whose whole question is how few people live in it.
    ///
    /// There is no day countdown and no time-based fallback: a settlement that never grows is
    /// genuinely never targeted, and is told so once its milestones run out. Growth is the
    /// whole trigger.
    ///
    /// Only active for the Village scenario (below <see cref="ScenarioConfig.VillageMaxPop"/>).
    /// </summary>
    [ActIndependent]
    public partial class OminousSignsSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("OminousSignsSystem");

        // ===== Tuning =====
        private const float HAPPINESS_PENALTY_AMOUNT = 0.05f;
        private const float FALLBACK_SOUND_DISTANCE = 5000f;
        private const float NoThunder = -1f;

        // ===== Ominous Sign Data =====
        private struct OminousSign
        {
            public float ProgressThreshold; // 0..1 XP progress across the milestone before war
            public string ChirperAuthor;
            public string ChirperMessage;
            public OminousEffect Effect;
            // Lerp factor city-center → horizon for the thunder SFX: 1 = farthest, lower = closer,
            // NoThunder (<0) = silent sign.
            public float ThunderLerp;
        }

        private enum OminousEffect
        {
            None,
            DisableLoans,
            CommercePenalty,
            HappinessPenalty,
            ShowWarningBanner
        }

        // 7 ominous signs along the growth toward the radar threshold.
        // OminousSignFlags is byte (8 bits max). Adding a 9th sign requires changing to ushort.
        private static readonly OminousSign[] s_Signs = new[]
        {
            new OminousSign
            {
                ProgressThreshold = 0.10f,
                ChirperAuthor = "@local_farmer",
                ChirperMessage = "Колона військової техніки на трасі. Нарахував 40 вантажівок.",
                Effect = OminousEffect.None,
                ThunderLerp = 1.0f // distant rumble (military convoy)
            },
            new OminousSign
            {
                ProgressThreshold = 0.30f,
                ChirperAuthor = "@gas_station_owner",
                ChirperMessage = "Поставки затримуються. Вводимо ліміти на заправку.",
                Effect = OminousEffect.None,
                ThunderLerp = NoThunder
            },
            new OminousSign
            {
                ProgressThreshold = 0.50f,
                ChirperAuthor = "@IT_company",
                ChirperMessage = "Проблеми з міжнародним трафіком. Працюємо над вирішенням.",
                Effect = OminousEffect.CommercePenalty,
                ThunderLerp = NoThunder
            },
            new OminousSign
            {
                ProgressThreshold = 0.65f,
                ChirperAuthor = "@bank_client",
                ChirperMessage = "Ліміт на зняття готівки $200/день?! Що відбувається?",
                Effect = OminousEffect.DisableLoans,
                ThunderLerp = 0.8f // tension rising (economic stress)
            },
            new OminousSign
            {
                ProgressThreshold = 0.80f,
                ChirperAuthor = "@supermarket_worker",
                ChirperMessage = "Полиці порожні до обіду. Сіль, сірники, крупи — все розібрали.",
                Effect = OminousEffect.None,
                ThunderLerp = NoThunder
            },
            new OminousSign
            {
                ProgressThreshold = 0.90f,
                ChirperAuthor = "@young_mother",
                ChirperMessage = "Не можу додзвонитися до сина в столиці. Мережа перевантажена.",
                Effect = OminousEffect.HappinessPenalty,
                ThunderLerp = 0.5f // closer thunder (uncertainty)
            },
            new OminousSign
            {
                ProgressThreshold = 0.97f,
                ChirperAuthor = "@CityMayor",
                ChirperMessage = "Шановні громадяни, зберігайте спокій. Ми моніторимо ситуацію.",
                Effect = OminousEffect.ShowWarningBanner,
                ThunderLerp = 0.3f // war imminent
            }
        };

        // ===== Persisted state =====
        private bool m_Active;
        private OminousSignFlags m_SignsTriggered;
        // The phase is over: the settlement outgrew village size and the city entry took the
        // war from here. Persisted so a reload does not restart the pre-war atmosphere.
        private bool m_WarStarted;
        // The "nobody is coming for a place this size" line has been said once. Persisted for
        // the same reason a sign flag is: a save/load must not repeat it.
        private bool m_BelowTownBoundaryNoticeShown;

        // ===== Transient runtime state (not serialized) =====
        [System.NonSerialized] private bool m_IsCatchingUp; // suppresses thunder during batch sign replay
        [System.NonSerialized] private float3 m_CachedSoundPosition;
        [System.NonSerialized] private float3 m_CachedCityCenter;
        [System.NonSerialized] private bool m_SoundPositionCached;

        // Milestone XP window (req[war-1] .. req[war]) — static prefab data, cached on first read.
        [System.NonSerialized] private int m_PrewarXpReq;
        [System.NonSerialized] private int m_WarXpReq;
        [System.NonSerialized] private bool m_MilestoneXpCached;

        // Reusable narrative context buffer — NarrativeTriggerEvent copies the dictionary into
        // its own storage, so a single instance can be cleared and refilled per trigger.
        [System.NonSerialized] private readonly System.Collections.Generic.Dictionary<string, string> m_SignContext = new();

        // Dependencies
        private VanillaVfxSystem? m_VfxSystem;
        private ModSettings? m_Settings;
        private ICityPopulationReader? m_PopulationReader;

        // Cached queries
        private EntityQuery m_ScenarioQuery;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_MilestoneQuery;
        private EntityQuery m_XpQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Active = false;
            if (s_Signs.Length > 8)
                Log.Error($"OminousSignFlags is byte (8 bits) but s_Signs has {s_Signs.Length} entries — overflow risk!");
            m_SignsTriggered = OminousSignFlags.None;
            m_WarStarted = false;
            m_BelowTownBoundaryNoticeShown = false;

            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_MilestoneQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.MilestoneLevel>());
            m_XpQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.XP>());

            SubscribeRequired<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);

            Log.Info(" Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Canonical resolve point: the population model registers the reader in its
            // OnCreate, so it is present before any OnStartRunning. TryAnnounceBelowTownBoundary
            // re-resolves with the same ??= because ResetState drops the reference on load.
            m_PopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            base.OnDestroy();
            Log.Info(" Destroyed");
        }

        /// <summary>
        /// Activate the Village pre-war phase, or end it. Fires on Village detection (new game),
        /// on the post-load re-announce from <c>ScenarioStateMachine.ValidateAfterLoad</c>
        /// (idempotent — the active guard preserves loaded state), and on the promotion the
        /// state machine issues when the settlement outgrows village size. That last one is the
        /// end of this phase: the settlement stops being a village, so it stops being ignored.
        /// </summary>
        private void OnScenarioTypeDetected(ScenarioTypeDetectedEvent evt)
        {
            if (evt.Type == ScenarioType.Village)
            {
                Log.Info($"[OminousSignsSystem] Village detected (pop={evt.Population}) - activating milestone-driven Pre-War");
                ActivatePreWar(evt.Population);
                return;
            }

            if (m_Active)
            {
                Log.Info($"[OminousSignsSystem] Settlement grew into {evt.Type} (pop={evt.Population}) - closing Pre-War, the city entry takes the war from here");
                ClosePreWarPhase();
                return;
            }

            Log.Info($"[OminousSignsSystem] {evt.Type} detected (pop={evt.Population}) - skipping Pre-War phase");
        }

        private void ActivatePreWar(int detectedPopulation)
        {
            // Idempotent: a loaded save keeps its persisted progress.
            if (m_Active || m_WarStarted) return;
            if (!CanActivatePreWar())
            {
                Log.Info("[OminousSignsSystem] Pre-War activation ignored — authoritative scenario state is no longer Village PreWar");
                return;
            }

            m_Active = true;
            m_SignsTriggered = OminousSignFlags.None;
            m_WarStarted = false;
            m_BelowTownBoundaryNoticeShown = false;
            m_SoundPositionCached = false;

            Log.Info($"[OminousSignsSystem] Pre-War activated. War at milestone {BalanceConfig.Current.Scenario.WarStartMilestone}, current pop={math.max(0, detectedPopulation)}");

            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousTensions.ToKey()), "OminousSignsSystem");
        }

        private bool CanActivatePreWar()
        {
            if (!m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                || actSingleton.CurrentAct != Act.PreWar)
            {
                return false;
            }

            if (!m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario))
                return true;

            return scenario.ScenarioType == ScenarioType.Village && !scenario.IsWarStarted;
        }

        protected override void OnThrottledUpdate()
        {
            if (!m_Active || m_WarStarted)
                return;

            if (!m_MilestoneQuery.TryGetSingleton<Game.City.MilestoneLevel>(out var milestone))
                return; // milestone singleton not ready (transient during load) — retry next tick

            // One read of the config per tick: a hot-reload between two reads would mix an old
            // milestone with a new population floor and decide on a torn pair.
            var scenarioConfig = BalanceConfig.Current.Scenario;
            int warMilestone = scenarioConfig.WarStartMilestone;

            // Refresh the horizon anchor once per tick so thunder reflects city expansion.
            m_SoundPositionCached = false;

            CheckOminousSigns(ComputeWarProgress(warMilestone));

            // The war itself is not decided here any more — growing out of village size is,
            // and the state machine measures that. What is left is the case where the two
            // diverge: the settlement has unlocked everything the milestones offer and is still
            // too small to be worth a strike, so nothing happens and nothing says why.
            if (milestone.m_AchievedMilestone >= warMilestone)
                TryAnnounceBelowTownBoundary(scenarioConfig.VillageMaxPop);
        }

        /// <summary>
        /// One line to the pre-war narrative channel for the settlement that has run out of
        /// milestones while still holding fewer than <see cref="ScenarioConfig.VillageMaxPop"/>
        /// residents — the boundary the shared tier rule uses to stop calling a settlement a
        /// village, and therefore the one that decides when it starts being attacked. Growth is
        /// the whole trigger, and without a word about it the wait reads as a mod that stopped
        /// working.
        ///
        /// Shown once and persisted, so a save/load does not repeat it. Says nothing at all
        /// when the owner cannot answer (no city entity) — that is an absent measurement, not
        /// a small settlement. A city that exists and holds nobody is a real reading and does
        /// get the notice: the branch only runs once the milestones are exhausted, which an
        /// unpopulated map does not reach.
        /// </summary>
        private void TryAnnounceBelowTownBoundary(int townBoundary)
        {
            if (m_BelowTownBoundaryNoticeShown)
                return;

            // ??= repeated here on purpose: ResetState drops the reference on load, and this
            // path can run before the next OnStartRunning re-resolves it.
            m_PopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
            if (!m_PopulationReader.TryGetResidentCount(out int population))
                return;

            if (population >= townBoundary)
                return;

            m_BelowTownBoundaryNoticeShown = true;
            Log.Info($"[OminousSignsSystem] Milestones exhausted at {population} residents (town boundary {townBoundary}) - settlement still too small to be targeted");
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousTooSmall.ToKey()), "OminousSignsSystem");
        }

        /// <summary>
        /// XP progress (0..1) across the milestone immediately before war
        /// (<c>req[warMilestone-1] → req[warMilestone]</c>). Below that milestone the city is
        /// not yet close enough to be noticed, so progress clamps to 0 and the signs stay silent.
        /// Returns 0 (silent) until the static milestone XP requirements can be read.
        /// </summary>
        private float ComputeWarProgress(int warMilestone)
        {
            if (!TryGetMilestoneXpWindow(warMilestone, out int prewarXp, out int warXp))
                return 0f;

            if (!m_XpQuery.TryGetSingleton<Game.City.XP>(out var xp))
                return 0f;

            int span = math.max(1, warXp - prewarXp);
            return math.saturate((float)(xp.m_XP - prewarXp) / span);
        }

        /// <summary>
        /// Resolve the XP requirements of the war milestone and the one before it from the static
        /// milestone prefab data. Cached after the first successful read (the data never changes).
        /// </summary>
        private bool TryGetMilestoneXpWindow(int warMilestone, out int prewarXp, out int warXp)
        {
            if (m_MilestoneXpCached)
            {
                prewarXp = m_PrewarXpReq;
                warXp = m_WarXpReq;
                return true;
            }

            prewarXp = 0;
            warXp = 0;
            bool foundPrewar = false, foundWar = false;
            foreach (var data in SystemAPI.Query<RefRO<Game.Prefabs.MilestoneData>>())
            {
                int index = data.ValueRO.m_Index;
                if (index == warMilestone - 1)
                {
                    prewarXp = data.ValueRO.m_XpRequried;
                    foundPrewar = true;
                }
                else if (index == warMilestone)
                {
                    warXp = data.ValueRO.m_XpRequried;
                    foundWar = true;
                }
            }

            if (foundPrewar && foundWar && warXp > prewarXp)
            {
                m_PrewarXpReq = prewarXp;
                m_WarXpReq = warXp;
                m_MilestoneXpCached = true;
                return true;
            }
            return false;
        }

        private void CheckOminousSigns(float progress)
        {
            int pending = 0;
            for (int i = 0; i < s_Signs.Length; i++)
            {
                if (progress >= s_Signs[i].ProgressThreshold && !HasTriggeredSign(i))
                    pending++;
            }
            if (pending == 0)
                return;

            // More than one sign coming due at once (post-load / migration catch-up): replay them
            // as a silent batch so thunder does not spam. A single fresh sign keeps its SFX.
            bool prevCatchingUp = m_IsCatchingUp;
            if (pending > 1)
                m_IsCatchingUp = true;
            try
            {
                for (int i = 0; i < s_Signs.Length; i++)
                {
                    if (progress >= s_Signs[i].ProgressThreshold && !HasTriggeredSign(i))
                        TriggerSign(i);
                }
            }
            finally
            {
                m_IsCatchingUp = prevCatchingUp;
            }
        }

        private bool HasTriggeredSign(int signIndex)
        {
            return (m_SignsTriggered & (OminousSignFlags)(1 << signIndex)) != 0;
        }

        private void MarkSignTriggered(int signIndex)
        {
            m_SignsTriggered |= (OminousSignFlags)(1 << signIndex);
        }

        private void TriggerSign(int signIndex)
        {
            var sign = s_Signs[signIndex];

            Log.Info($"[OminousSignsSystem] Triggering ominous sign {signIndex} (progress {sign.ProgressThreshold:P0})");

            MarkSignTriggered(signIndex);

            // Include sign index so post-load batch replay generates unique notification IDs
            // (identical IDs in one frame get dropped by cooldown dedup).
            m_SignContext.Clear();
            m_SignContext["idx"] = signIndex.ToString();
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousSign.ToKey(), m_SignContext), "OminousSignsSystem");

            if (!m_IsCatchingUp)
                PlayAtmosphericSound(sign.ThunderLerp);

            switch (sign.Effect)
            {
                case OminousEffect.DisableLoans:
                    ApplyDisableLoans();
                    break;
                case OminousEffect.CommercePenalty:
                    ApplyCommercePenalty(0.10f); // -10%
                    break;
                case OminousEffect.HappinessPenalty:
                    ApplyHappinessPenalty(HAPPINESS_PENALTY_AMOUNT); // -5%
                    break;
                case OminousEffect.ShowWarningBanner:
                    ShowWarningBanner();
                    break;
                case OminousEffect.None:
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(OminousEffect)}: {sign.Effect}");
                    break;
            }
        }

        /// <summary>Play a distant-thunder SFX. lerpT: 1 = farthest horizon, lower = closer, &lt;0 = silent.</summary>
        private void PlayAtmosphericSound(float lerpT)
        {
            if (lerpT < 0f)
                return;

            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            if (m_Settings != null && m_Settings.IsAudioMuted(AudioCategory.Alert))
                return;

            float3 distantPos = GetDistantSoundPosition();
            m_VfxSystem ??= World.GetExistingSystemManaged<VanillaVfxSystem>();
            m_VfxSystem?.RequestSfx(EffectNames.LIGHTNING_SFX, math.lerp(m_CachedCityCenter, distantPos, math.saturate(lerpT)));
        }

        /// <summary>Get a position on the horizon for distant sound effects (camera-relative, cached per tick).</summary>
        private float3 GetDistantSoundPosition()
        {
            if (m_SoundPositionCached)
                return m_CachedSoundPosition;

            m_SoundPositionCached = true;

            // Event handlers can run while another system owns the active ECS context.
            // Use camera-relative VFX anchoring instead of forcing an entity array sync here.
            var cam = UnityEngine.Camera.main; // cache — Camera.main does FindObjectByTag
            if (cam != null)
            {
                var camPos = cam.transform.position;
                m_CachedSoundPosition = new float3(camPos.x + FALLBACK_SOUND_DISTANCE, camPos.y, camPos.z);
                m_CachedCityCenter = new float3(camPos.x, 0f, camPos.z);
                return m_CachedSoundPosition;
            }

            m_CachedSoundPosition = new float3(FALLBACK_SOUND_DISTANCE, 0f, 0f);
            m_CachedCityCenter = default;
            return m_CachedSoundPosition;
        }

        /// <summary>
        /// End the pre-war phase because the settlement stopped being a village: replay the
        /// signs that never got their turn, lift the pre-war penalties, run the war-onset
        /// headlines. What this deliberately does NOT do is start the war — the cold open owns
        /// that now, for a grown village exactly as for a city that was born big, so the
        /// settlement gets its air raid, its first strike and its air-defense quest instead of
        /// waking up already at war.
        /// </summary>
        private void ClosePreWarPhase()
        {
            if (m_WarStarted)
                return;

            Log.Info(" === PRE-WAR ENDS: the settlement is a target now ===");

            m_WarStarted = true;
            m_Active = false;

            // Fire any signs not yet shown before the phase closes (fast growth can outrun the
            // late signs). Suppress atmospheric sounds for the batch.
            m_IsCatchingUp = true;
            try
            {
                for (int i = 0; i < s_Signs.Length; i++)
                {
                    if (!HasTriggeredSign(i))
                    {
                        Log.Info($"[OminousSignsSystem] Triggering skipped sign {i} before war start");
                        TriggerSign(i);
                    }
                }
            }
            finally
            {
                m_IsCatchingUp = false;
            }

            if (EventBus != null)
            {
                // OminousSigns' own responsibilities: narrative + clear pre-war penalties
#pragma warning disable CIVIC242 // Multi-publisher by design — each system publishes distinct NarrativeTrigger keys
#pragma warning disable CIVIC244 // By design: closing the phase is one cascade — the headlines and the penalty release belong to the same moment
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar1.ToKey()), "OminousSignsSystem");
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar2.ToKey()), "OminousSignsSystem");
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar3.ToKey()), "OminousSignsSystem");
#pragma warning restore CIVIC242

                // Pre-war is over, so the pre-war penalties are: the loan freeze and the
                // commerce cut were the phase, not the war.
                EventBus.SafePublish(new PreWarTensionEvent(PreWarEffect.WarStarted, 0f), "OminousSignsSystem");
#pragma warning restore CIVIC244
            }

            Log.Info(" Pre-war phase closed");
        }

        // ===== Effect Implementations =====

        private void ApplyDisableLoans()
        {
            EventBus?.SafePublish(new PreWarTensionEvent(PreWarEffect.LoansDisabled, 1f), "OminousSignsSystem");
            Log.Info(" Loans disabled (bank restrictions)");
        }

        private void ApplyCommercePenalty(float penalty)
        {
            EventBus?.SafePublish(new PreWarTensionEvent(PreWarEffect.CommercePenalty, penalty), "OminousSignsSystem");
            Log.Info($"[OminousSignsSystem] Commerce penalty: -{penalty * 100}%");
        }

        private void ApplyHappinessPenalty(float penalty)
        {
            EventBus?.SafePublish(new PreWarTensionEvent(PreWarEffect.HappinessPenalty, penalty), "OminousSignsSystem");
            Log.Info($"[OminousSignsSystem] Happiness penalty: -{penalty * 100}%");
        }

        private void ShowWarningBanner()
        {
            Log.Info(" Showing war warning banner");
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousEmergency.ToKey()), "OminousSignsSystem");
        }

        // ===== Public API =====

        /// <summary>Is the Village pre-war phase active?</summary>
        public bool IsActive => m_Active;

        /// <summary>Has war started?</summary>
        public bool HasWarStarted => m_WarStarted;

        /// <summary>
        /// Reset all serializable state to defaults. Called on new game and version-incompatible load.
        /// </summary>
        private void ResetState()
        {
            m_Active = false;
            m_SignsTriggered = OminousSignFlags.None;
            m_WarStarted = false;
            m_BelowTownBoundaryNoticeShown = false;
            m_CachedSoundPosition = default;
            m_CachedCityCenter = default;
            m_SoundPositionCached = false;
            m_VfxSystem = null; // force re-resolution after load
            m_PopulationReader = null; // same — the next world registers its own model system
            m_MilestoneXpCached = false;
            m_PrewarXpReq = 0;
            m_WarXpReq = 0;
            m_IsCatchingUp = false;
            Log.Info(" State reset");
        }
    }
}
