using System;
using System.Collections.Generic;
using Colossal.Logging;
using Game.Areas;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.Utils.ThreadSafeRandom;
using CivicSurvival.Domains.Narrative.Data;

namespace CivicSurvival.Domains.Narrative.Services
{
    /// <summary>
    /// Helper for binding story characters to buildings.
    /// Extracted from NarrativeSystem to reduce complexity.
    /// </summary>
    public sealed class CharacterBindingHelper : IDisposable
    {
        private static readonly LogContext Log = new("CharacterBinding");

        private readonly EntityQuery m_IndustrialQuery;
        private readonly EntityQuery m_PoliceQuery;
        private readonly EntityQuery m_HospitalQuery;
        private readonly EntityQuery m_PowerPlantQuery;
        private readonly EntityQuery m_ResidentialQuery;
        private readonly EntityQuery m_CommercialQuery;
        private readonly EntityQuery m_OfficeQuery;
        private readonly EntityQuery m_AdminBuildingQuery;
        private readonly EntityQuery m_FireStationQuery;
        private readonly EntityQuery m_WaterTreatmentQuery;
        // Throttle for binding warnings (log once per minute, not every frame)
#pragma warning disable CIVIC152 // Bounded by character count (~10 characters)
        private readonly Dictionary<string, float> m_LastBindingWarnTime = new();
        private readonly Dictionary<BindingSlot, CandidateCache> m_CandidateCache = new();
#pragma warning restore CIVIC152
        private const float BINDING_WARN_THROTTLE_SECONDS = 60f;

        public CharacterBindingHelper(
            EntityQuery industrialQuery,
            EntityQuery policeQuery,
            EntityQuery hospitalQuery,
            EntityQuery powerPlantQuery,
            EntityQuery residentialQuery,
            EntityQuery commercialQuery,
            EntityQuery officeQuery,
            EntityQuery adminBuildingQuery,
            EntityQuery fireStationQuery,
            EntityQuery waterTreatmentQuery)
        {
            m_IndustrialQuery = industrialQuery;
            m_PoliceQuery = policeQuery;
            m_HospitalQuery = hospitalQuery;
            m_PowerPlantQuery = powerPlantQuery;
            m_ResidentialQuery = residentialQuery;
            m_CommercialQuery = commercialQuery;
            m_OfficeQuery = officeQuery;
            m_AdminBuildingQuery = adminBuildingQuery;
            m_FireStationQuery = fireStationQuery;
            m_WaterTreatmentQuery = waterTreatmentQuery;
        }

        /// <summary>
        /// Try to bind a character to an appropriate building.
        /// Returns true if binding was successful or already bound.
        /// </summary>
        public bool TryBind(
            StoryCharacter character,
            float currentTime,
            uint currentFrame,
            ComponentLookup<CurrentDistrict> districtLookup,
            EntityStorageInfoLookup storageLookup,
            ComponentLookup<Deleted> deletedLookup,
            EntityCommandBuffer ecb)
        {
            if (character.BoundEntity != null) return true;
            if (character.Slot == BindingSlot.None) return false;

            EntityQuery query = GetQueryForSlot(character.Slot);

            Entity? foundEntity = query.IsEmpty ? null : FindRandomEntity(character.Slot, query, storageLookup, deletedLookup);

            if (foundEntity.HasValue)
            {
                BindCharacterToEntity(character, foundEntity.Value, currentTime, currentFrame, districtLookup, ecb);
                return true;
            }
            else
            {
                HandleWaitingForBinding(character, query, currentTime);
                return false;
            }
        }

        private EntityQuery GetQueryForSlot(BindingSlot slot)
        {
            return slot switch
            {
                BindingSlot.None => default,
                BindingSlot.District => m_ResidentialQuery,         // Babcya lives in any residential
                BindingSlot.LargestIndustrial => m_IndustrialQuery,
                BindingSlot.FirstHospital => m_HospitalQuery,
                BindingSlot.CityHall => m_AdminBuildingQuery,
                BindingSlot.PoliceStation => m_PoliceQuery,
                BindingSlot.FireStation => m_FireStationQuery,
                BindingSlot.PowerPlant => m_PowerPlantQuery,
                BindingSlot.WaterTreatment => m_WaterTreatmentQuery,
                BindingSlot.HighValueResidential => m_ResidentialQuery, // Fallback to any residential
                BindingSlot.MediaBuilding => m_OfficeQuery,
                BindingSlot.Restaurant => m_CommercialQuery,
                BindingSlot.LowDensityResidential => m_ResidentialQuery,
                _ => m_ResidentialQuery
            };
        }

        private void BindCharacterToEntity(
            StoryCharacter character,
            Entity entity,
            float currentTime,
            uint currentFrame,
            ComponentLookup<CurrentDistrict> districtLookup,
            EntityCommandBuffer ecb)
        {
            character.BoundEntity = entity;
            character.State = CharacterState.Active;

            // Special handling for Valera - create SpotterData via request
            // Uses ECS Request pattern to avoid cross-domain coupling
            if (character.ID == "Valera")
            {
                int districtIndex = BuildingPowerUtils.GetBuildingDistrict(entity, districtLookup);

                var requestEntity = ecb.CreateEntity();
                ecb.AddComponent(requestEntity, new AddSpotterRequest
                {
                    BuildingEntityIndex = entity.Index,
                    BuildingEntityVersion = entity.Version,
                    DistrictIndex = districtIndex,
                    IsCharacterSpotter = true
                });
                RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(AddSpotterRequest), character.ID, currentTime, currentFrame);

                Log.Info($"[CharacterBinding] Valera bound to khrushchevka {entity} (district {districtIndex}) - SpotterData request created");
            }

            // Send bind reaction
            if (character.CanReact(currentTime))
            {
                var reactionKey = character.Reactions.GetRandom(ReactionTriggers.OnBind);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }

            Log.Info($"[CharacterBinding] {character.ID} bound to entity {entity}");
        }

        private void HandleWaitingForBinding(StoryCharacter character, EntityQuery query, float currentTime)
        {
            float timeSinceLastReaction = currentTime - character.LastReactionTime;

            // Throttle warning logs
            float realTime = UnityEngine.Time.realtimeSinceStartup;
            bool shouldLogWarn = timeSinceLastReaction > 10f;
            if (shouldLogWarn)
            {
                if (!m_LastBindingWarnTime.TryGetValue(character.ID, out float lastWarnTime) ||
                    (realTime - lastWarnTime) > BINDING_WARN_THROTTLE_SECONDS)
                {
                    int candidateCount = query.IsEmpty ? 0 : query.CalculateEntityCount();
                    m_LastBindingWarnTime[character.ID] = realTime;
                    Log.Warn($"[CharacterBinding] {character.ID} waiting for {character.Slot} - {candidateCount} candidates");
                }
            }

            // Occasional idle message (10% chance)
            if (character.CanReact(currentTime) && ThreadSafeRandom.NextFloat() < BalanceConfig.Current.Narrative.IdleMessageChance)
            {
                var reactionKey = character.Reactions.GetRandom(ReactionTriggers.IdleWaiting);
                if (reactionKey != null)
                {
                    if (CharacterMessageSender.Send(character, reactionKey))
                        character.RecordReaction(currentTime);
                }
            }
        }

        private Entity? FindRandomEntity(
            BindingSlot slot,
            EntityQuery query,
            EntityStorageInfoLookup storageLookup,
            ComponentLookup<Deleted> deletedLookup)
        {
            if (query.IsEmpty) return null;

            int entityCount = query.CalculateEntityCount();
            if (entityCount == 0) return null;

            uint version = (uint)query.GetCombinedComponentOrderVersion(includeEntityType: true);
            if (!m_CandidateCache.TryGetValue(slot, out var cache)
                || cache.Version != version
                || cache.Entities.Length != entityCount)
            {
                var newCache = new CandidateCache
                {
                    Version = version,
                    Entities = query.ToEntityArray(Allocator.Persistent)
                };
                cache.Dispose();
                cache = newCache;
                m_CandidateCache[slot] = cache;
            }

            int candidateCount = cache.Entities.Length;
            if (candidateCount == 0) return null;

            int start = Next(candidateCount);
            for (int offset = 0; offset < candidateCount; offset++)
            {
                Entity candidate = cache.Entities[(start + offset) % candidateCount];
                if (storageLookup.Exists(candidate) && !deletedLookup.HasComponent(candidate))
                    return candidate;
            }

            cache.Dispose();
            m_CandidateCache.Remove(slot);
            if (Log.IsDebugEnabled) Log.Debug($"[CharacterBinding] Invalidated stale candidate cache for {slot}");
            return null;
        }

        public void Dispose()
        {
            foreach (var cache in m_CandidateCache.Values)
                cache.Dispose();
            m_CandidateCache.Clear();
            GC.SuppressFinalize(this);
        }

        private struct CandidateCache : IDisposable
        {
            public uint Version;
            public NativeArray<Entity> Entities;

            public void Dispose()
            {
                if (Entities.IsCreated)
                    Entities.Dispose();
            }
        }
    }
}
