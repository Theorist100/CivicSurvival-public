using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.AirDefense;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Domains.Narrative.Quests
{
    /// <summary>
    /// Narrative quests — small, event-driven objectives handed out by story characters.
    ///
    /// Architecture: one generic <see cref="QuestSlot"/> per registered quest carries ALL
    /// shared lifecycle state (phase, progress, anchor, deadline, cooldown, chip-clear).
    /// The shared lifecycle (arm / complete / fail / deadline tick / chip clear / feed
    /// posts) is generic over the slot + a static <see cref="QuestMeta"/> row. Adding a
    /// quest = a <see cref="QuestId"/> value, a meta row, its trigger/completion event
    /// logic, and texts — no new state shape, codec fields, DTO fields or UI branches.
    ///
    /// Current roster:
    /// - <see cref="QuestId.KotletaLights"/> — armed by chance on ANY district blackout
    ///   start (anchor = the district that went dark; a fixed-place trigger would be
    ///   unreachable in most cities). Restore power there before the deadline →
    ///   buckwheat reserve. Repeatable behind a cooldown.
    /// - <see cref="QuestId.RightVoice"/> — offered once per city on the first
    ///   multi-contact psy raid (anchor = the arming wave, which does not count).
    ///   Blunt N landings with the matching speaker → media trust.
    ///
    /// Rewards cross into Cognitive via Core command events (GrantBuckwheatCommand /
    /// AdjustMediaTrustCommand — the CorruptionGainEvent pattern), so Axiom 5 holds.
    /// Triggers/completions are event-driven; the only frame work is the slot lifecycle
    /// tick in OnUpdateImpl (deadline expiry + chip clearing at frame precision).
    /// Deadlines are absolute TotalGameHours timestamps (persist across save/load).
    /// </summary>
    [ActIndependent]
#if DEBUG
    public partial class QuestSystem : CivicSystemBase, IQuestDebugMutator
#else
    public partial class QuestSystem : CivicSystemBase
#endif
    {
        private static readonly LogContext Log = new("QuestSystem");

        // How long a terminal phase (Completed/Failed) stays on the chip before clearing.
        private const float RESULT_DISPLAY_HOURS = 4f;

        // Fallbacks mirror the balance contract defaults (Narrative.Quest*).
        private const float FALLBACK_KOTLETA_CHANCE = 0.35f;
        private const int FALLBACK_KOTLETA_COOLDOWN_DAYS = 3;
        private const float FALLBACK_KOTLETA_DEADLINE_HOURS = 6f;
        private const float FALLBACK_KOTLETA_REWARD_TONS = 3f;
        private const int FALLBACK_VOICE_TARGET = 3;
        private const float FALLBACK_VOICE_REWARD_TRUST = 0.15f;

        /// <summary>
        /// Static per-quest metadata: feed voice + satire keys + lifecycle traits shared
        /// by the generic slot machinery. One row per <see cref="QuestId"/>.
        /// </summary>
        private readonly struct QuestMeta
        {
            public QuestMeta(QuestId id, string handle, string titleKey, string offerKey, string satireStart, string satireDone, string? satireFail, bool oncePerCity)
            {
                Id = id;
                Handle = handle;
                TitleKey = titleKey;
                OfferKey = offerKey;
                SatireStart = satireStart;
                SatireDone = satireDone;
                SatireFail = satireFail;
                OncePerCity = oncePerCity;
            }

            public QuestId Id { get; }
            public string Handle { get; }
            /// <summary>Localization key of the quest's display title (the QuestBadge modal
            /// uses the same key on the TS side) — the call toast's header.</summary>
            public string TitleKey { get; }
            /// <summary>Localization key of the character's call pitch — the offer toast's
            /// body (the full description lives in the chip's modal after accepting).</summary>
            public string OfferKey { get; }
            public string SatireStart { get; }
            public string SatireDone { get; }
            /// <summary>Null = the quest cannot fail (no deadline).</summary>
            public string? SatireFail { get; }
            /// <summary>True: after resolving, the Offered latch keeps it from re-arming.</summary>
            public bool OncePerCity { get; }
        }

        // Feed voices. Kotleta speaks for his own quest; the Voice quest is the free press
        // reacting to the psy war (its reward is media trust — the same ledger).
        private static readonly QuestMeta[] s_Roster =
        {
            new(QuestId.KotletaLights, "@DeputatKotleta", "UI_QUEST_KOTLETA_TITLE", "UI_QUEST_KOTLETA_OFFER",
                "SATIRE_QUEST_KOTLETA_START", "SATIRE_QUEST_KOTLETA_DONE", "SATIRE_QUEST_KOTLETA_FAIL",
                oncePerCity: false),
            new(QuestId.RightVoice, "@FreePress_Live", "UI_QUEST_VOICE_TITLE", "UI_QUEST_VOICE_OFFER",
                "SATIRE_QUEST_VOICE_START", "SATIRE_QUEST_VOICE_DONE", satireFail: null,
                oncePerCity: true),
            // The volunteer network calls during the cold open. No offer toast: the call IS the
            // intro's ActivistCall modal, so this one arms directly (see OnActivistWindowOpened).
            new(QuestId.AirDefenseReady, "@Volunteer_Oksana", "UI_QUEST_AIRDEFENSE_TITLE", "UI_QUEST_AIRDEFENSE_OFFER",
                "SATIRE_QUEST_AIRDEFENSE_START", "SATIRE_QUEST_AIRDEFENSE_DONE", "SATIRE_QUEST_AIRDEFENSE_FAIL",
                oncePerCity: true),
        };

        // The volunteers' line when they call and find the guns already up. Separate from the
        // roster's SatireDone, which celebrates finishing the task — here there was no task.
        private const string SATIRE_AIRDEFENSE_ALREADY_STANDING = "SATIRE_QUEST_AIRDEFENSE_ALREADY";

        // Runtime slots, index-aligned with s_Roster. Serialized as a slot list.
        private readonly QuestSlot[] m_Slots = CreateBootSlots();

        // Pending quest CALLS (offer toast is up, player hasn't answered), index-aligned with
        // s_Roster. Deliberately TRANSIENT: toasts don't survive a save/load, so a call caught
        // by a save simply reads as "missed" after loading (cooldown not even started — the
        // trigger will re-offer). Toast ids are process-scoped; CS2 reuses system instances
        // across loads, so both arrays are cleared in ResetState (see
        // nonserialized-persists-across-load precedent).
        [System.NonSerialized] private readonly int[] m_PendingOfferToastIds = new int[s_Roster.Length];
        [System.NonSerialized] private readonly int[] m_PendingOfferAnchors = new int[s_Roster.Length];
        // Toast-event subscription target; re-resolved when the service instance changes
        // (the MaintenanceContractSystem pattern).
        [System.NonSerialized] private Core.UI.Toast.ToastService? m_ToastService;

        // Cached act query: handlers run outside OnUpdate (foreign context — SystemAPI
        // would resolve against CheckedStateRef and throw), the HeroDeploymentSystem pattern.
        private EntityQuery m_CurrentActQuery;

        // Manpower as it stood when the activists' window opened — the basis for the
        // air-defense target, fixed when the player picks up the call. Transient like the
        // pending-offer arrays: the call does not survive a save, so neither must this.
        [System.NonSerialized] private int m_ActivistWindowManpower;

        // Debug-only manpower read, through the Mobilization read-contract (never its
        // singleton: CIVIC211). The live path never uses this — the intro snapshots manpower
        // when the activists' window opens and ships it on the event, so the target cannot
        // drift while the player builds. Only the debug panel, which arms the quest with no
        // window around it, has to ask for the number itself. Resolved in OnStartRunning;
        // Require = fail-loud (CIVIC463). DEBUG-only for the same reason as its single
        // reader, DebugAvailableManpower: in a Release build nothing consults it.
#if DEBUG
        private IMobilizationManpowerReader m_ManpowerReader = null!;
#endif

        // Live air-defense fleet, so the quest can score the sky as it already is rather than
        // only what gets built after the activists call. Null-object when AirDefense is closed
        // (a zero fleet), which reads as "nothing standing" — the honest fail-closed direction.
        private IAirDefenseStatsReader m_FleetReader = null!;

        /// <summary>
        /// War started = act past PreWar. Missing singleton (early boot) reads as
        /// "not started" — the fail-safe direction: never arm a quest blind.
        /// </summary>
        private bool IsWarStarted()
            => m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var act)
               && act.CurrentAct != Act.PreWar;

        private static QuestSlot[] CreateBootSlots()
        {
            var slots = new QuestSlot[s_Roster.Length];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new QuestSlot { Id = s_Roster[i].Id, Anchor = QuestSlot.NO_ANCHOR };
            return slots;
        }

        // ── Read API for QuestUISystem ──
        public int SlotCount => m_Slots.Length;

        /// <summary>Slot snapshot by index (roster order).</summary>
        public QuestSlot GetSlot(int index) => m_Slots[index];

        /// <summary>Hours left before the slot's deadline (0 when inactive or deadline-free).</summary>
        public double GetHoursLeft(int index)
        {
            ref readonly var slot = ref m_Slots[index];
            if (slot.Phase != QuestPhase.Active || slot.DeadlineHour <= 0.0) return 0.0;
            if (!GameTimeSystem.TryGetGameHours(out var now)) return 0.0;
            double left = slot.DeadlineHour - now;
            return left > 0.0 ? left : 0.0;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            SubscribeRequired<BlackoutStartedEvent>(OnBlackoutStarted);
            SubscribeRequired<BlackoutEndedEvent>(OnBlackoutEnded);
            SubscribeRequired<PsyWaveResultEvent>(OnPsyWaveResult);
            SubscribeRequired<ActivistWindowOpenedEvent>(OnActivistWindowOpened);
            SubscribeRequired<AirDefensePlacedEvent>(OnAirDefensePlaced);
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IQuestDebugMutator>(this);
#endif

            Log.Info($"Created (event-driven narrative quests, roster={s_Roster.Length})");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Registration-site resolve: MobilizationSystem registers the reader in its
            // OnCreate, which precedes any system's first update (SickLeaveWaveSystem pattern).
#if DEBUG
            m_ManpowerReader = ServiceRegistry.Instance.Require<IMobilizationManpowerReader>();
#endif
            // Null-object rather than Require: AirDefense may be closed, and a zero fleet is a
            // valid answer there (nothing is standing), not a missing dependency.
            m_FleetReader ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullAirDefenseStatsReader.Instance);
        }

        protected override void OnDestroy()
        {
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IQuestDebugMutator>(this);
#endif
            UnsubscribeSafe<BlackoutStartedEvent>(OnBlackoutStarted);
            UnsubscribeSafe<BlackoutEndedEvent>(OnBlackoutEnded);
            UnsubscribeSafe<PsyWaveResultEvent>(OnPsyWaveResult);
            UnsubscribeSafe<ActivistWindowOpenedEvent>(OnActivistWindowOpened);
            UnsubscribeSafe<AirDefensePlacedEvent>(OnAirDefensePlaced);
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);

            if (m_ToastService != null)
            {
                m_ToastService.OnToastInteracted -= OnQuestToastInteracted;
                m_ToastService.OnToastExpired -= OnQuestToastExpired;
                m_ToastService = null;
            }

            base.OnDestroy();
        }

        // The only per-frame work is the slot lifecycle tick: deadline expiry and
        // terminal-phase chip clearing. An hour-quantized check (the old HourChangedEvent
        // route) left up to an hour of game time where an expired quest still showed
        // "0h left" in the ACTIVE color — the S4 audit's stale-chip window. A frame tick
        // over the tiny fixed roster (a couple of phase/double compares) closes it at
        // frame precision, and pausing is inherently safe: game hours stand still.
        // Everything else stays event-driven.
        protected override void OnUpdateImpl()
        {
            if (!GameTimeSystem.TryGetGameHours(out var now))
                return;

            for (int i = 0; i < m_Slots.Length; i++)
            {
                ref var slot = ref m_Slots[i];

                if (slot.Phase == QuestPhase.Active && slot.DeadlineHour > 0.0 && now > slot.DeadlineHour)
                    Fail(i, now);

                if ((slot.Phase == QuestPhase.Completed || slot.Phase == QuestPhase.Failed) && now >= slot.ClearAtHour)
                {
                    slot.Phase = QuestPhase.Inactive;
                    slot.Anchor = QuestSlot.NO_ANCHOR;
                    slot.Progress = 0;
                }
            }
        }

        // ── Generic slot lifecycle ───────────────────────────────────────────

        private int IndexOf(QuestId id)
        {
            for (int i = 0; i < s_Roster.Length; i++)
                if (s_Roster[i].Id == id) return i;
            return -1;
        }

        /// <summary>Can this slot arm now? (Inactive + cooldown passed + once-per-city latch clear.)</summary>
        private bool CanArm(int index, double now)
        {
            ref readonly var slot = ref m_Slots[index];
            if (slot.Phase != QuestPhase.Inactive) return false;
            if (now < slot.CooldownUntilHour) return false;
            if (s_Roster[index].OncePerCity && slot.Offered) return false;
            return true;
        }

        private void Arm(int index, int anchor, double deadlineHour, int target)
        {
            ref var slot = ref m_Slots[index];
            slot.Phase = QuestPhase.Active;
            slot.Offered = true;
            slot.Progress = 0;
            slot.Target = target;
            slot.Anchor = anchor;
            slot.DeadlineHour = deadlineHour;

            PostFeed(s_Roster[index].Handle, s_Roster[index].SatireStart, SocialMood.Warning);
            Log.Info($"[Quest] {slot.Id} armed: anchor={anchor} deadline={(deadlineHour > 0 ? $"{deadlineHour:F1}h" : "none")} target={target}");
        }

        private void Complete(int index, double now)
        {
            ref var slot = ref m_Slots[index];
            slot.Phase = QuestPhase.Completed;
            slot.ClearAtHour = now + RESULT_DISPLAY_HOURS;
            StartCooldown(ref slot, now);

            PostFeed(s_Roster[index].Handle, s_Roster[index].SatireDone, SocialMood.Neutral);
            PublishResolved(index, success: true);
            Log.Info($"[Quest] {slot.Id} COMPLETED");
        }

        private void Fail(int index, double now)
        {
            ref var slot = ref m_Slots[index];
            slot.Phase = QuestPhase.Failed;
            slot.ClearAtHour = now + RESULT_DISPLAY_HOURS;
            StartCooldown(ref slot, now);

            var fail = s_Roster[index].SatireFail;
            if (fail != null)
                PostFeed(s_Roster[index].Handle, fail, SocialMood.Suffering);
            PublishResolved(index, success: false);
            Log.Info($"[Quest] {slot.Id} FAILED");
        }

        /// <summary>
        /// Announce a resolved quest so the UI can surface it. Before this, a resolution spoke
        /// only through a social-feed post and a chip that clears itself after
        /// RESULT_DISPLAY_HOURS — at normal speed both could pass unseen, so players met
        /// outcomes they were never told about. The counter travels with the event because the
        /// slot zeroes it when the chip clears.
        /// </summary>
        private void PublishResolved(int index, bool success)
        {
            ref readonly var slot = ref m_Slots[index];
            EventBus?.SafePublish(
                new QuestResolvedEvent((int)slot.Id, success, slot.Progress, slot.Target),
                nameof(QuestSystem));
        }

        private void StartCooldown(ref QuestSlot slot, double now)
        {
            // One shared cooldown knob today (Kotleta is the only repeatable quest);
            // a per-quest knob moves into QuestMeta the day a second repeatable appears.
            var cfg = BalanceConfig.Current.Narrative;
            int days = cfg.QuestKotletaCooldownDays >= 0 ? cfg.QuestKotletaCooldownDays : FALLBACK_KOTLETA_COOLDOWN_DAYS;
            slot.CooldownUntilHour = now + days * GameRate.HOURS_PER_DAY;
        }

        // ── Quest calls: offer toast → accept arms, decline/ring-out defers ─────────
        //
        // The quest no longer self-arms on its trigger. The trigger places a CALL — an
        // offer toast with the character's proposal. Accept arms the quest (deadline
        // counts from consent, which is also fairer than the old trigger-time start);
        // decline or letting the toast expire puts the slot on the regular cooldown so
        // the call comes back later ("didn't pick up — missed it"). The once-per-city
        // latch (slot.Offered, set in Arm) now means "taken once", not "called once" —
        // declining the Voice call does not lose it forever.

        private void OfferQuest(int index, int anchor, double now)
        {
            if (m_PendingOfferToastIds[index] != 0)
                return;

            var toastService = ResolveToastService();
            if (toastService == null)
            {
                // No toast surface (headless edge) — degrade to the pre-call behavior
                // rather than silently losing the quest.
                Log.Warn($"[Quest] {s_Roster[index].Id} call skipped: ToastService unavailable — arming directly");
                ArmByRoster(index, anchor, now);
                return;
            }

            int toastId = toastService.QueueOffer(
                ToastType.QuestOffer,
                Localization.LocalizationManager.Get(s_Roster[index].TitleKey),
                Localization.LocalizationManager.Get(s_Roster[index].OfferKey),
                Localization.LocalizationManager.Get("UI_QUEST_OFFER_ACCEPT"),
                Localization.LocalizationManager.Get("UI_QUEST_OFFER_DECLINE"),
                ToastPriority.High,
                contextData: anchor);
            if (toastId == 0)
            {
                // Toast queue full — arm directly instead of dropping the rare trigger.
                ArmByRoster(index, anchor, now);
                return;
            }

            m_PendingOfferToastIds[index] = toastId;
            m_PendingOfferAnchors[index] = anchor;
            Log.Info($"[Quest] {s_Roster[index].Id} call placed (toast #{toastId}, anchor={anchor})");
        }

        // Arm with the per-quest parameters the old direct triggers used. Deadlines are
        // computed HERE, at consent time — not at trigger time.
        private void ArmByRoster(int index, int anchor, double now)
        {
            switch (s_Roster[index].Id)
            {
                case QuestId.KotletaLights:
                    ArmKotleta(index, anchor, now);
                    break;
                case QuestId.RightVoice:
                    Arm(index, anchor, deadlineHour: 0.0, target: VoiceTarget());
                    break;
                case QuestId.AirDefenseReady:
                    // Deadline-free: the intro's activist window is the clock, and its close
                    // event resolves the slot either way.
                    Arm(index, anchor, deadlineHour: 0.0,
                        target: System.Math.Max(1, AirDefenseTarget(m_ActivistWindowManpower)));
                    break;
                default:
                    Log.Warn($"[Quest] ArmByRoster: no arm recipe for {s_Roster[index].Id}");
                    break;
            }
        }

        private Core.UI.Toast.ToastService? ResolveToastService()
        {
            var current = ServiceRegistry.TryGet<Core.UI.Toast.ToastService>();
            if (ReferenceEquals(current, m_ToastService))
                return m_ToastService;

            if (m_ToastService != null)
            {
                m_ToastService.OnToastInteracted -= OnQuestToastInteracted;
                m_ToastService.OnToastExpired -= OnQuestToastExpired;
            }
            m_ToastService = current;
            if (m_ToastService != null)
            {
                m_ToastService.OnToastInteracted += OnQuestToastInteracted;
                m_ToastService.OnToastExpired += OnQuestToastExpired;
            }
            return m_ToastService;
        }

        private int FindPendingByToast(int toastId)
        {
            if (toastId == 0) return -1;
            for (int i = 0; i < m_PendingOfferToastIds.Length; i++)
                if (m_PendingOfferToastIds[i] == toastId) return i;
            return -1;
        }

        private void ClearPendingOffer(int index)
        {
            m_PendingOfferToastIds[index] = 0;
            m_PendingOfferAnchors[index] = 0;
        }

        private void OnQuestToastInteracted(int toastId, bool accepted)
        {
            int index = FindPendingByToast(toastId);
            if (index < 0) return;

            int anchor = m_PendingOfferAnchors[index];
            ClearPendingOffer(index);
            if (!GameTimeSystem.TryGetGameHours(out var now))
                return;

            if (accepted && CanArm(index, now))
            {
                ArmByRoster(index, anchor, now);
                return;
            }

            // Declined — or accepted a call whose slot state changed underneath (should not
            // happen: anchor staleness dismisses the toast first). Same outcome: cooldown.
            StartCooldown(ref m_Slots[index], now);
            Log.Info($"[Quest] {s_Roster[index].Id} call {(accepted ? "accepted but stale" : "declined")} — cooldown started");
        }

        private void OnQuestToastExpired(int toastId)
        {
            int index = FindPendingByToast(toastId);
            if (index < 0) return;

            ClearPendingOffer(index);
            if (!GameTimeSystem.TryGetGameHours(out var now))
                return;
            StartCooldown(ref m_Slots[index], now);
            Log.Info($"[Quest] {s_Roster[index].Id} call rang out — cooldown started");
        }

        // ── Kotleta: lights before the deadline ──────────────────────────────

        private void OnBlackoutStarted(BlackoutStartedEvent evt)
        {
            // War gate: a peacetime blackout must not start a war-flavoured quest, and it
            // kept the reward flowing into the still-act-gated buckwheat feature.
            if (!IsWarStarted())
                return;

            int index = IndexOf(QuestId.KotletaLights);
            if (!GameTimeSystem.TryGetGameHours(out var now) || !CanArm(index, now))
                return;

            var cfg = BalanceConfig.Current.Narrative;
            float chance = cfg.QuestKotletaChance > 0f ? cfg.QuestKotletaChance : FALLBACK_KOTLETA_CHANCE;
            if (ThreadSafeRandom.NextFloat() >= chance)
                return;

            OfferQuest(index, evt.DistrictIndex, now);
        }

        private void ArmKotleta(int index, int districtIndex, double now)
        {
            var cfg = BalanceConfig.Current.Narrative;
            float deadline = cfg.QuestKotletaDeadlineHours > 0f ? cfg.QuestKotletaDeadlineHours : FALLBACK_KOTLETA_DEADLINE_HOURS;
            Arm(index, districtIndex, now + deadline, target: 0);
        }

        private void OnBlackoutEnded(BlackoutEndedEvent evt)
        {
            int index = IndexOf(QuestId.KotletaLights);

            // A pending CALL whose district recovered on its own is stale: accepting it
            // would arm a quest that is already complete. Retract the toast (clear first —
            // DismissToast fires OnToastExpired synchronously, and the handler must not
            // read this as "rang out" and start a cooldown).
            if (m_PendingOfferToastIds[index] != 0
                && (m_PendingOfferAnchors[index] == QuestSlot.NO_ANCHOR || m_PendingOfferAnchors[index] == evt.DistrictIndex))
            {
                int staleToast = m_PendingOfferToastIds[index];
                ClearPendingOffer(index);
                m_ToastService?.DismissToast(staleToast);
                Log.Info($"[Quest] KotletaLights call retracted: district {evt.DistrictIndex} recovered before the player answered");
            }

            ref readonly var slot = ref m_Slots[index];
            if (slot.Phase != QuestPhase.Active)
                return;
            if (slot.Anchor != QuestSlot.NO_ANCHOR && evt.DistrictIndex != slot.Anchor)
                return;
            if (!GameTimeSystem.TryGetGameHours(out var now))
                return;

            var cfg = BalanceConfig.Current.Narrative;
            float tons = cfg.QuestKotletaRewardTons >= 0f ? cfg.QuestKotletaRewardTons : FALLBACK_KOTLETA_REWARD_TONS;
            EventBus?.SafePublish(new GrantBuckwheatCommand(tons, "quest_kotleta"), nameof(QuestSystem));
            Complete(index, now);
        }

        // ── Voice: blunt raids with the right speaker ────────────────────────

        private void OnPsyWaveResult(PsyWaveResultEvent evt)
        {
            int index = IndexOf(QuestId.RightVoice);
            ref readonly var slot = ref m_Slots[index];

            if (slot.Phase != QuestPhase.Active)
            {
                // The first combined raid is the teaching moment: the mixed strike the
                // player just saw is exactly what the quest teaches them to answer.
                // Anchor = the arming wave, so its own blunts never count.
                if (evt.RaidCount < 2 || !GameTimeSystem.TryGetGameHours(out var armNow) || !CanArm(index, armNow))
                    return;
                OfferQuest(index, anchor: evt.Wave, armNow);
                return;
            }

            if (evt.Wave == slot.Anchor || evt.Blunted <= 0)
                return;

            ref var live = ref m_Slots[index];
            live.Progress += evt.Blunted;
            Log.Info($"[Quest] RightVoice progress: {live.Progress}/{live.Target} (wave={evt.Wave} blunted={evt.Blunted})");

            if (live.Progress < live.Target)
                return;

            var cfg = BalanceConfig.Current.Narrative;
            float trust = cfg.QuestVoiceRewardTrust > 0f ? cfg.QuestVoiceRewardTrust : FALLBACK_VOICE_REWARD_TRUST;
            EventBus?.SafePublish(new AdjustMediaTrustCommand(trust, "quest_voice"), nameof(QuestSystem));

            if (GameTimeSystem.TryGetGameHours(out var now))
                Complete(index, now);
        }

        private static int VoiceTarget()
        {
            int target = BalanceConfig.Current.Narrative.QuestVoiceBluntTarget;
            return target > 0 ? target : FALLBACK_VOICE_TARGET;
        }

        // ── Air defense: guns up before the opening strike ───────────────────
        //
        // The one quest that arms without a call toast: its call is the intro's ActivistCall
        // modal, which is already on screen when the window opens. Deadline-free — the window
        // itself is the clock, and its close event resolves the slot either way.

        private void OnActivistWindowOpened(ActivistWindowOpenedEvent evt)
        {
            int index = IndexOf(QuestId.AirDefenseReady);
            if (!GameTimeSystem.TryGetGameHours(out var now) || !CanArm(index, now))
                return;

            // Arming is a DECISION — it sizes a target the player is then scored against — so an
            // unmeasured pool withholds it instead of sizing off a substituted zero. Logged
            // apart from the "affords no gun" case below: both end in silence, and only the log
            // can say which silence this was.
            if (evt.AvailableManpower is not int availableManpower)
            {
                Log.Info("[Quest] AirDefenseReady not called: manpower pool unmeasured at window open");
                return;
            }

            if (AirDefenseTarget(availableManpower) <= 0)
            {
                // No crewable guns in reach (or mobilization not up yet): asking for one would
                // be a task the city cannot do. Stay silent rather than hand out a sure failure.
                Log.Info($"[Quest] AirDefenseReady not called: manpower={availableManpower} affords no gun");
                return;
            }

            // Guns already standing answer the ask before it is made, and the same restraint
            // applies: do not hand out a task the city has no reason to do. Progress counts only
            // placements made after arming, so without this a covered city was asked for one
            // more gun it did not need and failed for not building it (live 2026-07-26: a Gepard
            // with five crew on the map, a target of one, nothing left to place). Preparing
            // early was worth less than preparing late — exactly backwards.
            int crewOnDuty = AirDefenseCrewOnDuty();
            int target = System.Math.Max(1, AirDefenseTarget(availableManpower));
            if (crewOnDuty >= target)
            {
                AcknowledgeAirDefenseStanding(index, crewOnDuty, target, now);
                return;
            }

            // The window's manpower is the target's basis, but the target is only fixed when
            // the player picks up (ArmByRoster) — the call goes out first, like every quest.
            m_ActivistWindowManpower = availableManpower;
            OfferQuest(index, QuestSlot.NO_ANCHOR, now);
        }

        /// <summary>
        /// Crew standing on the live air-defense fleet, in the same unit the target is measured
        /// in. Counted as fleet times the per-type crew cost rather than read off a manpower
        /// field, because the mobilization contract exposes only what is FREE — people already
        /// on the guns are, by definition, not in it.
        ///
        /// Zero when AirDefense is closed (null-object fleet): nothing is standing, which is the
        /// honest answer, and the quest goes out as usual.
        /// </summary>
        private int AirDefenseCrewOnDuty()
        {
            var fleet = m_FleetReader.Fleet;
            if (fleet.TotalAa <= 0)
                return 0;

            var cfg = BalanceConfig.Current;
            return (fleet.HeritageCount * cfg.AAUnits.HeritageCrewRequired)
                 + (fleet.BoforsCount * cfg.Mobilization.BoforsCrew)
                 + (fleet.GepardCount * cfg.Mobilization.GepardCrew)
                 + (fleet.PatriotCount * cfg.Mobilization.PatriotCrew)
                 + (fleet.HawkCount * cfg.Mobilization.HawkCrew);
        }

        /// <summary>
        /// Settle the air-defense quest as done without ever offering it: the sky is covered, so
        /// there is nothing to accept. The reward pays out and the volunteers say so in the feed.
        ///
        /// No QuestResolvedEvent goes out, deliberately — that event cuts the intro's activist
        /// window short so the player is not left waiting after the task is settled. A city that
        /// came prepared keeps its full window instead: being ready must not pull the sirens
        /// forward.
        /// </summary>
        private void AcknowledgeAirDefenseStanding(int index, int crewOnDuty, int target, double now)
        {
            ref var slot = ref m_Slots[index];
            slot.Offered = true;
            slot.Phase = QuestPhase.Completed;
            // Chip reads "5/5", not "5/1": the target was never the ask here — the standing
            // fleet is both the requirement and the answer.
            slot.Progress = crewOnDuty;
            slot.Target = crewOnDuty;
            slot.Anchor = QuestSlot.NO_ANCHOR;
            slot.DeadlineHour = 0.0;
            slot.ClearAtHour = now + RESULT_DISPLAY_HOURS;
            StartCooldown(ref slot, now);

            PostFeed(s_Roster[index].Handle, SATIRE_AIRDEFENSE_ALREADY_STANDING, SocialMood.Neutral);
            EventBus?.SafePublish(new AirDefenseDrillRewardEvent(), nameof(QuestSystem));
            Log.Info($"[Quest] AirDefenseReady not called: {crewOnDuty} crew already on the guns (asked for {target}) — counted as done");
        }

        /// <summary>
        /// What the activists ask for, measured in CREW rather than in guns: half the manpower
        /// the city has free. Counting installations would be a lie — crew is per type
        /// (a heritage Bofors and a Patriot battery are not the same commitment), so "five guns"
        /// means wildly different amounts of the city's people depending on what was built.
        /// Half the pool means the same thing whatever the player chooses to field.
        ///
        /// Taken as a snapshot when the window opens: recomputing it live would shrink the
        /// target with every gun placed (each one takes its crew out of the pool), and the quest
        /// would finish itself.
        /// </summary>
        private static int AirDefenseTarget(int availableManpower)
            => availableManpower > 0 ? availableManpower / 2 : 0;

        private void OnAirDefensePlaced(AirDefensePlacedEvent evt)
        {
            if (!evt.Success) return;

            int index = IndexOf(QuestId.AirDefenseReady);
            ref var slot = ref m_Slots[index];
            if (slot.Phase != QuestPhase.Active) return;

            // Progress is CREW, matching the target: a heavy battery counts for what it actually
            // costs the city in people, not as one tick like a light gun.
            slot.Progress += System.Math.Max(1, evt.Crew);
            Log.Info($"[Quest] AirDefenseReady progress: {slot.Progress}/{slot.Target} crew ({evt.AaType} +{evt.Crew})");

            if (slot.Progress < slot.Target || !GameTimeSystem.TryGetGameHours(out var now))
                return;

            // Target reached — settle it now rather than waiting for the strike to end, so the
            // player learns the city is covered while it still matters to them. The wave-end
            // handler then finds the slot already resolved and leaves it alone.
            EventBus?.SafePublish(new AirDefenseDrillRewardEvent(), nameof(QuestSystem));
            Complete(index, now);
        }

        /// <summary>Free manpower right now — debug arm path only (see m_ManpowerReader). Empty
        /// when the pool is unmeasured; the debug arm then refuses rather than sizing a target
        /// off a substituted zero, because a forced quest is still a quest the player is scored
        /// against.</summary>
#if DEBUG
        private int? DebugAvailableManpower()
            => m_ManpowerReader.TryGetAvailableManpower(out int free) ? free : null;
#endif

        /// <summary>
        /// The quest runs THROUGH the opening strike, not up to the sirens: guns placed while
        /// the drones are already overhead defend the city just as well, and cutting the count
        /// off at the siren would punish a player who kept building under fire. The strike
        /// ending is the honest moment to score it.
        /// </summary>
        private void OnWaveEnded(WaveEndedEvent evt)
        {
            int index = IndexOf(QuestId.AirDefenseReady);
            ref readonly var slot = ref m_Slots[index];
            if (slot.Phase != QuestPhase.Active) return;
            if (!GameTimeSystem.TryGetGameHours(out var now)) return;

            if (slot.Progress >= slot.Target)
            {
                EventBus?.SafePublish(new AirDefenseDrillRewardEvent(), nameof(QuestSystem));
                Complete(index, now);
                return;
            }

            // Fewer guns than asked for, and the strike is over. No penalty — the damage the
            // city just took is punishment enough; the quest simply does not pay out.
            Fail(index, now);
        }

        // ── Debug (IQuestDebugMutator) ───────────────────────────────────────
        //
        // DEBUG-only, like the registration above and the only caller
        // (ScenarioDebugActionSystem, whose whole file is under #if DEBUG). Without the
        // guard the method survives into the Release DLL players get, with nothing able to
        // call it — CIVIC243 says exactly that, and it only fires in a Release build.

#if DEBUG
        public void DebugForceQuest(int questId, string source)
        {
            int index = IndexOf((QuestId)questId);
            if (index < 0)
            {
                Log.Warn($"[Quest] DebugForceQuest: unknown quest id {questId} ({source})");
                return;
            }
            if (!GameTimeSystem.TryGetGameHours(out var now))
            {
                Log.Warn($"[Quest] DebugForceQuest({questId}) skipped: game time unavailable");
                return;
            }

            // Force past chance / cooldown / once-per-city gates, but through the FULL
            // player path — the call toast included (the direct-arm shortcut made the call
            // untestable from the debug panel: "armed" appeared with no ring). Accepting
            // the call arms via ArmByRoster; Kotleta gets NO_ANCHOR so the next district
            // blackout to END completes it (testable via DebugForceGridCollapse + recovery).
            switch ((QuestId)questId)
            {
                case QuestId.KotletaLights:
                    OfferQuest(index, QuestSlot.NO_ANCHOR, now);
                    break;

                case QuestId.RightVoice:
                    OfferQuest(index, QuestSlot.NO_ANCHOR, now);
                    break;

                case QuestId.AirDefenseReady:
                    // No call toast to place: this quest's call is the intro's ActivistCall
                    // modal, so the debug path arms it directly. The target is the live
                    // manpower snapshot the intro would have sent, minimum one gun so the
                    // quest is always testable outside the cold open.
                    if (DebugAvailableManpower() is not int debugManpower)
                    {
                        Log.Warn($"[Quest] DebugForceQuest: AirDefenseReady not armed — manpower pool unmeasured ({source})");
                        return;
                    }
                    Arm(index, QuestSlot.NO_ANCHOR, deadlineHour: 0.0,
                        target: System.Math.Max(1, AirDefenseTarget(debugManpower)));
                    break;

                default:
                    Log.Warn($"[Quest] DebugForceQuest: quest {questId} has no debug arm path ({source})");
                    return;
            }
            Log.Info($"[Quest] DebugForceQuest: {(QuestId)questId} call placed ({source})");
        }
#endif

        // ── Feed ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Post a quest beat to the social feed. Same mechanism as HeroVoice: silent when the
        /// key is unregistered or unlocalized — no raw placeholder ever reaches the feed.
        /// Lifecycle beats bypass the feed's per-author cooldown (start and resolution can
        /// land inside its 60s real-time window at high game speed — S3 audit), while the
        /// duplicate filter still applies.
        /// </summary>
        private void PostFeed(string handle, string satireKey, SocialMood mood)
        {
            if (EventBus == null)
                return;
            if (!SatireRegistry.TryGetMessage(satireKey, out string message))
                return;

            EventBus.SafePublish(new SocialPostEvent(handle, message, mood, BypassAuthorCooldown: true), nameof(QuestSystem));
        }
    }
}
