using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Narrative.Data;

namespace CivicSurvival.Domains.Narrative.Systems
{
    /// <summary>
    /// NarrativeSystem - Save/Load serialization (IDefaultSerializable).
    /// Persists character state (relationships, blackout counts, etc.)
    /// across game saves.
    ///
    /// BoundEntity is persisted through the entity-table remap path so blackout state
    /// and character binding remain one deterministic post-load contract.
    /// </summary>
    public partial class NarrativeSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            ResetCharacterState();
            m_LastDecayDay = -1;
            m_UpdateTimer = 0f;
            m_PendingBlackoutStarts.Clear();
            m_PendingBlackoutEnds.Clear();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                int count = m_Characters != null ? m_Characters.Count : 0;
                var characters = new NarrativeCharacterPersistState[count];
                if (m_Characters != null)
                {
                    int index = 0;
                    foreach (var kvp in m_Characters)
                    {
                        var character = kvp.Value;
                        characters[index++] = new NarrativeCharacterPersistState(
                            character.ID,
                            (byte)character.State,
                            character.Relationship,
                            character.IsActive,
                            character.LastReactionTime,
                            character.BlackoutCount,
                            character.AngryUntilTime,
                            character.IsInDistrictBlackout,
                            character.BoundEntity.HasValue,
                            character.BoundEntity ?? Unity.Entities.Entity.Null);
                    }
                }

                var state = new NarrativePersistState(characters, m_LastDecayDay);
                NarrativeSystemCodec.Write(state, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(NarrativeSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(NarrativeSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                NarrativeSystemCodec.Read(reader, 1000, MAX_BLACKOUT_COUNT, out var state);
                m_UpdateTimer = 0f;
                m_LastDecayDay = state.LastDecayDay;
                // Drop pending blackout indices from a previous city. CS2 reuses the system
                // instance on in-game load and calls only Deserialize on the happy path
                // (never ResetState/OnDestroy), so stale district indices would otherwise be
                // drained on the first OnUpdate of the loaded city and credited to the wrong character.
                m_PendingBlackoutStarts.Clear();
                m_PendingBlackoutEnds.Clear();
                int appliedCount = ApplyCharacterStates(state.Characters);

                int totalCharacters = m_Characters != null ? m_Characters.Count : 0;
                Log.Info($"[NarrativeSystem] Deserialized v{version}: {appliedCount}/{totalCharacters} characters restored");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private int ApplyCharacterStates(IReadOnlyList<NarrativeCharacterPersistState> characters)
        {
            int applied = 0;
            var seen = new HashSet<string>();
            for (int i = 0; i < characters.Count; i++)
            {
                var saved = characters[i];

                if (m_Characters != null && m_Characters.TryGetValue(saved.Id, out var character))
                {
                    seen.Add(saved.Id);
                    character.State = saved.State switch
                    {
                        1 => CharacterState.Active,
                        2 => CharacterState.Angry,
                        3 => CharacterState.Gone,
                        _ => CharacterState.Waiting,
                    };
                    character.Relationship = saved.Relationship;
                    character.IsActive = saved.IsActive;
                    character.LastReactionTime = saved.LastReactionTime;
                    character.BlackoutCount = saved.BlackoutCount;
                    character.BoundEntity = saved.HasBoundEntity ? saved.BoundEntity : null;
                    character.AngryUntilTime = saved.AngryUntilTime;
                    character.IsInDistrictBlackout = saved.HasBoundEntity && saved.IsInDistrictBlackout;
                    applied++;
                }
                else
                {
                    Log.Warn($"[NarrativeSystem] Unknown character ID '{saved.Id}' in save — skipped (mod version mismatch?)");
                }
            }
            if (m_Characters != null)
            {
                foreach (var character in m_Characters.Values)
                {
                    if (!seen.Contains(character.ID))
                        ResetCharacterFields(character);
                }
            }
            return applied;
        }

        private static readonly (string Id, float Relationship)[] s_DefaultRelationships = CreateDefaultRelationships();

        private static (string Id, float Relationship)[] CreateDefaultRelationships()
        {
            var defaults = StoryCharacterRegistry.CreateCharacters();
            var result = new (string Id, float Relationship)[defaults.Count];
            int index = 0;
            foreach (var kvp in defaults)
                result[index++] = (kvp.Key, kvp.Value.Relationship);
            return result;
        }

        private void ResetCharacterState()
        {
            if (m_Characters == null) return;

            foreach (var character in m_Characters.Values)
                ResetCharacterFields(character);

            // FIX NAR-P2-004: Reset decay day tracker
            m_LastDecayDay = -1;

            m_UpdateTimer = 0f;

            Log.Info("[NarrativeSystem] Character state reset");
        }

        private static void ResetCharacterFields(StoryCharacter character)
        {
            character.State = CharacterState.Waiting;
            character.Relationship = GetDefaultRelationship(character.ID);
            character.IsActive = true;
            character.MakeReadyForFirstReaction();
            character.BlackoutCount = 0;
            character.BoundEntity = null;
            character.AngryUntilTime = 0f;
            character.IsInDistrictBlackout = false;
        }

        private static float GetDefaultRelationship(string characterId)
        {
            for (int i = 0; i < s_DefaultRelationships.Length; i++)
            {
                if (s_DefaultRelationships[i].Id == characterId)
                    return s_DefaultRelationships[i].Relationship;
            }
            return 0f;
        }
    }
}
