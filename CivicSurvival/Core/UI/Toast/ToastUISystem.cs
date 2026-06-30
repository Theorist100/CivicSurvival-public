using System;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.UI.Toast
{
    /// <summary>
    /// UI system for toast notifications.
    /// Pure UI bindings - delegates all logic to ToastService.
    ///
    /// Migrated from ToastUIPanel → CivicUIPanelSystem.
    /// Gains: proper ECS lifecycle. ToastService lifecycle managed in OnCreate/OnDestroy.
    /// </summary>
    // CA1001: m_Service disposed in OnDestroy (ECS lifecycle, not IDisposable)
#pragma warning disable CA1001
    [ActIndependent]
    public partial class ToastUISystem : CivicUIPanelSystem
#pragma warning restore CA1001
    {
        private ToastService m_Service = null!;

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Create and register ToastService (previously done in MainUISystem.CreatePanels)
            m_Service = new ToastService();
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<ToastService>(m_Service);

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            // FIX #151: GetterValueBinding callbacks are called by CS2's UISystemBase directly,
            // bypassing try-catch wrapper. Unhandled exceptions kill the panel.
            Bindings.AddGetter<string>(ToastsJson, () =>
            {
                try { return m_Service.GetToastsJson(); }
                catch (Exception ex) { Log.Error($"GetToastsJson failed: {ex}"); return "[]"; }
            });
            Bindings.AddGetter<int>(ToastCount, () =>
            {
                try { return m_Service.ActiveToastCount; }
                catch (Exception ex) { Log.Error($"toastCount failed: {ex}"); return 0; }
            });
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(AcceptToast, FeatureIds.Toast, OnAcceptToast);
            Triggers.Add<int>(RejectToast, FeatureIds.Toast, OnRejectToast);
            Triggers.Add<int>(DismissToast, FeatureIds.Toast, OnDismissToast);
        }

        protected override void OnPanelUpdate()
        {
            m_Service.Update();
        }

        private void OnAcceptToast(int toastId)
        {
            m_Service.AcceptToast(toastId);
        }

        private void OnRejectToast(int toastId)
        {
            m_Service.RejectToast(toastId);
        }

        private void OnDismissToast(int toastId)
        {
            m_Service.DismissToast(toastId);
        }

        protected override void OnDestroy()
        {
            // Dispose ToastService (panel owns lifecycle)
            m_Service?.Dispose();
            if (ServiceRegistry.IsInitialized && m_Service != null)
                ServiceRegistry.Instance.Unregister<ToastService>(m_Service);

            base.OnDestroy();
        }
    }
}
