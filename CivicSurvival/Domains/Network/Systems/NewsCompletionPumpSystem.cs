using CivicSurvival.Core.Attributes;
using Game;

namespace CivicSurvival.Domains.Network.Systems
{
    /// <summary>
    /// Drains GlobalNews and PersonalChronicle async completions during UIUpdate so
    /// paused gameplay still delivers terminal UI state. Both producers poll in
    /// GameSimulation (frozen while paused); this UIUpdate pump is what makes their
    /// delivery pause-safe (AXIOM 14).
    /// </summary>
    [ActIndependent]
    public partial class NewsCompletionPumpSystem : GameSystemBase
    {
        private GlobalNewsSystem? m_GlobalNews;
        private PersonalChronicleSystem? m_PersonalChronicle;

        protected override void OnUpdate()
        {
            m_GlobalNews ??= World.GetExistingSystemManaged<GlobalNewsSystem>();
            m_GlobalNews?.PumpAsyncCompletions();

            m_PersonalChronicle ??= World.GetExistingSystemManaged<PersonalChronicleSystem>();
            m_PersonalChronicle?.PumpAsyncCompletions();
        }
    }
}
