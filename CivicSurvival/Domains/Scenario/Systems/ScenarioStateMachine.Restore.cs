using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// C1 (W2 G1 — verified): ScenarioStateMachine owns ScenarioSingleton +
    /// CurrentActSingleton but created the host entity only in OnCreate, which CS2
    /// does NOT re-run on a same-session save→load. Without an ICivicSingletonOwner
    /// restore path the entity stays destroyed post-load, every
    /// WriteSingletonFromState early-returns on the stale handle, and 30+ cross-domain
    /// readers — including RequireForUpdate&lt;CurrentActSingleton&gt; consumers
    /// (SpotterCommandIngressSystem / IntelPurchaseSystem / SaveMetadataSystem) — lose
    /// the act mid-war (silent PreWar / permanently gated systems).
    ///
    /// PostLoadValidationSystem.RestoreSingletonOwners() invokes OnLoadRestore before
    /// the validators run, so the singletons are live again by the time
    /// ValidateAfterLoad / RunValidation execute. Mirrors WorldShockSystem.Restore.cs.
    /// The host entity is runtime-only (state is persisted via ScenarioStateMachine
    /// codec, not the entity) so a plain field-!Exists recreate is correct here — no
    /// deserialized duplicate to reuse.
    /// </summary>
    public partial class ScenarioStateMachine
    {
        private Entity EnsureSingletonEntity(EntityManager em)
        {
            var entity = EnsureSingleton(ref m_Singleton, em, ScenarioSingleton.Default, EnsureScenarioSingletonShape);
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            return entity;
        }

        private static void EnsureScenarioSingletonShape(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<CurrentActSingleton>(entity))
                em.AddComponentData(entity, CurrentActSingleton.Default);
            em.SetName(entity, "ScenarioSingleton");
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            EnsureSingletonEntity(entityManager);
            WriteSingletonFromState();
            // C-5: ActEpochClock keeps its historical restore boundary here. Threat
            // generation advances synchronously in Deserialize/ResetState instead, so
            // stale threat transients are invalid before any consumer can tick and we
            // do not double-advance on the later restore pass.
            EnsureEpochClock();
            m_actEpochClock?.AdvanceForLoad();
        }
    }
}
