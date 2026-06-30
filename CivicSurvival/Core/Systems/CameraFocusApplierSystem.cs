using Game.Rendering;
using Game.Simulation;
using Unity.Entities;
using UnityEngine;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned applier for queued camera-focus requests from
    /// <see cref="CameraFocusState"/>. Drains the queue once per OnUpdate and
    /// writes <c>controller.pivot</c>/<c>zoom</c>.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class CameraFocusApplierSystem : SystemBase
    {
        private static readonly LogContext Log = new("CameraFocusApplierSystem");

        private CameraUpdateSystem? m_CameraSystem;
#pragma warning disable CIVIC150 // ServiceRegistry handle, not gameplay state; lazy retry handles registration order.
        private CameraFocusState? m_State;
#pragma warning restore CIVIC150
        private bool m_StateMissingWarned;

        protected override void OnCreate()
        {
            base.OnCreate();

            TryResolveCamera();
        }

        private void TryResolveCamera()
        {
            if (m_CameraSystem != null) return;
            m_CameraSystem = World.GetExistingSystemManaged<CameraUpdateSystem>();
            if (m_CameraSystem == null && Log.IsDebugEnabled)
                Log.Debug("CameraUpdateSystem not yet in world — will retry on next OnUpdate");
        }

        private bool TryResolveState()
        {
            if (m_State != null) return true;

            m_State = ServiceRegistry.TryGet<CameraFocusState>();
            if (m_State == null && !m_StateMissingWarned)
            {
                Log.Warn("CameraFocusState not in ServiceRegistry — will retry on next OnUpdate");
                m_StateMissingWarned = true;
            }
            if (m_State != null)
                m_StateMissingWarned = false;
            return m_State != null;
        }

        protected override void OnDestroy()
        {
            // Drop any pending focus request before the world dies — otherwise it
            // would apply to the next world's camera with stale coordinates.
            m_State?.Clear();
            m_CameraSystem = null;
            m_State = null;
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!TryResolveState()) return;
            var state = m_State;
            if (state == null) return;

            TryResolveCamera();  // lazy retry — handles late vanilla-system creation
            if (m_CameraSystem == null)
            {
                if (Log.IsDebugEnabled) Log.Debug("OnUpdate skipped: CameraUpdateSystem still not in world");
                return;
            }
            if (!state.TryConsume(out var position, out var zoom)) return;

            var controller = m_CameraSystem.activeCameraController;
            if (controller == null)
            {
                if (Log.IsDebugEnabled) Log.Debug("No active camera controller — focus dropped");
                return;
            }

            controller.pivot = new Vector3(position.x, position.y, position.z);
            if (zoom.HasValue) controller.zoom = zoom.Value;

            if (Log.IsDebugEnabled)
            {
                if (zoom.HasValue)
                    Log.Debug($"Focused on ({position.x:F0}, {position.y:F0}, {position.z:F0}), zoom: {zoom.Value:F1}");
                else
                    Log.Debug($"Focused on ({position.x:F0}, {position.y:F0}, {position.z:F0})");
            }
        }
    }
}
