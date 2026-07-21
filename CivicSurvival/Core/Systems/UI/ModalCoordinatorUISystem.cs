using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Core.Systems.UI
{
    /// <summary>
    /// Publishes ModalCoordinator state for React. The coordinator owns priority;
    /// React renders only the active modal id from this snapshot.
    /// </summary>
    [ActIndependent]
    public partial class ModalCoordinatorUISystem : CivicUIPanelSystem
    {
        private int m_ModalObserverCursor = int.MinValue;

        protected override bool RequiresLoadedGame => false;

        protected override int UpdateInterval => 1;

#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(ActiveModalState, ModalCoordinator.Instance.SnapshotJson);
        }

        protected override void OnPanelUpdate()
        {
            var coordinator = ModalCoordinator.Instance;
            var observed = coordinator.SnapshotView.Observe(ref m_ModalObserverCursor);
            if (!observed.Changed)
                return;

            Bindings.Update(ActiveModalState, observed.Value.Json);
            Log.Info($"pushed snapshot v={observed.Version} json={observed.Value.Json}");
        }
#pragma warning restore CIVIC098
    }
}
