using System;
using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Domains.Narrative.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Localization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Serialization;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.Narrative.Resolvers
{
    /// <summary>
    /// Resolves infrastructure events into notification DTOs.
    /// Handles: generator fire, cascade, winter, battery, disasters.
    /// Uses batching to aggregate multiple simultaneous equipment explosions into single notification.
    /// </summary>
    public sealed class InfraNarrativeResolver : INarrativeResolver
    {
        public string Domain => "Infra";

        private const int LOW_BATTERY_ALERT_THRESHOLD = 20;
        // Approximate Hz from stress ratio: linear interpolation between Normal(50Hz) and Collapse(48.5Hz)
        // stressPercent is 0-1 (GridStressData.StressPercent = StressHours / CollapseThresholdHours)
        private static float ApproxFrequencyHz(float stressPercent)
        {
            var cfg = BalanceConfig.Current.GridStress;
            return cfg.NormalFrequency - stressPercent * (cfg.NormalFrequency - cfg.CollapseFrequency);
        }

        private static readonly LogContext Log = new("InfraNarrativeResolver");

        private readonly NotificationState m_Sink;
        private IEventBus? m_EventBus;

        // State tracking
        private int m_PreviousBatteryPercent = 100;
        private bool m_WasWinterActive;
        private GridStressZone m_LastStressZone = GridStressZone.Normal;
        private bool m_IsGridCollapsed;
        private bool m_EmittedGridCollapseNarrative;
        private bool m_IsImportLimited;

        // R4-S4-05rev FIX: Suppress duplicate transition notifications after load.
        // These 4 fields reset to defaults on load (not serialized). First post-load events
        // re-trigger zone/collapse/winter notifications the player already saw.
        // Time-based: deterministic duration regardless of resolver update cadence.
        private const float SUPPRESS_DURATION_HOURS = 0.01f; // ~36 game-seconds
        private float m_SuppressUntilTime;
        private bool m_IsSuppressing;

        // Batch state for equipment explosions
        private readonly BatchAggregator<(int BuildingIndex, float WearPercent)> m_PendingExplosions = new(
            BatchIdentityPolicy.PerKeyDistinct<(int BuildingIndex, float WearPercent), int>(data => data.BuildingIndex));

        public InfraNarrativeResolver(NotificationState sink)
        {
            m_Sink = sink;
        }

        /// <summary>
        /// R4-S4-05rev FIX: Called after load to suppress first transition notifications.
        /// State fields (m_LastStressZone, m_IsGridCollapsed, etc.) are not serialized,
        /// so they reset to defaults. First post-load events would re-fire notifications
        /// the player already saw. This flag makes handlers update state without pushing.
        /// </summary>
        public void NotifyDeserialized()
        {
            m_SuppressUntilTime = -1f; // resolved on first Update
            m_IsSuppressing = true;
        }

        public NarrativeInfraResolverPersistState CapturePersistState()
        {
            return new NarrativeInfraResolverPersistState(
                m_PreviousBatteryPercent,
                m_WasWinterActive,
                (int)m_LastStressZone,
                m_IsGridCollapsed,
                m_EmittedGridCollapseNarrative,
                m_IsImportLimited);
        }

        public void RestorePersistState(in NarrativeInfraResolverPersistState state)
        {
            m_PreviousBatteryPercent = state.PreviousBatteryPercent;
            m_WasWinterActive = state.WasWinterActive;
            // Cast to byte (GridStressZone's underlying type) before IsDefined: the
            // persisted value is int and Enum.IsDefined throws on a type mismatch.
            // Mirrors GridStressSystem.Serialization.cs, the other consumer of this field.
            m_LastStressZone = Enum.IsDefined(typeof(GridStressZone), (byte)state.LastStressZone)
                ? (GridStressZone)state.LastStressZone
                : GridStressZone.Normal;
            m_IsGridCollapsed = state.IsGridCollapsed;
            m_EmittedGridCollapseNarrative = state.EmittedGridCollapseNarrative;
            m_IsImportLimited = state.IsImportLimited;
            m_SuppressUntilTime = 0f;
            m_IsSuppressing = false;
        }

        public void Subscribe()
        {
            m_EventBus = ServiceRegistry.Instance.Require<IEventBus>();

            m_EventBus.Subscribe<InfraEvent>(OnInfraEvent);
        }

        public void Unsubscribe()
        {
            if (m_EventBus == null)
            {
                Log.Warn("EventBus not cached during Unsubscribe");
                return;
            }

            m_EventBus.Unsubscribe<InfraEvent>(OnInfraEvent);
            m_EventBus = null;
        }

        /// <summary>
        /// Consolidated handler for all infrastructure events.
        /// Replaces 13 separate handlers.
        /// </summary>
        private void OnInfraEvent(InfraEvent evt)
        {
            switch (evt.Type)
            {
                case InfraEventType.GeneratorFire:
                    HandleGeneratorFire();
                    break;
                case InfraEventType.WinterCrisis:
                    HandleWinterCrisis();
                    break;
                case InfraEventType.WinterActivated:
                    // No narrative for activation — WinterCrisis handles the escalation.
                    // This case MUST exist to prevent "Unhandled InfraEventType" warning spam.
                    // Published by: WinterMultiplierSystem (symmetric with WinterEnded).
                    break;
                case InfraEventType.WinterEnded:
                    HandleWinterEnded();
                    break;
                case InfraEventType.BatteryLow:
                    HandleBatteryLow(evt.BatteryPercent);
                    break;
                case InfraEventType.BatteryDepleted:
                    HandleBatteryDepleted();
                    break;
                case InfraEventType.BatteryRecharged:
                    HandleBatteryRecharged();
                    break;
                case InfraEventType.PowerPlantDisaster:
                    HandlePowerPlantDisaster(evt.IsShady);
                    break;
                case InfraEventType.ImportLimitReached:
                    HandleImportLimitReached();
                    break;
                case InfraEventType.ImportLimitCleared:
                    m_IsImportLimited = false;
                    break;
                case InfraEventType.GridStressWarning:
                    HandleGridStressWarning(evt.StressPercent, evt.StressZone);
                    break;
                case InfraEventType.GridCollapse:
                    HandleGridCollapse();
                    break;
                case InfraEventType.GridRecovery:
                    HandleGridRecovery();
                    break;
                case InfraEventType.EquipmentExplosion:
                    HandleEquipmentExplosion(evt.BuildingIndex, evt.WearPercent);
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(InfraEventType)}: {evt.Type}");
                    break;
            }
        }

        private void HandleGeneratorFire()
        {
            // No suppress: instant event, not replayed after load

            // System alert
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("generator_fire"),
                LocalizationManager.Get("NOTIFY_TITLE_FIRE"),
                SatireRegistry.GetMessage("SATIRE_FIRE"),
                Status: NotificationStatus.Error
            ));

            // NEWS: Official report from DSNS (@DSNS_Official → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "DSNS",
                LocalizationManager.GetRandom("NEWS_GENERATOR_FIRE", "district"),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: Citizen reaction
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_GENERATOR_FIRE"),
                SocialMood.Suffering
            );
        }

        private void HandleWinterCrisis()
        {
            if (m_WasWinterActive) return;
            m_WasWinterActive = true;

            // R4-S4-05rev FIX: After load, update state without pushing (prevents duplicate)
            if (IsSuppressing()) return;

            // System alert
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("winter_crisis"),
                LocalizationManager.Get("NOTIFY_TITLE_WINTER"),
                SatireRegistry.GetMessage("SATIRE_WINTER"),
                Status: NotificationStatus.Warning
            ));

            // NEWS: Official weather warning (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_WINTER_WARNING"),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: Citizen suffering
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_WINTER_COLD"),
                SocialMood.Suffering
            );
        }

        private void HandleWinterEnded()
        {
            // H11: State mutation BEFORE suppress — suppress blocks side-effects (narrative),
            // not state tracking. Without this, m_WasWinterActive stays true permanently.
            m_WasWinterActive = false;
        }

        private void HandleBatteryLow(int percent)
        {
            // Always track current percent for accurate tier comparison
            if (percent > LOW_BATTERY_ALERT_THRESHOLD)
            {
                m_PreviousBatteryPercent = percent;
                return;
            }
            int prevTier = m_PreviousBatteryPercent / 10;
            int currTier = percent / 10;
            if (currTier >= prevTier) return;

            // R4-S4-05rev FIX: After load, update tracking state without pushing (prevents duplicate)
            // State update inside suppress branch — sets baseline for post-suppress tier comparison
            if (IsSuppressing())
            {
                m_PreviousBatteryPercent = percent;
                return;
            }

            m_PreviousBatteryPercent = percent;

            // System alert
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("battery_low"),
                LocalizationManager.Get("NOTIFY_TITLE_BATTERY"),
                SatireRegistry.GetMessage("SATIRE_BATTERY", percent),
                Status: NotificationStatus.Warning
            ));

            // NEWS: Official battery status (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_BATTERY_LOW", percent),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: Citizen panic (only at critical levels)
            if (percent <= 10)
            {
                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_BATTERY"),
                    SocialMood.Suffering
                );
            }
        }

        private void HandleBatteryDepleted()
        {
            // S19-H8 FIX: Update state BEFORE suppress check — otherwise m_PreviousBatteryPercent
            // stays at default 100 after load, breaking BatteryLow tier-transition logic.
            m_PreviousBatteryPercent = 0;
            if (IsSuppressing()) return;

            // Critical system alert
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("battery_depleted"),
                LocalizationManager.Get("NOTIFY_TITLE_BATTERY"),
                LocalizationManager.Get("NOTIFY_BATTERY_DEPLETED"),
                Status: NotificationStatus.Error
            ));

            // NEWS: Official announcement (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_BATTERY_DEPLETED"),
                string.Empty,
                SocialMood.Angry
            );
        }

        private void HandleBatteryRecharged()
        {
            m_PreviousBatteryPercent = 100;
        }

        private void HandlePowerPlantDisaster(bool isShady)
        {
            // No suppress: instant event, not replayed after load

            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("plant_disaster"),
                LocalizationManager.Get("NOTIFY_TITLE_PLANT_DISASTER"),
                SatireRegistry.GetMessage(isShady ? "SATIRE_SHADY_DISASTER" : "SATIRE_DISASTER"),
                Status: NotificationStatus.Error
            ));
        }

        private void HandleImportLimitReached()
        {
            if (m_IsImportLimited) return;
            m_IsImportLimited = true;
            if (IsSuppressing()) return;

            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("import_limit"),
                LocalizationManager.Get("NOTIFY_TITLE_GRID"),
                SatireRegistry.GetMessage("SATIRE_IMPORT"),
                Status: NotificationStatus.Warning
            ));
        }

        private void HandleGridStressWarning(float stressPercent, GridStressZone zone)
        {
            // Only notify on zone transitions (not every update)
            if (zone == m_LastStressZone)
                return;

            m_LastStressZone = zone;

            // Stress resolved to Normal — reset tracking only, no player notification
            if (zone == GridStressZone.Normal)
                return;

            // R4-S4-05rev FIX: After load, update state without pushing (prevents duplicate)
            if (IsSuppressing()) return;

            // System alert
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId($"grid_stress_{zone}"),
                LocalizationManager.Get("NOTIFY_TITLE_GRID_STRESS"),
                LocalizationManager.Get("NOTIFY_GRID_STRESS_MSG", ApproxFrequencyHz(stressPercent)),
                Status: zone == GridStressZone.Red ? NotificationStatus.Error : NotificationStatus.Warning
            ));

            // NEWS: Official warning (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_GRID_STRESS"),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: Citizen notices. Suppressed on Red — there the GridCritical modal
            // (GridStressSystem) carries the message, so the chirp would be redundant
            // noise on top of a hard-interrupt. Yellow keeps the chirp as soft flavor.
            if (zone != GridStressZone.Red)
            {
                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_GRID_STRESS"),
                    SocialMood.Warning
                );
            }
        }

        private void HandleGridCollapse()
        {
            if (m_IsGridCollapsed)
                return;

            m_IsGridCollapsed = true;

            // R4-S4-05rev FIX: After load, update state without pushing (prevents duplicate)
            if (IsSuppressing()) return;
            m_EmittedGridCollapseNarrative = true;

            float recoveryHours = BalanceConfig.Current.GridStress.RecoveryDurationHours;

            // System alert - CRITICAL
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("grid_collapse"),
                LocalizationManager.Get("NOTIFY_TITLE_GRID_COLLAPSE"),
                LocalizationManager.Get("NOTIFY_GRID_COLLAPSE_MSG", recoveryHours),
                Status: NotificationStatus.Error
            ));

            // NEWS: Emergency announcement (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_GRID_COLLAPSE"),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: Citizen panic
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_GRID_COLLAPSE"),
                SocialMood.Suffering
            );

            // SATIRE: Tech worker (citizen handle)
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "TECH_WORKER",
                SatireRegistry.GetMessage("SATIRE_GRID_COLLAPSE", recoveryHours),
                SocialMood.Suffering
            );
        }

        private void HandleGridRecovery()
        {
            bool shouldEmitRecovery = m_EmittedGridCollapseNarrative;
            m_IsGridCollapsed = false;
            m_EmittedGridCollapseNarrative = false;
            m_LastStressZone = GridStressZone.Normal;

            if (!shouldEmitRecovery || IsSuppressing()) return;

            // System alert - success
            m_Sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("grid_recovery"),
                LocalizationManager.Get("NOTIFY_TITLE_GRID_RECOVERY"),
                LocalizationManager.Get("NOTIFY_GRID_RECOVERY_MSG"),
                Status: NotificationStatus.Success
            ));

            // NEWS: Recovery announcement (@EnergyMinistry → Herald)
            NarrativeEmitter.EmitNews(
                m_EventBus,
                "ENERGY_MINISTRY",
                LocalizationManager.GetRandom("NEWS_GRID_RECOVERY"),
                string.Empty,
                SocialMood.Neutral
            );

            // CHIRP: Citizen relief
            NarrativeEmitter.EmitSocial(
                m_EventBus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_GRID_RECOVERY"),
                SocialMood.Neutral
            );
        }

        /// <summary>
        /// Accumulate equipment explosion event for batching.
        /// Will flush after BATCH_WINDOW_SECONDS.
        /// </summary>
        private void HandleEquipmentExplosion(int buildingIndex, float wearPercent)
        {
            if (IsSuppressing()) return;
#pragma warning disable CIVIC230 // PerKeyDistinct(BuildingIndex) owns duplicate suppression for this batch
            bool forceFlush = m_PendingExplosions.Add((buildingIndex, wearPercent));
#pragma warning restore CIVIC230

            if (forceFlush)
            {
                EmitExplosionNotifications(m_PendingExplosions.ForceFlush());
            }
        }

        /// <summary>
        /// Called every frame for batch flushing.
        /// Flushes pending equipment explosion events after batch window.
        /// </summary>
        /// <summary>
        /// Self-resolving suppress check — works from both Update() and event handlers.
        /// Not gated by resolver cadence: resolves the suppress window through TryGetGameHours.
        /// </summary>
        private bool IsSuppressing()
        {
            if (!m_IsSuppressing) return false;
            if (!GameTimeSystem.TryGetGameHours(out var gameHours))
            {
                Log.Warn("GameTimeSystem unavailable while resolving narrative suppress window");
                return true;
            }
            if (m_SuppressUntilTime < 0)
                m_SuppressUntilTime = gameHours + SUPPRESS_DURATION_HOURS;
            if (gameHours >= m_SuppressUntilTime)
            {
                m_IsSuppressing = false;
                return false;
            }
            return true;
        }

        public void Update(float currentTime)
        {
            IsSuppressing(); // advance state even if no events fire this tick

            try
            {
                if (m_PendingExplosions.IsReadyToFlush())
                {
                    EmitExplosionNotifications(m_PendingExplosions.FlushAndGet());
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error flushing explosions: {ex}");
                m_PendingExplosions.Clear();
            }
        }

        /// <summary>
        /// Reset transition and batch state for a new game/default load. Pending explosion toasts
        /// are deliberately discarded here; callers that need delivery must call FlushAll() first.
        /// </summary>
        public void Reset()
        {
            m_PendingExplosions.Clear();
            m_PreviousBatteryPercent = 100;
            m_WasWinterActive = false;
            m_LastStressZone = GridStressZone.Normal;
            m_IsGridCollapsed = false;
            m_EmittedGridCollapseNarrative = false;
            m_IsImportLimited = false;
            m_SuppressUntilTime = 0;
            m_IsSuppressing = false;
        }

        public void FlushAll()
        {
            var explosions = m_PendingExplosions.ForceFlush();
            if (explosions.Count > 0)
            {
                EmitExplosionNotifications(explosions);
            }
        }

        /// <summary>
        /// Emit notifications for equipment explosions.
        /// Single explosion: normal notification (4 toasts).
        /// Multiple explosions: aggregated notification (2 toasts).
        /// </summary>
        private void EmitExplosionNotifications(IReadOnlyList<(int BuildingIndex, float WearPercent)> explosions)
        {
            if (explosions.Count == 0) return;

            int explosionCount = explosions.Count;

            if (explosionCount == 1)
            {
                // Single explosion - normal notification (4 toasts)
                var (buildingIndex, wearPercent) = explosions[0];

                // FIX NAR-P1-003: Replace hardcoded building type with localization
                m_Sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId($"equipment_explosion_{buildingIndex}"),
                    LocalizationManager.Get("NOTIFY_TITLE_EQUIPMENT_EXPLOSION"),
                    LocalizationManager.Get("NOTIFY_EQUIPMENT_EXPLOSION_MSG", LocalizationManager.Get("BUILDING_TYPE_POWER_PLANT"), (int)Math.Round(wearPercent * 100)),
                    Status: NotificationStatus.Error
                ));

                // NEWS: official State Emergency Service channel (@DSNS_Official → Herald)
                NarrativeEmitter.EmitNews(
                    m_EventBus,
                    "DSNS",
                    LocalizationManager.GetRandom("NEWS_EQUIPMENT_EXPLOSION"),
                    string.Empty,
                    SocialMood.Warning
                );

                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_EQUIPMENT_EXPLOSION"),
                    SocialMood.Warning
                );

                // SATIRE: tech worker (citizen handle)
                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "TECH_WORKER",
                    SatireRegistry.GetMessage("SATIRE_EQUIPMENT_EXPLOSION", LocalizationManager.Get("BUILDING_TYPE_POWER_PLANT"), (int)Math.Round(wearPercent * 100)),
                    SocialMood.Angry
                );
            }
            else
            {
                // FIX NAR-P1-003: Replace hardcoded batch messages with localization
                // Multiple explosions - aggregated notification (2 toasts)
                m_Sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId("equipment_explosion_batch"),
                    LocalizationManager.Get("NOTIFY_TITLE_EQUIPMENT_EXPLOSION"),
                    LocalizationManager.Get("NOTIFY_EQUIPMENT_EXPLOSION_BATCH_MSG", explosionCount),
                    Status: NotificationStatus.Error
                ));

                NarrativeEmitter.EmitSocial(
                    m_EventBus,
                    "TECH_WORKER",
                    LocalizationManager.Get("SATIRE_EQUIPMENT_EXPLOSION_BATCH", explosionCount),
                    SocialMood.Angry
                );
            }
        }
    }
}
