using Unity.Collections;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Managed reason-id token. ECS/job/event boundaries convert to FixedString64Bytes
    /// only at convergence points.
    /// </summary>
    public readonly struct ReasonId
    {
        private readonly string _v;

        private ReasonId(string value)
        {
            _v = value ?? string.Empty;
        }

        public static readonly ReasonId None = new ReasonId(string.Empty);

        internal static ReasonId Of(string id) => new ReasonId(id);

        internal static ReasonId FromRuntime(string id) => new ReasonId(id);

        public bool IsDefault => _v == null;

        public bool IsEmpty => string.IsNullOrEmpty(_v);

        public FixedString64Bytes ToFixedString() => new FixedString64Bytes(ToString());

        public override string ToString() => _v ?? string.Empty;

        public static implicit operator string(ReasonId reasonId) => reasonId.ToString();
    }
}
