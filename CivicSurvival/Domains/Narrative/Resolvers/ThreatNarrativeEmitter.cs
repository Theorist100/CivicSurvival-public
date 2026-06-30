using System;
using System.Collections.Generic;
using Unity.Mathematics;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Narrative.Infrastructure;
using CivicSurvival.Localization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Narrative.Resolvers
{
    /// <summary>
    /// Stateless notification emitter for the threat narrative domain. Every method
    /// formats one or more <see cref="NarrativeToastDto"/> values and pushes them
    /// into the provided <see cref="NotificationState"/> sink.
    /// </summary>
    internal static class ThreatNarrativeEmitter
    {
        private const float MS_PER_SECOND = 1000f;
        private const float SLOW_THRESHOLD_MS = 5f;

        // Energy Ministry author identity for the EmitNews pilot. Handle = stable id key
        // (matches PersonaRegistry "ENERGY_MINISTRY" / IsOfficialAuthor); display = Herald
        // source string (matches GetAuthorDisplayName). The handle→display table currently
        // lives in SocialFeedService; unifying it into one Core registry is a later refactor
        // step — until then these mirror that table for the migrated channel.
        private const string EnergyMinistryHandle = "@EnergyMinistry";
        private const string EnergyMinistryDisplay = "Ministry of Energy";

        private static readonly LogContext Log = new("ThreatNarrativeEmitter");

        public static string SpatialId(string baseId, float3 position)
        {
            int x = (int)math.round(position.x / 100f);
            int z = (int)math.round(position.z / 100f);
            return $"{baseId}_{x}_{z}";
        }

        public static void EmitWavePhaseChanged(NotificationState sink, GamePhase phase, int waveNumber)
        {
            if (phase != GamePhase.Calm && phase != GamePhase.Alert && phase != GamePhase.Attack && phase != GamePhase.Recovery)
            {
                Log.Warn($"Unknown wave phase for narrative: {phase}");
                return;
            }

            string phaseName = phase switch
            {
                GamePhase.Calm => LocalizationManager.Get("THREAT_PHASE_CALM"),
                GamePhase.Alert => LocalizationManager.Get("THREAT_PHASE_ALERT"),
                GamePhase.Attack => LocalizationManager.Get("THREAT_PHASE_ATTACK"),
                GamePhase.Recovery => LocalizationManager.Get("THREAT_PHASE_RECOVERY"),
                _ => phase.ToString()
            };

            if (phase == GamePhase.Alert || phase == GamePhase.Attack)
            {
                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    $"wave_{waveNumber}_{phase}",
                    LocalizationManager.Get("THREAT_ALERT_TITLE"),
                    $"{phaseName} - Wave #{waveNumber}",
                    Status: NotificationStatus.Warning
                ));
            }
        }

        public static void EmitThreatAlert(NotificationState sink, IEventBus? bus, int waveNumber, int threatCount)
        {
            string message = LocalizationManager.Get("THREAT_ALERT_MESSAGE", threatCount, waveNumber);

            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                $"threat_alert_{waveNumber}",
                LocalizationManager.Get("THREAT_ALERT_TITLE"),
                message,
                Status: NotificationStatus.Warning
            ));

            // NEWS: official emergency channel (@CityEmergency).
            NarrativeEmitter.EmitNews(
                bus,
                "CITY_EMERGENCY",
                LocalizationManager.GetRandom("NEWS_ALERT_INCOMING", threatCount),
                string.Empty,
                SocialMood.Warning
            );

            // CHIRP: citizen reaction.
            NarrativeEmitter.EmitSocial(
                bus,
                "LOCAL_RESIDENT",
                LocalizationManager.GetRandom("CHIRP_ALERT_FEAR"),
                SocialMood.Suffering
            );
        }

        public static void EmitHospitalScandal(NotificationState sink, IEventBus? bus, float3 position)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId(SpatialId("hospital_scandal", position)),
                LocalizationManager.Get("NOTIFY_TITLE_SCANDAL"),
                LocalizationManager.Get("NOTIFY_SCANDAL_HOSPITAL_HIT"),
                Status: NotificationStatus.Error
            ));

            // CHIRP: citizen voices (@MarianaPravda, @AngryDoctor).
            NarrativeEmitter.EmitSocial(
                bus,
                "MARIANA",
                LocalizationManager.Get("SCANDAL_MARIANNA_HOSPITAL_1"),
                SocialMood.Angry
            );

            NarrativeEmitter.EmitSocial(
                bus,
                "MARIANA",
                LocalizationManager.Get("SCANDAL_MARIANNA_HOSPITAL_2"),
                SocialMood.Angry
            );

            NarrativeEmitter.EmitSocial(
                bus,
                "ANGRY_DOCTOR",
                LocalizationManager.Get("SCANDAL_DOCTOR_REACT"),
                SocialMood.Suffering
            );
        }

        public static void EmitFirstStrike(NotificationState sink, int affectedPlantCount)
        {
            int plantCount = Math.Max(1, affectedPlantCount);
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("first_strike"),
                LocalizationManager.Get("NOTIFY_TITLE_FIRST_STRIKE"),
                LocalizationManager.Get("NOTIFY_FIRST_STRIKE_MSG", plantCount),
                Status: NotificationStatus.Error
            ));
        }

        public static void EmitPowerPlantDamage(NotificationState sink, IEventBus? bus, int reportMW, int remainingMW)
        {
            // Toast: ephemeral, must stay unique (TimedId) so the cooldown never swallows a
            // repeat alert of the same type. Routed through the sink → NotificationSystem.
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("power_plant_damage"),
                LocalizationManager.Get("NOTIFY_TITLE_POWER_DAMAGE"),
                LocalizationManager.Get("NOTIFY_POWER_DAMAGE_MSG", reportMW, remainingMW),
                Status: NotificationStatus.Error
            ));

            // News (Herald): channel chosen explicitly via EmitNews, published straight onto
            // NewsPostEvent with a content-stable ContentId — no NotificationSystem author
            // demux, and m_SeenIds dedup now actually collapses the triple-emit duplicate.
            NarrativeEmitter.EmitNews(
                bus,
                EnergyMinistryHandle,
                EnergyMinistryDisplay,
                LocalizationManager.Get("ENERGY_MINISTRY_DAMAGE", reportMW),
                string.Empty,
                SocialMood.Warning
            );
        }

        public static void EmitPowerPlantDamageNoSocial(NotificationState sink, int reportMW, int remainingMW)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("power_plant_damage"),
                LocalizationManager.Get("NOTIFY_TITLE_POWER_DAMAGE"),
                LocalizationManager.Get("NOTIFY_POWER_DAMAGE_MSG", reportMW, remainingMW),
                Status: NotificationStatus.Error
            ));
        }

        public static void EmitRepairNoFunds(NotificationState sink)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("repair_no_funds"),
                LocalizationManager.Get("NOTIFY_TITLE_NO_FUNDS"),
                LocalizationManager.Get("NOTIFY_NO_FUNDS_REPAIR"),
                Status: NotificationStatus.Error
            ));
        }

        public static void EmitAAInstallationLost(NotificationState sink, float3 position)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId(SpatialId("aa_installation_lost", position)),
                LocalizationManager.Get("NOTIFY_TITLE_AA_LOST"),
                LocalizationManager.Get("NOTIFY_AA_LOST_MSG"),
                Status: NotificationStatus.Warning
            ));
        }

        public static void EmitWaveEnded(NotificationState sink, IEventBus? bus, WaveEndedEvent evt)
        {
            float interceptRate = evt.Intercepted + evt.Hits > 0
                ? (float)evt.Intercepted / (evt.Intercepted + evt.Hits) * 100f
                : 0f;

            string result = evt.Hits == 0
                ? LocalizationManager.Get("THREAT_WAVE_END_SUCCESS")
                : LocalizationManager.Get("THREAT_WAVE_END_RESULT", evt.Hits, evt.Intercepted, (int)interceptRate);

            var status = evt.Hits == 0 ? NotificationStatus.Success : NotificationStatus.Warning;

            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                $"wave_end_{evt.WaveNumber}",
                LocalizationManager.Get("NOTIFY_TITLE_ATTACK_ENDED"),
                LocalizationManager.Get("NOTIFY_WAVE_COMPLETE", evt.WaveNumber, result),
                Status: status
            ));

            // NEWS: official city channel (@CityAlert).
            NarrativeEmitter.EmitNews(
                bus,
                "CITY_ALERT",
                LocalizationManager.GetRandom("NEWS_INTERCEPT_SUCCESS", evt.Intercepted, evt.Intercepted + evt.Hits, (int)interceptRate),
                string.Empty,
                evt.Hits == 0 ? SocialMood.Neutral : SocialMood.Warning
            );

            if (evt.Hits > 0)
            {
                NarrativeEmitter.EmitSocial(
                    bus,
                    "MARIANA",
                    LocalizationManager.Get("THREAT_WAVE_END_SOCIAL_BAD", evt.WaveNumber, evt.Hits, evt.Intercepted),
                    SocialMood.Angry
                );
            }
            else
            {
                NarrativeEmitter.EmitSocial(
                    bus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_INTERCEPT_RELIEF"),
                    SocialMood.Neutral
                );
            }
        }

        public static void EmitAAResupply(NotificationState sink, IEventBus? bus, AAResupplyEvent evt)
        {
            switch (evt.Result)
            {
                case AAResupplyResult.Full:
                    sink.Push(new NarrativeToastDto(
                        NotificationType.SystemAlert,
                        NarrativeEmitter.TimedId("aa_resupply"),
                        LocalizationManager.Get("NOTIFY_TITLE_AA_RESUPPLY"),
                        LocalizationManager.Get("NOTIFY_AA_RESUPPLY_MSG", evt.Rounds, evt.Cost),
                        Status: NotificationStatus.Success
                    ));
                    if (PersonaRegistry.TryResolve("TECH_WORKER", out var techWorker))
                    {
                        bus.SafePublish(new SocialPostEvent(
                            techWorker.Handle,
                            SatireRegistry.GetMessage(techWorker.MessageKeyPrefix),
                            SocialMood.Neutral), "ThreatNarrativeEmitter");
                    }
                    break;

                case AAResupplyResult.Partial:
                    sink.Push(new NarrativeToastDto(
                        NotificationType.SystemAlert,
                        NarrativeEmitter.TimedId("aa_partial"),
                        LocalizationManager.Get("NOTIFY_TITLE_AA_PARTIAL"),
                        LocalizationManager.Get("NOTIFY_AA_PARTIAL_MSG", evt.Rounds, evt.Needed),
                        Status: NotificationStatus.Warning
                    ));
                    NarrativeEmitter.EmitSocial(
                        bus,
                        "MARIANA",
                        LocalizationManager.Get("THREAT_AA_PARTIAL", evt.Rounds, evt.Needed),
                        SocialMood.Angry
                    );
                    break;

                case AAResupplyResult.Failed:
                    sink.Push(new NarrativeToastDto(
                        NotificationType.SystemAlert,
                        NarrativeEmitter.TimedId("aa_no_ammo"),
                        LocalizationManager.Get("NOTIFY_TITLE_NO_AMMO"),
                        LocalizationManager.Get("NOTIFY_NO_AMMO_MSG", evt.Cost),
                        Status: NotificationStatus.Error
                    ));
                    NarrativeEmitter.EmitSocial(
                        bus,
                        "VALERA",
                        LocalizationManager.Get("THREAT_AA_FAILED_1"),
                        SocialMood.Angry
                    );
                    NarrativeEmitter.EmitSocial(
                        bus,
                        "MARIANA",
                        LocalizationManager.Get("THREAT_AA_FAILED_2"),
                        SocialMood.Angry
                    );
                    break;

                case AAResupplyResult.Emergency:
                    sink.Push(new NarrativeToastDto(
                        NotificationType.SystemAlert,
                        NarrativeEmitter.TimedId("aa_emergency"),
                        LocalizationManager.Get("NOTIFY_TITLE_EMERGENCY_RESUPPLY"),
                        LocalizationManager.Get("NOTIFY_EMERGENCY_MSG", evt.Cost),
                        Status: NotificationStatus.Warning
                    ));
                    if (PersonaRegistry.TryResolve("TECH_WORKER", out var emergencyWorker))
                    {
                        bus.SafePublish(new SocialPostEvent(
                            emergencyWorker.Handle,
                            SatireRegistry.GetMessage(emergencyWorker.MessageKeyPrefix),
                            SocialMood.Neutral), "ThreatNarrativeEmitter");
                    }
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(AAResupplyResult)}: {evt.Result}");
                    break;
            }
        }

        public static void EmitHeritageGranted(NotificationState sink, IEventBus? bus, HeritageGrantedEvent evt)
        {
            sink.Push(new NarrativeToastDto(
                NotificationType.SystemAlert,
                NarrativeEmitter.TimedId("heritage_granted"),
                LocalizationManager.Get("NOTIFY_TITLE_HERITAGE_AA"),
                LocalizationManager.Get("NOTIFY_HERITAGE_AA_MSG", evt.Count),
                Status: NotificationStatus.Success
            ));

            // NEWS: official TRO command channel (@TRO_Commander).
            NarrativeEmitter.EmitNews(
                bus,
                "TRO_COMMANDER",
                LocalizationManager.Get("HERITAGE_TRO_MESSAGE", evt.Count),
                string.Empty,
                SocialMood.Neutral
            );

            // CHIRP: @MilitaryAdvisor is not an official-feed handle → citizen channel
            // (preserves the pre-refactor demux routing).
            NarrativeEmitter.EmitSocial(
                bus,
                "MILITARY_ADVISOR",
                LocalizationManager.Get("HERITAGE_UPGRADE_HINT"),
                SocialMood.Warning
            );
        }

        public static void EmitIntercepts(NotificationState sink, IEventBus? bus, IReadOnlyList<ThreatInterceptEvent> intercepts)
        {
            if (intercepts.Count == 0) return;

            int ballisticCount = 0;
            for (int i = 0; i < intercepts.Count; i++)
            {
                if (intercepts[i].IsBallistic)
                    ballisticCount++;
            }

            int droneCount = intercepts.Count - ballisticCount;
            string message;
            if (intercepts.Count == 1)
            {
                string key = intercepts[0].IsBallistic ? "THREAT_INTERCEPT_BALLISTIC" : "THREAT_INTERCEPT_DRONE";
                message = LocalizationManager.Get(key);
            }
            else
            {
                message = LocalizationManager.Get("THREAT_INTERCEPT_BATCH", intercepts.Count, ballisticCount, droneCount);
            }

            // NEWS: official emergency channel (@CityEmergency).
            NarrativeEmitter.EmitNews(
                bus,
                "CITY_EMERGENCY",
                message,
                string.Empty,
                SocialMood.Neutral
            );
        }

        public static void EmitImpacts(NotificationState sink, IEventBus? bus, IReadOnlyList<ThreatImpactEvent> impacts)
        {
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            if (impacts.Count == 0) return;

            int impactCount = impacts.Count;
            int ballisticCount = 0;
            for (int i = 0; i < impacts.Count; i++)
            {
                if (impacts[i].IsBallistic) ballisticCount++;
            }
            int droneCount = impactCount - ballisticCount;

            if (impactCount == 1)
            {
                var evt = impacts[0];
                string threatType = evt.IsBallistic
                    ? LocalizationManager.Get("THREAT_IMPACT_BALLISTIC")
                    : LocalizationManager.Get("THREAT_IMPACT_DRONE");

                string message = LocalizationManager.Get("THREAT_IMPACT_MESSAGE", threatType);

                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId("threat_impact"),
                    LocalizationManager.Get("NOTIFY_TITLE_IMPACT"),
                    message,
                    Status: NotificationStatus.Error
                ));

                if (PersonaRegistry.TryResolve("CITIZEN", out var persona))
                {
                    bus.SafePublish(new SocialPostEvent(
                        persona.Handle,
                        SatireRegistry.GetMessage(persona.MessageKeyPrefix),
                        SocialMood.Suffering), "ThreatNarrativeEmitter");
                }
            }
            else
            {
                string message;
                if (ballisticCount > 0 && droneCount > 0)
                    message = LocalizationManager.Get("NOTIFY_IMPACT_BATCH_MIXED", impactCount, ballisticCount, droneCount);
                else if (ballisticCount > 0)
                    message = LocalizationManager.Get("NOTIFY_IMPACT_BATCH_BALLISTIC", impactCount);
                else
                    message = LocalizationManager.Get("NOTIFY_IMPACT_BATCH_DRONE", impactCount);

                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId("threat_impact_batch"),
                    LocalizationManager.Get("NOTIFY_TITLE_IMPACT"),
                    message,
                    Status: NotificationStatus.Error
                ));

                NarrativeEmitter.EmitSocial(
                    bus,
                    "CITIZEN",
                    LocalizationManager.Get("CHIRP_IMPACT_BATCH", impactCount),
                    SocialMood.Suffering
                );
            }

            float impactElapsedMs = (UnityEngine.Time.realtimeSinceStartup - t0) * MS_PER_SECOND;
            if (impactElapsedMs > SLOW_THRESHOLD_MS)
            {
                Log.Warn($"SLOW EmitImpact={impactElapsedMs:F0}ms count={impactCount}");
            }
        }

        public static void EmitDebris(NotificationState sink, IEventBus? bus, IReadOnlyList<float3> debris)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (debris.Count == 0) return;

            int debrisCount = debris.Count;
            int gcBefore = System.GC.CollectionCount(2);

            if (debrisCount == 1)
            {
                long t1 = sw.ElapsedMilliseconds;
                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId("threat_debris"),
                    LocalizationManager.Get("NOTIFY_TITLE_DEBRIS"),
                    LocalizationManager.Get("THREAT_DEBRIS_MESSAGE"),
                    Status: NotificationStatus.Error
                ));
                long t2 = sw.ElapsedMilliseconds;

                // NEWS: official State Emergency Service channel (@DSNS_Official).
                NarrativeEmitter.EmitNews(
                    bus,
                    "DSNS",
                    LocalizationManager.GetRandom("NEWS_DEBRIS_DAMAGE", "district"),
                    string.Empty,
                    SocialMood.Warning
                );
                long t3 = sw.ElapsedMilliseconds;

                NarrativeEmitter.EmitSocial(
                    bus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.GetRandom("CHIRP_DEBRIS"),
                    SocialMood.Suffering
                );
                long t4 = sw.ElapsedMilliseconds;

                sw.Stop();
                int gcAfter = System.GC.CollectionCount(2);
                if (sw.ElapsedMilliseconds > 5)
                {
                    Log.Warn($"SLOW EmitDebris1={sw.ElapsedMilliseconds}ms t1={t1} t2={t2} t3={t3} t4={t4} gc2={gcAfter - gcBefore}");
                }
            }
            else
            {
                long t1 = sw.ElapsedMilliseconds;
                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    NarrativeEmitter.TimedId("threat_debris_batch"),
                    LocalizationManager.Get("NOTIFY_TITLE_DEBRIS"),
                    LocalizationManager.Get("NOTIFY_DEBRIS_BATCH_MSG", debrisCount),
                    Status: NotificationStatus.Error
                ));
                long t2 = sw.ElapsedMilliseconds;

                NarrativeEmitter.EmitSocial(
                    bus,
                    "LOCAL_RESIDENT",
                    LocalizationManager.Get("CHIRP_DEBRIS_BATCH", debrisCount),
                    SocialMood.Suffering
                );
                long t3 = sw.ElapsedMilliseconds;

                sw.Stop();
                int gcAfter = System.GC.CollectionCount(2);
                if (sw.ElapsedMilliseconds > 5)
                {
                    Log.Warn($"SLOW EmitDebrisN={sw.ElapsedMilliseconds}ms count={debrisCount} t1={t1} t2={t2} t3={t3} gc2={gcAfter - gcBefore}");
                }
            }
        }
    }
}
