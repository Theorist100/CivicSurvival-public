using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for throttled UI systems that own a <see cref="TriggerRegistry"/>.
    /// The registry is created in OnCreate, populated by <see cref="ConfigureTriggers"/>,
    /// auto-registered as bindings, and disposed in OnDestroy.
    ///
    /// Subclasses implement <see cref="ConfigureTriggers"/> only — no manual
    /// AddBinding loop, no manual Dispose. Non-trigger bindings and queries
    /// can still be set up in an OnCreate override (after base.OnCreate()).
    /// </summary>
    // CA1001: ECS systems are owned by World; trigger registry is disposed in OnDestroy.
#pragma warning disable CA1001
    public abstract partial class TriggeredThrottledUISystemBase : ThrottledUISystemBase
#pragma warning restore CA1001
    {
        private readonly TriggerRegistry m_Triggers = new();

        protected TriggerRegistry Triggers => m_Triggers;

        protected override void OnCreate()
        {
            base.OnCreate();

            ConfigureTriggers(m_Triggers);

            foreach (var binding in m_Triggers.All)
                AddBinding(binding);
        }

        protected abstract void ConfigureTriggers(TriggerRegistry triggers);

        protected override void OnDestroy()
        {
            m_Triggers.Dispose();
            base.OnDestroy();
        }
    }
}
