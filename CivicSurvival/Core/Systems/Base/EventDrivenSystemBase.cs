using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.Base
{
    [ActIndependent]
    public abstract partial class EventDrivenSystemBase : CivicSystemBase
    {
        protected sealed override void OnUpdateImpl()
        {
            OnEventDrivenUpdate();
        }

        protected virtual void OnEventDrivenUpdate()
        {
        }

        protected override bool RequiresLoadedGame => true;
    }
}
