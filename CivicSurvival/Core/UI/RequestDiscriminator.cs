using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.UI
{
    public enum RequestDiscriminatorKind : byte
    {
        None = 0,
        DistrictIndex,
        OfferKey,
        Field,
        OperationSlot
    }

    public readonly struct RequestDiscriminator : IEquatable<RequestDiscriminator>
    {
        public static readonly RequestDiscriminator None = new(RequestDiscriminatorKind.None);
        private const int HashPrime = 397;

        private static readonly Dictionary<string, RequestDiscriminatorKind> s_wireKinds =
            new Dictionary<string, RequestDiscriminatorKind>(StringComparer.Ordinal)
            {
                { "districtIndex", RequestDiscriminatorKind.DistrictIndex },
                { "offerKey", RequestDiscriminatorKind.OfferKey },
                { "field", RequestDiscriminatorKind.Field },
                { "operationSlot", RequestDiscriminatorKind.OperationSlot }
            };

        private readonly string _value;

        private RequestDiscriminator(RequestDiscriminatorKind kind, int districtIndex = 0, string value = "")
        {
            Kind = kind;
            DistrictIndex = districtIndex;
            _value = value ?? string.Empty;
        }

        public RequestDiscriminatorKind Kind { get; }

        public int DistrictIndex { get; }

        public string OfferKey => Kind == RequestDiscriminatorKind.OfferKey ? _value : string.Empty;

        public string Field => Kind == RequestDiscriminatorKind.Field ? _value : string.Empty;

        public string OperationSlot => Kind == RequestDiscriminatorKind.OperationSlot ? _value : string.Empty;

        public string KindWire
        {
            get
            {
                switch (Kind)
                {
                    case RequestDiscriminatorKind.None:
                        return "none";
                    case RequestDiscriminatorKind.DistrictIndex:
                        return "districtIndex";
                    case RequestDiscriminatorKind.OfferKey:
                        return "offerKey";
                    case RequestDiscriminatorKind.Field:
                        return "field";
                    case RequestDiscriminatorKind.OperationSlot:
                        return "operationSlot";
                    default:
                        throw new InvalidOperationException($"Unknown request discriminator kind '{Kind}'.");
                }
            }
        }

        public string ValueWire
        {
            get
            {
                switch (Kind)
                {
                    case RequestDiscriminatorKind.None:
                        return string.Empty;
                    case RequestDiscriminatorKind.DistrictIndex:
                        return DistrictIndex.ToString();
                    case RequestDiscriminatorKind.OfferKey:
                        return OfferKey;
                    case RequestDiscriminatorKind.Field:
                        return Field;
                    case RequestDiscriminatorKind.OperationSlot:
                        return OperationSlot;
                    default:
                        throw new InvalidOperationException($"Unknown request discriminator kind '{Kind}'.");
                }
            }
        }

        public static RequestDiscriminator ForDistrictIndex(int districtIndex) =>
            new(RequestDiscriminatorKind.DistrictIndex, districtIndex);

        public static RequestDiscriminator ForOfferKey(string offerKey) =>
            string.IsNullOrEmpty(offerKey)
                ? None
                : new RequestDiscriminator(RequestDiscriminatorKind.OfferKey, value: offerKey);

        public static RequestDiscriminator ForField(string field) =>
            string.IsNullOrEmpty(field)
                ? None
                : new RequestDiscriminator(RequestDiscriminatorKind.Field, value: field);

        public static RequestDiscriminator ForOperationSlot(string operationSlot) =>
            string.IsNullOrEmpty(operationSlot)
                ? None
                : new RequestDiscriminator(RequestDiscriminatorKind.OperationSlot, value: operationSlot);

        public static RequestDiscriminator FromWire(string kind, string value)
        {
            if (!s_wireKinds.TryGetValue(kind ?? string.Empty, out var typedKind))
                return None;

            switch (typedKind)
            {
                case RequestDiscriminatorKind.DistrictIndex:
                    return int.TryParse(value, out int districtIndex)
                        ? ForDistrictIndex(districtIndex)
                        : None;
                case RequestDiscriminatorKind.OfferKey:
                    return ForOfferKey(value);
                case RequestDiscriminatorKind.Field:
                    return ForField(value);
                case RequestDiscriminatorKind.OperationSlot:
                    return ForOperationSlot(value);
                default:
                    return None;
            }
        }

        public bool Equals(RequestDiscriminator other) =>
            Kind == other.Kind
            && DistrictIndex == other.DistrictIndex
            && ValueWire == other.ValueWire;

        public override bool Equals(object obj) =>
            obj is RequestDiscriminator other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * HashPrime)
                    ^ DistrictIndex
                    ^ StringComparer.Ordinal.GetHashCode(ValueWire);
            }
        }

        public static bool operator ==(RequestDiscriminator left, RequestDiscriminator right) =>
            left.Equals(right);

        public static bool operator !=(RequestDiscriminator left, RequestDiscriminator right) =>
            !left.Equals(right);
    }
}
