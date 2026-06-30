using Game;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
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
    /// The village is "below the enemy's radar" purely because it is small. The war is
    /// driven by city progression — the settlement is noticed once it reaches the war
    /// milestone (<see cref="ScenarioConfig.WarStartMilestone"/>):
    /// - Ominous signs (chirps, gameplay penalties, thunder) escalate across the last
    ///   milestone before war, tracked by XP progress toward <see cref="ScenarioConfig.WarStartMilestone"/>.
    /// - War starts the moment the achieved milestone reaches the war milestone.
    ///
    /// The war milestone sits one step above the refugee milestone
    /// (<see cref="ScenarioConfig.RefugeeStartMilestone"/>): refugees arrive first, the
    /// settlement keeps growing, and only then is it large enough to be targeted. Using
    /// milestones (not a raw population count) keeps that ordering stable regardless of how
    /// fast the player grows, and avoids colliding with the Village/Town population boundary.
    ///
    /// There is no day countdown and no time-based fallback: a settlement that never
    /// progresses is genuinely never targeted. Growth is the whole trigger.
    ///
    /// Only active for the Village scenario (started below <see cref="ScenarioConfig.VillageMaxPop"/>).
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
        private bool m_WarStarted;

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

            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_MilestoneQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.MilestoneLevel>());
            m_XpQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.XP>());

            SubscribeRequired<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);

            Log.Info(" Created");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            base.OnDestroy();
            Log.Info(" Destroyed");
        }

        /// <summary>
        /// Activate the Village pre-war phase. Fires on Village detection (new game) and on the
        /// post-load re-announce from <c>ScenarioStateMachine.ValidateAfterLoad</c> (idempotent —
        /// the active guard preserves loaded state).
        /// </summary>
        private void OnScenarioTypeDetected(ScenarioTypeDetectedEvent evt)
        {
            if (evt.Type == ScenarioType.Village)
            {
                Log.Info($"[OminousSignsSystem] Village detected (pop={evt.Population}) - activating milestone-driven Pre-War");
                ActivatePreWar(evt.Population);
            }
            else
            {
                Log.Info($"[OminousSignsSystem] {evt.Type} detected (pop={evt.Population}) - skipping Pre-War phase");
            }
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

            int warMilestone = BalanceConfig.Current.Scenario.WarStartMilestone;

            // Refresh the horizon anchor once per tick so thunder reflects city expansion.
            m_SoundPositionCached = false;

            if (milestone.m_AchievedMilestone >= warMilestone)
            {
                Log.Info($"[OminousSignsSystem] War milestone reached (achieved={milestone.m_AchievedMilestone} >= {warMilestone}) - war starts");
                TriggerWarStart();
                return;
            }

            CheckOminousSigns(ComputeWarProgress(warMilestone));
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

        private void TriggerWarStart()
        {
            if (m_WarStarted)
                return;

            Log.Info(" === WAR STARTS ===");

            m_WarStarted = true;
            m_Active = false;

            // Fire any signs not yet shown before war starts (fast growth can outrun the late signs).
            // Suppress atmospheric sounds for the batch.
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
#pragma warning disable CIVIC244 // By design: war start is an atomic cascade — all events must fire together in order
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar1.ToKey()), "OminousSignsSystem");
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar2.ToKey()), "OminousSignsSystem");
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.OminousWar3.ToKey()), "OminousSignsSystem");
#pragma warning restore CIVIC242
                EventBus.SafePublish(new PreWarTensionEvent(PreWarEffect.WarStarted, 0f), "OminousSignsSystem");

                // Request war start — ScenarioStateMachine decides when (single source of truth)
                EventBus.SafePublish(new WarStartRequestEvent(), "OminousSignsSystem");
#pragma warning restore CIVIC244
            }

            Log.Info(" Published WarStartRequestEvent");
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
            m_CachedSoundPosition = default;
            m_CachedCityCenter = default;
            m_SoundPositionCached = false;
            m_VfxSystem = null; // force re-resolution after load
            m_MilestoneXpCached = false;
            m_PrewarXpReq = 0;
            m_WarXpReq = 0;
            m_IsCatchingUp = false;
            Log.Info(" State reset");
        }
    }
}
