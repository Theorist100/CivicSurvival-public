using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// AA credit state — RUNTIME read-model only. Heritage credits, donor credits,
    /// defense-policy projection.
    ///
    /// Persistence model — "system owns persistence, singleton is runtime read-model"
    /// (same as ScenarioSingleton / CurrentActSingleton, canon C1):
    /// This is a PLAIN <see cref="IComponentData"/> — NOT ISerializable, NOT
    /// IEmptySerializable. The engine therefore never serializes/round-trips this
    /// entity, so a same-session load cannot produce a "surviving session entity +
    /// engine-restored entity" duplicate. The durable credit truth lives in
    /// AirDefenseStateSystem's system serializer (.Serialization); this entity is a
    /// runtime projection recreated by EnsureExists and hydrated by OnLoadRestore.
    /// No collapse / dedup logic is required (there is structurally only ever one).
    ///
    /// Sole writer: AirDefenseStateSystem (synchronous main-thread RW, no async job).
    /// CurrentPolicy is a written PROJECTION (policy A): the persisted policy owner is
    /// AirDefensePolicySystem ([Persist]); this field exists only so snapshot
    /// readers see a value. Cross-domain policy reads go through IDefensePolicyReader.
    /// Credit readers (AirDefenseUISystem, DonorConferenceSystem) read via the
    /// synchronously-refreshed snapshot (AirDefenseStateSystem.GetCreditsSnapshot).
    /// </summary>
    public struct AirDefenseCreditsSingleton : IComponentData
    {
        public const int MaxDonorPatriotCredits = 4;

        /// <summary>Current defense policy (targeting priority).</summary>
        public DefensePolicy CurrentPolicy;

        /// <summary>Heritage AA credits remaining (free placements from city reserves).</summary>
        public int HeritageCredits;

        /// <summary>Initial heritage credits granted at game start.</summary>
        public int HeritageCreditsMax;

        /// <summary>Donor Patriot placement credits remaining (free from donor conference).</summary>
        public int DonorPatriotCredits;

        /// <summary>Total donor Patriot credits ever granted.</summary>
        public int DonorPatriotCreditsMax;

        /// <summary>
        /// Global player setting: do Patriot SAMs engage drones (Shahed)? Default false —
        /// the Patriot is an anti-ballistic SAM reserved for ballistics, and only engages
        /// drones when the player explicitly opts in (a single toggle for all Patriots).
        /// Ballistic interception (BallisticDefenseSystem) is unaffected by this flag —
        /// Patriot always intercepts ballistics. Persisted (survives save/load).
        /// </summary>
        public bool PatriotInterceptsDrones;

        /// <summary>
        /// Per-save AA rule: should idle AA installations auto-buy ammo from the city budget
        /// during calm (the graduated trickle refill)? Default TRUE — the trickle runs unless the
        /// player opts out, in which case AA is refilled only via the manual emergency resupply.
        /// A per-city rule alongside <see cref="PatriotInterceptsDrones"/>, not a global setting.
        /// Persisted (survives save/load).
        /// </summary>
        public bool AutoResupplyEnabled;

        /// <summary>
        /// Game-hour (<see cref="GameTimeSystem"/> TotalGameHours) of the last emergency resupply,
        /// per AA type. Used to gate the per-type resupply cooldown (only the dear types — Patriot —
        /// carry a non-zero cooldown). NO_RESUPPLY = never resupplied this session/save → not on
        /// cooldown. Persisted so a saved Patriot mid-cooldown stays gated after load. Game-hour,
        /// not ElapsedTime (which resets on load).
        /// </summary>
        public const float NoResupplyHour = -1f;

        public float LastResupplyHourHeritage;
        public float LastResupplyHourBofors;
        public float LastResupplyHourGepard;
        public float LastResupplyHourPatriot;

        /// <summary>
        /// Sentinel for <see cref="LastResupplyWavePatriot"/> meaning "never resupplied this
        /// session/save" → the wave cooldown does not gate. Distinct from a real wave number,
        /// which is always >= 0 (0 = no wave active, >= 1 = a real wave).
        /// </summary>
        public const int NoResupplyWave = -1;

        /// <summary>
        /// <see cref="CivicSurvival.Core.Components.Threats.WaveStateSingleton.WaveNumber"/> of the last Patriot emergency resupply.
        /// Patriot is gated to one resupply per wave (Economy.PatriotResupplyCooldownWaves) instead
        /// of a wall-clock cooldown: one full magazine per installation is enough to clear a whole
        /// drone wave in a large city, so a second top-up inside the same wave would trivialize it.
        /// Persisted so a saved mid-wave gate survives load. Only Patriot carries a wave cooldown;
        /// the gun types refill freely (their wave-cooldown config is 0).
        /// </summary>
        public int LastResupplyWavePatriot;

        public readonly float GetLastResupplyHour(AAType type) => type switch
        {
            AAType.HeritageBofors => LastResupplyHourHeritage,
            AAType.Bofors40mm => LastResupplyHourBofors,
            AAType.Gepard => LastResupplyHourGepard,
            AAType.PatriotSAM => LastResupplyHourPatriot,
            _ => NoResupplyHour
        };

        public void SetLastResupplyHour(AAType type, float gameHour)
        {
            switch (type)
            {
                case AAType.HeritageBofors: LastResupplyHourHeritage = gameHour; break;
                case AAType.Bofors40mm: LastResupplyHourBofors = gameHour; break;
                case AAType.Gepard: LastResupplyHourGepard = gameHour; break;
                case AAType.PatriotSAM: LastResupplyHourPatriot = gameHour; break;
                default: throw new System.ArgumentOutOfRangeException(nameof(type), type, "Unhandled AAType");
            }
        }

        public void SetDefaults() => this = Default;

        public static AirDefenseCreditsSingleton Default => new()
        {
            CurrentPolicy = DefensePolicy.HumanitarianShield,
            PatriotInterceptsDrones = false,
            AutoResupplyEnabled = true,
            LastResupplyHourHeritage = NoResupplyHour,
            LastResupplyHourBofors = NoResupplyHour,
            LastResupplyHourGepard = NoResupplyHour,
            LastResupplyHourPatriot = NoResupplyHour,
            LastResupplyWavePatriot = NoResupplyWave
        };

        /// <summary>
        /// Ensure the single runtime projection entity exists. Plain IComponentData =
        /// the engine never round-trips it, so there is structurally only ever one —
        /// no collapse/dedup needed (mirrors ScenarioStateMachine.EnsureSingletonEntity).
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

    }
}
