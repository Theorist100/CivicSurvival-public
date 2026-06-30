using System;
using System.Collections.Generic;
using Unity.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Narrative.Data
{
    /// <summary>
    /// Binding slot - what type of building/entity the character is looking for
    /// </summary>
    public enum BindingSlot
    {
        None,                       // Not bound to any entity
        District,                   // Bound to a district
        LargestIndustrial,          // Largest industrial building
        FirstHospital,              // First hospital in city
        CityHall,                   // City hall (signature)
        PoliceStation,              // Police HQ
        FireStation,                // Fire station
        PowerPlant,                 // Any power plant
        WaterTreatment,             // Water treatment facility
        HighValueResidential,       // Expensive residential
        MediaBuilding,              // TV/Radio station
        Restaurant,                 // Restaurant (for Kotleta)
        LowDensityResidential       // Khrushchevka (for Valera)
    }

    /// <summary>
    /// Character lifecycle state
    /// </summary>
    public enum CharacterState : byte
    {
        Waiting = 0,    // Looking for binding target, posting "waiting" messages
        Active = 1,     // Bound to entity, normal operation
        Angry = 2,      // Building destroyed or prolonged blackout
        Gone = 3        // Left the city (relationship too low)
    }

    /// <summary>
    /// Story character - virtual NPC that observes ECS world
    /// </summary>
    public class StoryCharacter
    {
        public string ID { get; set; }
        public string NameKey { get; set; } = "";   // Localization key for name
        public string RoleKey { get; set; } = "";   // Localization key for role
        public CharacterArchetype Archetype { get; set; }

        // Binding
        public Entity? BoundEntity { get; set; }
        public BindingSlot Slot { get; set; }

        // Angry cooldown: game-time hour when Angry expires (serialized via NarrativeSystem)
        public float AngryUntilTime { get; set; }

        // State
        public CharacterState State { get; set; } = CharacterState.Waiting;
        // FIX #191: Clamp to documented [-100, 100] range
        private float m_Relationship;
        public float Relationship
        {
            get => m_Relationship;
            set => m_Relationship = Math.Clamp(value, -100f, 100f);
        }
        public bool IsActive { get; set; } = true;

        // Tracking
        public float LastReactionTime { get; set; } = NarrativeSystemCodec.ReadyForImmediateReactionTime;
        public float ReactionCooldown { get; set; } = 1f;  // Hours between reactions (game time)
        public int BlackoutCount { get; set; }
        // Set true when OnDistrictBlackoutStarted handles this character; cleared on BlackoutEnded.
        // Prevents CheckCharacterBlackout polling from re-chirping the same ongoing blackout.
        public bool IsInDistrictBlackout { get; set; }

        // Reactions pool
        public ReactionPool Reactions { get; set; }

        public StoryCharacter(string id)
        {
            ID = id;
            Reactions = new ReactionPool();
        }

        public bool CanReact(float currentTime)
        {
            if (!IsFinite(currentTime)) return false;
            if (!IsFinite(LastReactionTime)) return true;
            return currentTime - LastReactionTime >= ReactionCooldown;
        }

        public void MakeReadyForFirstReaction()
        {
            LastReactionTime = NarrativeSystemCodec.ReadyForImmediateReactionTime;
        }

        public void RecordReaction(float currentTime)
        {
            LastReactionTime = currentTime;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Pool of reactions for different triggers.
    /// TS-006 FIX: Thread-safe with lock (Dictionary and Random are not thread-safe).
    /// </summary>
    public class ReactionPool
    {
#pragma warning disable CIVIC152 // Bounded by enum count (CharacterReaction variants)
        private readonly Dictionary<string, List<string>> _reactions = new();
#pragma warning restore CIVIC152
        private readonly Random _random = new();
        private readonly object _lock = new();  // TS-006 FIX

        public void Add(string trigger, params string[] localizationKeys)
        {
            lock (_lock)
            {
                if (!_reactions.TryGetValue(trigger, out var pool))
                {
                    pool = new List<string>();
                    _reactions[trigger] = pool;
                }
                pool.AddRange(localizationKeys);
            }
        }

        public string? GetRandom(string trigger)
        {
            lock (_lock)
            {
                if (!_reactions.TryGetValue(trigger, out var pool) || pool.Count == 0)
                {
                    return null;
                }
                return pool[_random.Next(pool.Count)];
            }
        }

        public bool HasTrigger(string trigger)
        {
            lock (_lock)
            {
                return _reactions.TryGetValue(trigger, out var list) && list.Count > 0;
            }
        }
    }

}
