namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Narrative quest identifiers. Values are wire/serialization stable — never renumber.
    /// </summary>
    public enum QuestId : byte
    {
        None = 0,

        /// <summary>
        /// "Lights for Kotleta" — a district blackout cut his suppliers; restore power there
        /// before the deadline. Repeatable (cooldown-gated), reward: buckwheat reserve tons.
        /// </summary>
        KotletaLights = 1,

        /// <summary>
        /// "The Right Voice" — blunt N psy-raid landings with the matching speaker archetype.
        /// Offered once per city after the first multi-contact raid, reward: media trust.
        /// </summary>
        RightVoice = 2,

        /// <summary>
        /// "Nothing in the Sky" — the activists call during the cold open: the city has no air
        /// defense and the first strike is already inbound. Put guns up before the sirens.
        /// Armed once per city when the intro's activist window opens, resolved when it closes,
        /// reward: a temporary happiness bonus that expires at the second wave.
        /// </summary>
        AirDefenseReady = 3,
    }

    /// <summary>
    /// Lifecycle phase of one quest slot. Serialized as a byte — values are stable.
    /// </summary>
    public enum QuestPhase : byte
    {
        Inactive = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
    }

    /// <summary>
    /// Generic per-quest runtime state. One slot per registered <see cref="QuestId"/>;
    /// every lifecycle field quests share lives here (phase, progress, anchor, deadline,
    /// cooldown, chip-clear timer), so adding a quest adds NO new state shape — only its
    /// trigger/completion logic and texts. Serialized as a slot list (QuestStateCodec).
    /// </summary>
    public struct QuestSlot
    {
        /// <summary>Anchor value meaning "no anchor / matches anything".</summary>
        public const int NO_ANCHOR = -1;

        public QuestId Id;
        public QuestPhase Phase;
        /// <summary>Once-per-city latch: a quest with this set never re-offers after resolving.</summary>
        public bool Offered;
        /// <summary>Progress toward <see cref="Target"/> for counting quests (0 for timed ones).</summary>
        public int Progress;
        /// <summary>Completion count for counting quests (0 = not a counting quest).</summary>
        public int Target;
        /// <summary>Quest-specific anchor: district index (Kotleta), arming wave (Voice), NO_ANCHOR = any.</summary>
        public int Anchor;
        /// <summary>Absolute TotalGameHours deadline (0 = no deadline).</summary>
        public double DeadlineHour;
        /// <summary>Absolute TotalGameHours before which the quest cannot re-arm.</summary>
        public double CooldownUntilHour;
        /// <summary>Absolute TotalGameHours when a terminal phase clears off the chip.</summary>
        public double ClearAtHour;
    }
}
