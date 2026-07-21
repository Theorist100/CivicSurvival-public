using System;
using System.Collections.Generic;
using Colossal.Logging;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Narrative.Data;
using CivicSurvival.Domains.Narrative.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Interfaces.Domain.Narrative;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Narrative.Systems
{
    /// <summary>
    /// Narrative Bridge System - connects story characters to ECS world.
    /// Scans city state and triggers character reactions.
    /// Integrates with NotificationSystem for social feed display.
    /// Uses event-driven architecture - no direct dependencies on other domain systems.
    /// Implements INarrativeReactions for cross-domain decoupling.
    /// </summary>
    [ActIndependent]
    public partial class NarrativeSystem : CivicSystemBase, INarrativeReactions, INarrativeCharacterBindings, IDisposable
    {
        private const float RELATIONSHIP_ABANDON_THRESHOLD = -80f;
        private const float CORRUPT_BLACKOUT_PENALTY = 5f;
        private const int MAX_BLACKOUT_COUNT = 10000;

        private static readonly LogContext Log = new("NarrativeSystem");

        // Maps a character's cumulative blackout-day count to its reaction-escalation trigger.
        // Shared by the district-event path (OnDistrictBlackoutStarted) and the polling path
        // (CheckCharacterBlackout) so both stay in lockstep with NarrativeConfig thresholds.
        private static string ClassifyBlackoutTrigger(int blackoutCount)
        {
            var narCfg = BalanceConfig.Current.Narrative;
            if (blackoutCount >= narCfg.BlackoutExtremeDays)
                return ReactionTriggers.OnBlackoutExtreme;
            if (blackoutCount >= narCfg.BlackoutLongDays)
                return ReactionTriggers.OnBlackoutLong;
            return ReactionTriggers.OnBlackout;
        }

#pragma warning disable CIVIC278 // Values reset via ResetCharacterState(); Clear() would break — OnCreate populates, not re-called
        [System.NonSerialized] private Dictionary<string, StoryCharacter> m_Characters = new();
#pragma warning restore CIVIC278
        private EntityQuery m_IndustrialQuery;
        private EntityQuery m_PoliceQuery;
        private EntityQuery m_HospitalQuery;
        private EntityQuery m_PowerPlantQuery;
        private EntityQuery m_ResidentialQuery;
        private EntityQuery m_CommercialQuery;
        private EntityQuery m_OfficeQuery;
        private EntityQuery m_AdminBuildingQuery;
        private EntityQuery m_FireStationQuery;
        private EntityQuery m_WaterTreatmentQuery;

        [System.NonSerialized] private CharacterBindingHelper? m_BindingHelper;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private SimulationSystem m_SimulationSystem = null!;

        // BUG-7 FIX: Cache IDistrictStateReader (avoid per-frame ServiceRegistry.Get)
        private IDistrictStateReader? m_DistrictState;

        // Component lookups
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private EntityStorageInfoLookup m_BoundEntityStorageLookup;
        private ComponentLookup<ElectricityConsumer> m_ElectricityConsumerLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;

        private float m_UpdateTimer;
        private readonly List<int> m_PendingBlackoutStarts = new();
        private readonly List<int> m_PendingBlackoutEnds = new();

        // FIX NAR-P2-004: Track last game day for blackout count decay
        private int m_LastDecayDay = -1;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Characters = StoryCharacterRegistry.CreateCharacters();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            // Industrial buildings (for Kotleta's factory)
            m_IndustrialQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<IndustrialProperty>(),
                ComponentType.Exclude<Deleted>()
            );

            // Police stations (for Bezkyshen'ko)
            m_PoliceQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.PoliceStation>(),
                ComponentType.Exclude<Deleted>()
            );

            // Hospitals (future characters)
            m_HospitalQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.Hospital>(),
                ComponentType.Exclude<Deleted>()
            );

            // Power plants (for Petrenko)
            m_PowerPlantQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.ElectricityProducer>(),
                ComponentType.Exclude<Deleted>()
            );

            // Residential buildings (for Valera's khrushchevka)
            m_ResidentialQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.Exclude<Deleted>()
            );

            // Commercial buildings (for restaurant binding)
            m_CommercialQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<CommercialProperty>(),
                ComponentType.Exclude<Deleted>()
            );

            // Office buildings (for media binding)
            m_OfficeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<OfficeProperty>(),
                ComponentType.Exclude<Deleted>()
            );

            // Government buildings (CityHall slot)
            m_AdminBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.AdminBuilding>(),
                ComponentType.Exclude<Deleted>()
            );

            // Fire stations
            m_FireStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.FireStation>(),
                ComponentType.Exclude<Deleted>()
            );

            // Water treatment plants
            m_WaterTreatmentQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.WaterPumpingStation>(),
                ComponentType.Exclude<Deleted>()
            );

            // Cache lookups
            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_BoundEntityStorageLookup = GetEntityStorageInfoLookup();
            m_ElectricityConsumerLookup = GetComponentLookup<ElectricityConsumer>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            // Create binding helper (ECS-pure, no cross-domain dependencies)
            CreateBindingHelper();

            // Subscribe to events (decoupled from other domain systems)
            SubscribeRequired<BlackoutStartedEvent>(OnBlackoutStarted);
            SubscribeRequired<BlackoutEndedEvent>(OnBlackoutEnded);
            SubscribeRequired<NarrativeTriggerEvent>(OnNarrativeTrigger);

            // Register interface for cross-domain access
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<INarrativeReactions>(this);
                ServiceRegistry.Instance.Register<INarrativeCharacterBindings>(this);
            }

            Log.Info($"NarrativeSystem initialized with {m_Characters.Count} characters");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_DistrictState ??= ServiceRegistry.Instance.Require<IDistrictStateReader>();
            Log.Info("[NarrativeSystem] Started (ECS-pure, no cross-domain dependencies)");
        }

        private void CreateBindingHelper()
        {
            m_BindingHelper = new CharacterBindingHelper(
                m_IndustrialQuery,
                m_PoliceQuery,
                m_HospitalQuery,
                m_PowerPlantQuery,
                m_ResidentialQuery,
                m_CommercialQuery,
                m_OfficeQuery,
                m_AdminBuildingQuery,
                m_FireStationQuery,
                m_WaterTreatmentQuery
            );
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<BlackoutStartedEvent>(OnBlackoutStarted);
            UnsubscribeSafe<BlackoutEndedEvent>(OnBlackoutEnded);
            UnsubscribeSafe<NarrativeTriggerEvent>(OnNarrativeTrigger);

            // Unregister interface
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<INarrativeReactions>(this);
                ServiceRegistry.Instance.Unregister<INarrativeCharacterBindings>(this);
            }

            m_Characters?.Clear();
            m_PendingBlackoutStarts.Clear();
            m_PendingBlackoutEnds.Clear();
            m_BindingHelper?.Dispose();
            m_BindingHelper = null;

            base.OnDestroy();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            m_BindingHelper?.Dispose();
            m_BindingHelper = null;
        }

        private void OnBlackoutStarted(BlackoutStartedEvent evt)
        {
            if (!m_PendingBlackoutStarts.Contains(evt.DistrictIndex))
                m_PendingBlackoutStarts.Add(evt.DistrictIndex);
        }

        private void OnBlackoutEnded(BlackoutEndedEvent evt)
        {
            if (!m_PendingBlackoutEnds.Contains(evt.DistrictIndex))
                m_PendingBlackoutEnds.Add(evt.DistrictIndex);
        }

        /// <summary>
        /// Handle narrative triggers from other domains (milestones, etc.)
        /// </summary>
        private void OnNarrativeTrigger(NarrativeTriggerEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("Narrative.OnTrigger");
#pragma warning disable CIVIC135 // TriggerKey is string by design — narrative system uses string keys
            string? reactionTrigger = evt.TriggerKey switch
            {
                var key when key == NarrativeTrigger.Milestone180.ToKey() => ReactionTriggers.OnWarFatigue,
                var key when key == NarrativeTrigger.MilestoneVictory.ToKey() => ReactionTriggers.OnVictory,
                _ => null
            };

            if (reactionTrigger == null) return;

            if (!GameTimeSystem.TryGetGameHours(out var currentTime))
                return;

            foreach (var character in m_Characters.Values)
            {
                if (!character.IsActive) continue;
                if (!character.CanReact(currentTime)) continue;

                var reactionKey = character.Reactions.GetRandom(reactionTrigger);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                    {
                        character.RecordReaction(currentTime);
                        Log.Info($"[NarrativeSystem] {character.ID} reacted to {evt.TriggerKey}");
                    }
                }
            }
        }

        protected override void OnUpdateImpl()
        {
            m_UpdateTimer -= SystemAPI.Time.DeltaTime;
            if (m_UpdateTimer > 0) return;
            m_UpdateTimer = BalanceConfig.Current.Narrative.UpdateIntervalSeconds;

            // Update lookups
            m_CurrentDistrictLookup.Update(this);
            m_BoundEntityStorageLookup.Update(this);
            m_ElectricityConsumerLookup.Update(this);
            m_DeletedLookup.Update(this);
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            if (!GameTimeSystem.TryGetGameHours(out var currentTime))
                return;
            DrainBlackoutEvents(currentTime);

            // Decay blackout counts daily (FIX F-S4-06: handle multi-day jumps)
            int currentDay = (int)Math.Floor(currentTime / GameRate.HOURS_PER_DAY);
            if (m_LastDecayDay < 0)
                m_LastDecayDay = currentDay;
            if (currentDay > m_LastDecayDay)
            {
                int daysMissed = currentDay - m_LastDecayDay;
                m_LastDecayDay = currentDay;
                foreach (var character in m_Characters.Values)
                {
                    if (character.BlackoutCount > 0)
                        character.BlackoutCount = Math.Max(0, character.BlackoutCount - daysMissed);
                }
            }

            // Take snapshot once for all characters (not per-character)
            var vipSnapshot = m_DistrictState?.TakeSnapshot();

            foreach (var character in m_Characters.Values)
            {
                if (!character.IsActive) continue;
                bool boundThisTick = false;

                // 1. Angry cooldown expiry → transition to Waiting
                if (character.State == CharacterState.Angry &&
                    currentTime >= character.AngryUntilTime)
                {
                    character.State = CharacterState.Waiting;
                    character.AngryUntilTime = 0f;
                }

                // 2. Lazy binding (skip while Angry)
                if (character.BoundEntity == null && character.Slot != BindingSlot.None &&
                    character.State != CharacterState.Angry)
                {
                    if (!ecbCreated)
                    {
                        ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                        ecbCreated = true;
                    }
                    boundThisTick = m_BindingHelper?.TryBind(
                        character,
                        currentTime,
                        m_SimulationSystem.frameIndex,
                        m_CurrentDistrictLookup,
                        m_BoundEntityStorageLookup,
                        m_DeletedLookup,
                        ecb) == true;
                }

                // 3. Ongoing liveness check after bind-time candidates were source-validated.
                if (character.BoundEntity.HasValue && !boundThisTick)
                {
                    var bound = character.BoundEntity.Value;
                    if (!m_BoundEntityStorageLookup.Exists(bound) || m_DeletedLookup.HasComponent(bound))
                    {
                        HandleBuildingDestroyed(character, currentTime);
                    }
                }

                // 4. Check blackout conditions
                if (character.BoundEntity.HasValue)
                {
                    CheckCharacterBlackout(character, currentTime);
                }

                // 5. Check corruption conditions (all archetypes — GetRandom returns null if no reaction registered)
                CheckCorruptionReactions(character, currentTime, vipSnapshot);

                // 6. Character leaves if relationship too low
                if (character.Relationship < RELATIONSHIP_ABANDON_THRESHOLD && character.State != CharacterState.Gone)
                {
                    HandleCharacterLeaving(character);
                }
            }
        }

        private void DrainBlackoutEvents(float currentTime)
        {
            for (int i = 0; i < m_PendingBlackoutStarts.Count; i++)
                OnDistrictBlackoutStarted(m_PendingBlackoutStarts[i], currentTime);
            m_PendingBlackoutStarts.Clear();

            for (int i = 0; i < m_PendingBlackoutEnds.Count; i++)
                OnDistrictBlackoutEnded(m_PendingBlackoutEnds[i], currentTime);
            m_PendingBlackoutEnds.Clear();
        }

        private void OnDistrictBlackoutStarted(int districtIndex, float currentTime)
        {
            foreach (var character in m_Characters.Values)
            {
                if (!character.IsActive) continue;
                if (!character.BoundEntity.HasValue) continue;

                int characterDistrict = BuildingPowerUtils.GetBuildingDistrict(
                    character.BoundEntity.Value, m_CurrentDistrictLookup);
                if (characterDistrict != districtIndex) continue;

                // FIX H28: Skip if polling already detected this blackout (prevents double increment).
                // CheckCharacterBlackout sets IsInDistrictBlackout=true when it detects power loss.
                if (character.IsInDistrictBlackout) continue;

                // Side effects always apply regardless of cooldown
                character.BlackoutCount++;

                string trigger = ClassifyBlackoutTrigger(character.BlackoutCount);

                // S19-H3 FIX: Apply relationship penalty regardless of reaction availability.
                // Previously, characters without OnBlackoutExtreme/OnBlackoutLong reactions
                // were immune to blackout relationship damage.
                character.Relationship -= character.Archetype == CharacterArchetype.Corrupt ? CORRUPT_BLACKOUT_PENALTY : 2f;

                // Mark as covered by district event — suppresses duplicate polling chirp.
                character.IsInDistrictBlackout = true;

                // Chirp gated by cooldown — side effects above always apply
                if (!character.CanReact(currentTime)) continue;

                // Try specific trigger first, fall back to base OnBlackout
                var reactionKey = character.Reactions.GetRandom(trigger)
                                  ?? (trigger != ReactionTriggers.OnBlackout ? character.Reactions.GetRandom(ReactionTriggers.OnBlackout) : null);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }
        }

        private void OnDistrictBlackoutEnded(int districtIndex, float currentTime)
        {
            foreach (var character in m_Characters.Values)
            {
                if (!character.IsActive) continue;

                if (!character.BoundEntity.HasValue)
                {
                    character.IsInDistrictBlackout = false;
                    continue;
                }

                int characterDistrict = BuildingPowerUtils.GetBuildingDistrict(
                    character.BoundEntity.Value, m_CurrentDistrictLookup);
                if (characterDistrict != districtIndex) continue;

                character.IsInDistrictBlackout = false;

                if (!character.CanReact(currentTime)) continue;

                var reactionKey = character.Reactions.GetRandom(ReactionTriggers.OnPowerRestored);
                if (reactionKey != null)
                {
                    character.Relationship += 1f;
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }
        }

        private void HandleBuildingDestroyed(StoryCharacter character, float currentTime)
        {
            var narCfg = BalanceConfig.Current.Narrative;
            character.State = CharacterState.Angry;
            character.AngryUntilTime = currentTime + narCfg.AngryDurationHours;
            character.Relationship -= narCfg.RelationshipPenaltyMajor;
            character.BoundEntity = null;
            character.IsInDistrictBlackout = false;

            var reactionKey = character.Reactions.GetRandom(ReactionTriggers.OnBuildingDestroyed);
            if (reactionKey != null)
            {
                if (CharacterMessageSender.Send(character, reactionKey))
                    character.RecordReaction(currentTime);
            }

            Log.Info($"[NarrativeSystem] {character.ID}'s building destroyed");
        }

        private void CheckCharacterBlackout(StoryCharacter character, float currentTime)
        {
            if (!character.BoundEntity.HasValue) return;

            bool isAffected = BuildingPowerUtils.IsBuildingWithoutPower(
                character.BoundEntity.Value, m_ElectricityConsumerLookup);

            if (!isAffected)
            {
                character.IsInDistrictBlackout = false;
                return;
            }

            // Skip if already notified via district blackout event — prevents re-chirp during ongoing blackout.
            if (character.IsInDistrictBlackout) return;

            // FIX H28: Mark as polling-detected so district event handler
            // won't double-increment if it fires later this frame.
            character.IsInDistrictBlackout = true;

            // Per-building power loss — escalation must match district blackout path
            character.BlackoutCount++;

            string trigger = ClassifyBlackoutTrigger(character.BlackoutCount);

            character.Relationship -= character.Archetype == CharacterArchetype.Corrupt ? CORRUPT_BLACKOUT_PENALTY : 2f;

            if (!character.CanReact(currentTime)) return;

            var reactionKey = character.Reactions.GetRandom(trigger);
            if (reactionKey != null)
            {
                if (CharacterMessageSender.Send(character, reactionKey))
                    character.RecordReaction(currentTime);
            }
        }

        private void CheckCorruptionReactions(StoryCharacter character, float currentTime, DistrictStateSnapshot? snapshot)
        {
            if (!character.CanReact(currentTime)) return;

            var narCfg = BalanceConfig.Current.Narrative;

            // Check VIP protection
            if (snapshot == null) return;
            var vipSnapshot = snapshot.Value;
            if (vipSnapshot.VIPDistricts != null && vipSnapshot.VIPDistricts.Count > 0 &&
                ThreadSafeRandom.NextFloat() < narCfg.VipReactionChance)
            {
                var reactionKey = character.Reactions.GetRandom(ReactionTriggers.OnVIPProtection);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }
        }

        private void HandleCharacterLeaving(StoryCharacter character)
        {
            character.State = CharacterState.Gone;
            character.IsActive = false;
            character.BoundEntity = null;
            character.IsInDistrictBlackout = false;

            var reactionKey = character.Reactions.GetRandom(ReactionTriggers.OnLeaving);
            if (reactionKey != null)
            {
                CharacterMessageSender.Send(character, reactionKey);
            }

            Log.Info($"[NarrativeSystem] {character.ID} has left the city");
        }

        // ========================================
        // PUBLIC API
        // ========================================

        public void TriggerReaction(CharacterArchetype archetype, string trigger)
        {
            if (!GameTimeSystem.TryGetGameHours(out var currentTime))
                return;

            foreach (var character in m_Characters.Values)
            {
                if (!character.IsActive) continue;
                if (character.Archetype != archetype) continue;
                if (!character.CanReact(currentTime)) continue;

                var reactionKey = character.Reactions.GetRandom(trigger);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }
        }

        public void TriggerCharacterReaction(string characterName, string trigger)
        {
            if (!m_Characters.TryGetValue(characterName, out var character)) return;
            if (!character.IsActive) return;

            if (!GameTimeSystem.TryGetGameHours(out var currentTime))
                return;
            if (!character.CanReact(currentTime)) return;

            var reactionKey = character.Reactions.GetRandom(trigger);
            if (reactionKey != null)
            {
                if (CharacterMessageSender.Send(character, reactionKey))
                    character.RecordReaction(currentTime);
            }
        }

        public Entity GetBoundEntity(string characterId)
        {
            if (!m_Characters.TryGetValue(characterId, out var character)) return Entity.Null;
            if (!character.IsActive || !character.BoundEntity.HasValue) return Entity.Null;
            return character.BoundEntity.Value;
        }

    }
}
