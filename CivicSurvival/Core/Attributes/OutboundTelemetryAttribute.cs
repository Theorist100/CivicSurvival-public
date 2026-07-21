using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a class whose <c>JsonBuilder.Object()</c> / <c>JsonBuilder.Array()</c>
    /// calls intentionally bypass the <c>ui-dto.contract.yaml</c> pipeline because
    /// the payload is sent to an external HTTP boundary (telemetry server, auth
    /// handshake, etc.), not to a UI binding. CIVIC471
    /// (<c>UiBindingMustUseContractDto</c>) reads this marker to allow-list the
    /// type so the analyzer does not require the payload to be promoted to a
    /// contract DTO.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OutboundTelemetryAttribute : Attribute
    {
    }
}
