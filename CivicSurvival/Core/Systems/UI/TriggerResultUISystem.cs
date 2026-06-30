using Colossal.UI.Binding;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.UI
{
    /// <summary>
    /// Reads RequestResultBridge reject-toast state and pushes it to UI via ValueBinding.
    /// Runs in UIUpdate group.
    ///
    /// TS side: useTriggerResult() hook reads "triggerResult" binding as JSON,
    /// RejectToast component shows auto-dismiss toast.
    /// </summary>
    [ActIndependent]
    public partial class TriggerResultUISystem : CivicUISystemBase
    {
        private static readonly LogContext Log = new("TriggerResultUI");

        private ValueBinding<string> m_Binding = null!;
        private string m_LastPushed = "";

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Binding = new ValueBinding<string>(B.Group, B.TriggerResult, "");
            AddBinding(m_Binding);

            Log.Info("Created — triggerResult binding registered");
        }

        protected override void OnUpdateImpl()
        {
            RequestResultBridge.TickRejectToastTtl(UnityEngine.Time.realtimeSinceStartupAsDouble, 3.0);
            string current = RequestResultBridge.GetRejectToastJson();

            // Only push to UI when value actually changes (avoid redundant binding updates)
            if (current == m_LastPushed) return;

            m_Binding.Update(current);
            m_LastPushed = current;

            if (current.Length > 0)
            {
                Log.Info($"Rejection: {current}");
            }
        }
    }
}
