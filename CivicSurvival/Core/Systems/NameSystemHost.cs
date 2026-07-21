using System;
using Game.Simulation;
using Game.UI;
using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned ECS host for <see cref="NameSystemFacade"/>. Owns the per-World
    /// vanilla <c>Game.UI.NameSystem</c> reference and writes itself into the
    /// façade on OnCreate, clears on OnDestroy.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class NameSystemHost : SystemBase
    {
        private static readonly LogContext Log = new("NameSystemHost");

        private NameSystem? m_NameSystem;
        [System.NonSerialized] private NameSystemFacade? m_Facade;

        protected override void OnCreate()
        {
            base.OnCreate();

            TryResolveVanilla();

            if (ServiceRegistry.IsInitialized)
            {
                m_Facade = ServiceRegistry.Instance.Get<NameSystemFacade>();
                if (m_Facade != null)
                    m_Facade.CurrentHost = this;
                else
                    Log.Warn("NameSystemFacade not registered in ServiceRegistry — façade lookups will return empty");
            }
        }

        private void TryResolveVanilla()
        {
            if (m_NameSystem != null) return;
            m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
            if (m_NameSystem == null && Log.IsDebugEnabled)
                Log.Debug("NameSystem not yet in world — will retry on next lookup");
        }

        protected override void OnDestroy()
        {
            // Race-guard against out-of-order host teardown across worlds.
            if (m_Facade != null && ReferenceEquals(m_Facade.CurrentHost, this))
                m_Facade.CurrentHost = null;
            m_Facade = null;
            m_NameSystem = null;
            base.OnDestroy();
        }

        protected override void OnUpdate() { /* host carries no per-frame work */ }

        internal string GetRenderedLabelName(Entity entity)
        {
            TryResolveVanilla();  // lazy retry — handles late vanilla-system creation
            if (m_NameSystem == null)
            {
                if (Log.IsDebugEnabled) Log.Debug("GetRenderedLabelName: NameSystem still not in world");
                return string.Empty;
            }
            try
            {
                return m_NameSystem.GetRenderedLabelName(entity) ?? string.Empty;
            }
            catch (ArgumentException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Name lookup skipped for stale entity {entity.Index}:{entity.Version}: {ex.GetType().Name}");
                return string.Empty;
            }
            catch (InvalidOperationException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Name lookup skipped for destroyed entity {entity.Index}:{entity.Version}: {ex.GetType().Name}");
                return string.Empty;
            }
        }
    }
}
