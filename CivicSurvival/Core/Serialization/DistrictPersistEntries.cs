namespace CivicSurvival.Core.Serialization
{
    public readonly struct DistrictBoolEntry
    {
        public DistrictBoolEntry(int districtIndex, bool value)
        {
            DistrictIndex = districtIndex;
            Value = value;
        }

        public int DistrictIndex { get; }
        public bool Value { get; }
    }

    public readonly struct DistrictFloatEntry
    {
        public DistrictFloatEntry(int districtIndex, float value)
        {
            DistrictIndex = districtIndex;
            Value = value;
        }

        public int DistrictIndex { get; }
        public float Value { get; }
    }
}
