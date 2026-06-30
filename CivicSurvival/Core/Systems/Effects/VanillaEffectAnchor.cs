using Game.Effects;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Anchor entity infrastructure for VanillaVfxSystem.
    ///
    /// Owns two runtime entities:
    /// - Anchor: holds the EnabledEffect buffer that CompleteEnabledSystem uses
    ///   to maintain m_EnabledIndex consistency on swap-back. Carries the empty
    ///   Game.Objects.Object tag so EffectControlSystem treats it as a dynamic
    ///   owner and does not take the static Transform/culling path.
    /// - DummyPrefab: PrefabRef target for the anchor. Has Prefab tag but NO
    ///   runtime Effect slot buffer. Each active injected effect owns one slot,
    ///   so vanilla EffectControlSystem validates the entry instead of marking
    ///   it WrongPrefab on the next pass.
    ///
    /// Both entities are runtime-only. VanillaVfxSystem destroys them explicitly
    /// at save/load boundaries and recreates them afterwards. They must not carry
    /// Game.Tools.Temp: ToolClearSystem routinely deletes Temp entities while
    /// the default tool clears ordinary cursor state.
    /// </summary>
    internal sealed class VanillaEffectAnchor
    {
        private Entity m_Anchor = Entity.Null;
        private Entity m_Dummy = Entity.Null;

        public Entity Entity => m_Anchor;
        public Entity DummyPrefab => m_Dummy;

        /// <summary>
        /// True when the anchor entity is alive and still usable by mod VFX writes.
        /// Entities tagged Deleted are owned by vanilla cleanup and must not receive
        /// new EnabledEffectData writes.
        /// </summary>
#pragma warning disable CIVIC051 // Existence check, not structural change
        public bool Exists(EntityManager em) =>
            m_Anchor != Entity.Null
            && em.Exists(m_Anchor)
            && !em.HasComponent<Deleted>(m_Anchor);
#pragma warning restore CIVIC051

        /// <summary>
        /// Create the anchor entity (with EnabledEffect buffer + PrefabRef)
        /// and a dummy prefab with a fixed-size Effect slot buffer.
        /// </summary>
        public void Create(EntityManager em, int effectSlots)
        {
            // CIVIC134: we're CREATING new prefab entities, not modifying existing ones.
            // CIVIC051: structural changes here run on init / post-load only, not hot path.
#pragma warning disable CIVIC051, CIVIC134
            m_Dummy = em.CreateEntity();
            em.AddComponent<Prefab>(m_Dummy);
            var dummyEffects = em.AddBuffer<Game.Prefabs.Effect>(m_Dummy);
            dummyEffects.ResizeUninitialized(effectSlots);
            for (int i = 0; i < dummyEffects.Length; i++)
                dummyEffects[i] = default;

            m_Anchor = em.CreateEntity();
            em.AddComponent<Game.Objects.Object>(m_Anchor);
            em.AddBuffer<EnabledEffect>(m_Anchor);
            em.AddComponentData(m_Anchor, new PrefabRef { m_Prefab = m_Dummy });
#pragma warning restore CIVIC051, CIVIC134
        }

        /// <summary>
        /// Destroy both entities if they still exist. Safe to call even if Create
        /// was never invoked or the entities were already cleaned up by serialization.
        /// </summary>
        public void Destroy(EntityManager em)
        {
            // CIVIC006/CIVIC208: one-shot structural change on destroy / post-load, not hot path.
            // CIVIC051: existence check + destroy on managed entities we own.
#pragma warning disable CIVIC006, CIVIC051, CIVIC208
            if (m_Anchor != Entity.Null && em.Exists(m_Anchor))
                em.DestroyEntity(m_Anchor);
            if (m_Dummy != Entity.Null && em.Exists(m_Dummy))
                em.DestroyEntity(m_Dummy);
#pragma warning restore CIVIC006, CIVIC051, CIVIC208

            m_Anchor = Entity.Null;
            m_Dummy = Entity.Null;
        }

        /// <summary>
        /// Route any still-live runtime entities through the vanilla Deleted lifecycle,
        /// then forget local handles so the host can rebuild without writing to
        /// Deleted-but-live anchors.
        /// </summary>
        public void MarkDeletedAndForget(EntityManager em)
        {
            // CIVIC006/CIVIC051: recovery/save-boundary cleanup, not hot path.
#pragma warning disable CIVIC006, CIVIC051
            if (m_Anchor != Entity.Null
                && em.Exists(m_Anchor)
                && !em.HasComponent<Deleted>(m_Anchor))
            {
                em.AddComponent<Deleted>(m_Anchor);
            }

            if (m_Dummy != Entity.Null
                && em.Exists(m_Dummy)
                && !em.HasComponent<Deleted>(m_Dummy))
            {
                em.AddComponent<Deleted>(m_Dummy);
            }
#pragma warning restore CIVIC006, CIVIC051

            m_Anchor = Entity.Null;
            m_Dummy = Entity.Null;
        }

        /// <summary>
        /// Read the anchor's EnabledEffect buffer through a caller-owned BufferLookup.
        /// Caller is responsible for calling lookup.Update(system) before this.
        /// </summary>
        public DynamicBuffer<EnabledEffect> GetBuffer(in BufferLookup<EnabledEffect> lookup)
        {
            // CIVIC035: anchor entity guaranteed to have EnabledEffect buffer (we created it)
#pragma warning disable CIVIC035
            return lookup[m_Anchor];
#pragma warning restore CIVIC035
        }
    }
}
