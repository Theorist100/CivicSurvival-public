using CivicSurvival.Core.Components.Lifecycle;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Countermeasures core FSM state (ECS singleton, CrossDomain).
    /// Contains only fields needed by cross-domain readers.
    ///
    /// FSM: Idle → Suspicion → Investigation → Article → Police → Arrest
    ///
    /// Writer: CountermeasuresUpdateSystem (sole writer)
    /// Cross-domain readers: ScandalSystem (Heat), DonorConferenceSystem (CorruptionScore),
    ///   DonorConferenceUISystem, ShadowImportUISystem, ShadowTradeDailySystem,
    ///   ShadowWalletSystem (CurrentPhase.IsPoliceActive())
    /// </summary>
    public struct CountermeasuresCoreFsm : IComponentData
    {
        /// <summary>Current phase of the countermeasures FSM.</summary>
        public CountermeasuresPhase CurrentPhase;

        /// <summary>Current corruption score (0-100), with inertia applied.</summary>
        public float CorruptionScore;

        /// <summary>Target corruption (what it would be instantly, before inertia).</summary>
        public float TargetCorruption;

        /// <summary>Heat accumulates at high corruption, triggers investigations.</summary>
        public float Heat;

        /// <summary>Number of charges (for Article/Arrest phases).</summary>
        public int ChargesCount;

        /// <summary>Result message from last player choice.</summary>
        public FixedString128Bytes LastChoiceResult;

        /// <summary>Current game hour (for timing events).</summary>
        public float GameHour;

        /// <summary>Next event allowed after this hour (cooldown).</summary>
        public float NextEventHour;

        /// <summary>Frozen offshore balance seized when the arrest outcome was emitted.</summary>
        public long ArrestedAssetsSeized;

        /// <summary>Frozen wallet balance after the arrest outcome was applied.</summary>
        public long ArrestedWalletAfter;

        /// <summary>Reset to default values.</summary>
        public void SetDefaults()
        {
            CurrentPhase = CountermeasuresPhase.Idle;
            CorruptionScore = 0f;
            TargetCorruption = 0f;
            Heat = 0f;
            ChargesCount = 0;
#pragma warning disable CIVIC323 // FixedString128Bytes is blittable value type, not NativeContainer
            LastChoiceResult = default;
#pragma warning restore CIVIC323
            GameHour = 0f;
            NextEventHour = 0f;
            ArrestedAssetsSeized = 0;
            ArrestedWalletAfter = 0;
        }

        /// <summary>Create with default values.</summary>
        public static CountermeasuresCoreFsm CreateDefault()
        {
            var state = new CountermeasuresCoreFsm();
            state.SetDefaults();
            return state;
        }

        /// <summary>Ensure singleton entity exists with all 4 countermeasures components.</summary>
        public static void EnsureExists(EntityManager em)
        {
            var entity = CivicSingleton.Ensure(em, CreateDefault(), new EnsureSingletonPolicy<CountermeasuresCoreFsm>
            {
                EnsureShape = EnsureSiblingComponents
            });
            em.SetName(entity, "CountermeasuresState");
        }

        private static void EnsureSiblingComponents(EntityManager em, Entity entity)
        {
            EnsureSiblingComponent(em, entity, CmInvestigationState.CreateDefault());
            EnsureSiblingComponent(em, entity, CmPoliceState.CreateDefault());
            EnsureSiblingComponent(em, entity, CmProtestState.CreateDefault());
        }

        private static void EnsureSiblingComponent<T>(EntityManager em, Entity canonical, in T defaultValue)
            where T : unmanaged, IComponentData
        {
            T value = defaultValue;
            bool foundSibling = false;
            using var query = em.CreateEntityQuery(ComponentType.ReadWrite<T>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var source = entities[i];
                if (source == canonical || !em.Exists(source) || !em.HasComponent<T>(source))
                    continue;

                if (!foundSibling)
                {
                    value = em.GetComponentData<T>(source);
                    foundSibling = true;
                }
            }

            if (!em.HasComponent<T>(canonical))
                em.AddComponentData(canonical, value);

            for (int i = 0; i < entities.Length; i++)
            {
                var duplicate = entities[i];
                if (duplicate != canonical && em.Exists(duplicate) && em.HasComponent<T>(duplicate))
                    em.RemoveComponent<T>(duplicate);
            }
        }
    }
}
