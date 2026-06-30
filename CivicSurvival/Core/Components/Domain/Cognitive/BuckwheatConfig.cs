using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Config-only component for Buckwheat Protocol.
    /// Single Writer: BuckwheatProcurementLevelRequestSystem (UI config changes).
    /// Split from BuckwheatSingleton to prevent ECB full-struct stomp.
    /// </summary>
    public struct BuckwheatConfig : IComponentData
    {
        /// <summary>Procurement level (0, 25, 50, 75, 100%).</summary>
        public int ProcurementLevel;

        public static BuckwheatConfig Default => new()
        {
            ProcurementLevel = 0
        };
    }
}
