using Game.UI;
using Colossal.UI.Binding;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Manages help button "seen" state for UI highlights.
    /// Simple state holder with bindings and serialization.
    /// No logic - just stores whether user has clicked help buttons.
    /// </summary>
    [ActIndependent]
    public partial class HelpStateSystem : CivicUISystemBase, IDefaultSerializable, IResettable
    {
        private static readonly LogContext Log = new("HelpState");
        // State
        private bool m_GridHelpSeen;
        private bool m_ShadowHelpSeen;
        private bool m_NeedsBindingSync;  // FIX: Dirty flag to avoid updates every frame

        // Bindings (read by UI for highlight)
        private ProfiledBinding<bool> m_GridHelpSeenBinding = null!;
        private ProfiledBinding<bool> m_ShadowHelpSeenBinding = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Bindings for UI to read "seen" state
            m_GridHelpSeenBinding = new ProfiledBinding<bool>(Group, GridHelpSeen, false);
            m_ShadowHelpSeenBinding = new ProfiledBinding<bool>(Group, ShadowHelpSeen, false);

            AddBinding(m_GridHelpSeenBinding.Binding);
            AddBinding(m_ShadowHelpSeenBinding.Binding);

            // Triggers for UI to mark as seen.
            // Lifecycle wrap: HelpStateSystem persists help-seen flags. Without the
            // gate, a stray JS call from menu UI would mutate the last loaded save's
            // help state. GameplayOnly silently no-ops while IsGameplayReady is false.
            AddBinding(new TriggerBinding(Group, MarkGridHelpSeen,
                CivicGameLifecycle.GameplayOnly(OnMarkGridHelpSeen)));
            AddBinding(new TriggerBinding(Group, MarkShadowHelpSeen,
                CivicGameLifecycle.GameplayOnly(OnMarkShadowHelpSeen)));

            Log.Info("Created");
        }

        private void OnMarkGridHelpSeen()
        {
            m_GridHelpSeen = true;
            m_NeedsBindingSync = true;
            m_GridHelpSeenBinding.Update(true);
            Log.Debug("Grid help marked as seen");
        }

        private void OnMarkShadowHelpSeen()
        {
            m_ShadowHelpSeen = true;
            m_NeedsBindingSync = true;
            m_ShadowHelpSeenBinding.Update(true);
            Log.Debug("Shadow help marked as seen");
        }

        protected override void OnUpdateImpl()
        {
            // FIX: Only sync bindings when state changed (after load/defaults)
            if (!m_NeedsBindingSync) return;

            m_GridHelpSeenBinding.Update(m_GridHelpSeen);
            m_ShadowHelpSeenBinding.Update(m_ShadowHelpSeen);
            m_NeedsBindingSync = false;
        }
    }
}
