using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Declares a UI reason-id constant that must exist in every active locale file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UiReasonIdAttribute : Attribute
    {
        public string DescriptionComment { get; }

        public UiReasonIdAttribute(string descriptionComment = "")
            => DescriptionComment = descriptionComment;
    }
}
