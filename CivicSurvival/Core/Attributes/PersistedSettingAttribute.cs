using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a ModSettings property as part of save/load persistence.
    /// The key is the stable on-disk field name used by KeyedSerializer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PersistedSettingAttribute : Attribute
    {
        public PersistedSettingAttribute(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public double Min { get; set; } = double.NaN;
        public double Max { get; set; } = double.NaN;
        public double Default { get; set; } = double.NaN;
        public bool Unclamped { get; set; }
    }
}
