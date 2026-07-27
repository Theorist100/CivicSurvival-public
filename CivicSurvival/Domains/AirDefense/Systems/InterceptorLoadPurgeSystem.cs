using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Purges restored interceptor missiles on the first ModificationEnd of a loaded session.
    ///
    /// The missile is a transient visual, but vanilla's serializer persists EVERY entity with a
    /// PrefabRef, so a save taken mid-flight round-trips the render shell. On load it comes back
    /// broken: <see cref="Interceptor"/> (no serializer) is stripped, but the vanilla render buffers
    /// (<c>MeshColor</c>/<c>MeshBatch</c>/<c>CullingInfo</c>, all IEmptySerializable) return length-0,
    /// and a length-0 MeshColor crashes vanilla BatchDataSystem (OOB read by m_MeshIndex in the render
    /// Burst job) on the first PreCulling pass of frame 0.
    ///
    /// Two reasons this runs HERE (ModificationEnd one-shot) instead of <c>IPostLoadValidation</c>:
    /// <list type="number">
    /// <item>ModificationEnd of frame 0 is ordered BEFORE that frame's PreCulling, so the shell is
    /// destroyed before the renderer can OOB-read it. PostLoadValidation runs in GameSimulation on
    /// frame +2 — too late (the render crash already happened).</item>
    /// <item>The query keys on <see cref="InterceptorTag"/> (IEmptySerializable → survives load), NOT
    /// <see cref="Interceptor"/> (stripped on load) — so it actually matches the restored shell.</item>
    /// </list>
    ///
    /// DestroyEntity is synchronous (EntityManager, not ECB): an ECB would defer to end-of-frame,
    /// after PreCulling already read the empty MeshColor — exactly the crash being prevented.
    ///
    /// One-shot: armed on a gameplay load, fires on the first ModificationEnd tick, then disables
    /// itself. Never touches live in-flight missiles during normal gameplay.
    /// </summary>
    [ActIndependent]
    [ReentrantOneShot("Runs once per load: armed in OnGameLoaded, fires on the first ModificationEnd tick, disables itself in OnUpdateImpl. Re-arms on the next load via OnGameLoaded.")]
    public partial class InterceptorLoadPurgeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("InterceptorLoadPurge");

        private EntityQuery m_RestoredQuery;

        [System.NonSerialized] private bool m_PurgePending;

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Key on InterceptorTag (survives load) — Interceptor is stripped on load.
            m_RestoredQuery = GetEntityQuery(ComponentType.ReadOnly<InterceptorTag>());
            Enabled = false;
            Log.Info("Created (disabled until game load)");
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            // Arm on every load regardless of purpose: the InterceptorTag query is empty outside a
            // gameplay session (editor/menu/asset loads carry no missiles), so the purge is a no-op
            // there — keeping the render-crash protection unconditional rather than gated on purpose.
            // A fresh pending flag (not an OnStartRunning reset) gates the one-shot, so each load
            // re-arms cleanly; [ReentrantOneShot] tells CIVIC365 this Enabled toggle is by-design.
            m_PurgePending = true;
            Enabled = true;
        }

        [CompletesDependency("One-shot post-load purge of restored interceptor shells; the bulk DestroyEntity runs once per load, before the first PreCulling, not per frame")]
        protected override void OnUpdateImpl()
        {
            if (!m_PurgePending)
            {
                Enabled = false;
                return;
            }

            m_PurgePending = false;
            Enabled = false;

            if (m_RestoredQuery.IsEmptyIgnoreFilter)
                return;

            // Bulk synchronous destroy (query overload — no ToEntityArray, so no sync-point array):
            // must land before the first PreCulling reads the restored shells' empty MeshColor. The
            // synchronous-DestroyEntity warning (CIVIC208) is exempt for [ReentrantOneShot] — this is a
            // controlled once-per-load structural pass, not the per-frame destroy churn the rule targets.
            int count = m_RestoredQuery.CalculateEntityCount();
            EntityManager.DestroyEntity(m_RestoredQuery);
            Log.Info($"Purged {count} restored interceptor shell(s) before first PreCulling");
        }
    }
}
