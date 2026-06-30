namespace CivicSurvival.Core.Types
{
    using System;

    public readonly struct FeatureId : IEquatable<FeatureId>
    {
        private readonly string _v;

        private FeatureId(string value)
        {
            _v = value ?? string.Empty;
        }

        internal static FeatureId Of(string id) => new FeatureId(id);

        public bool IsEmpty => string.IsNullOrEmpty(_v);

        public override string ToString() => _v ?? string.Empty;

        public bool Equals(FeatureId other) => string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FeatureId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

        public static bool operator ==(FeatureId left, FeatureId right) => left.Equals(right);

        public static bool operator !=(FeatureId left, FeatureId right) => !left.Equals(right);

        public static implicit operator string(FeatureId featureId) => featureId.ToString();
    }
}
