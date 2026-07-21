using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct ModalSnapshot : IEquatable<ModalSnapshot>
    {
        private readonly string? m_Json;

        public ModalSnapshot(string json)
        {
            m_Json = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        }

        public string Json => m_Json ?? "{}";

        public static ModalSnapshot Empty { get; } = new("{}");

        public bool Equals(ModalSnapshot other)
            => string.Equals(Json, other.Json, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is ModalSnapshot other && Equals(other);

        public override int GetHashCode()
            => StringComparer.Ordinal.GetHashCode(Json);

        public static bool operator ==(ModalSnapshot left, ModalSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ModalSnapshot left, ModalSnapshot right)
            => !left.Equals(right);
    }
}
