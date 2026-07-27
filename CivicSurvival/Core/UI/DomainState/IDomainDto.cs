using System.Text;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Marker for DTOs serialized through the UI bridge readiness helper.
    /// ui-dto codegen owns WriteTo; handwritten DTO files only declare runtime
    /// fields and optional WriteEligibility hooks for [DtoEligibility] fields.
    /// </summary>
    public interface IDomainDto
    {
        void WriteTo(StringBuilder sb);
    }
}
