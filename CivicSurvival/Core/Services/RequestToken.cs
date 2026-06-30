using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Capability returned by RequestResultBridge.Begin. The raw id is only backing data
    /// for request metadata and ECS events; completion APIs validate the full token.
    /// </summary>
    public readonly struct RequestToken
    {
        internal readonly int RequestId;
        internal readonly RequestKind Kind;
        internal readonly string ResultKey;
        internal readonly int Generation;
        internal readonly RequestDiscriminator Discriminator;

        internal RequestToken(
            int requestId,
            RequestKind kind,
            string resultKey,
            int generation,
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            RequestId = requestId;
            Kind = kind;
            ResultKey = resultKey ?? string.Empty;
            Generation = generation;
            Discriminator = RequestDiscriminator.FromWire(discriminatorKind, discriminatorValue ?? string.Empty);
        }

        internal bool IsValid => RequestId > 0 && Generation > 0 && !string.IsNullOrEmpty(ResultKey);
    }
}
