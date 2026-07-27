using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Economy
{
    /// <summary>
    /// Backend owner signal for shadow trade request consumers.
    /// UI trigger handlers use this to reject immediately when the consumer is disabled.
    ///
    /// Same-feature consumers (ShadowImport/ExportUISystem in ShadowEconomy) call
    /// <c>Require&lt;T&gt;()</c>; no null-object generation needed.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.ShadowEconomyName)]
    public interface IShadowTradeConsumerReadiness
    {
        bool CanConsumeShadowTradeRequests { get; }
    }
}
