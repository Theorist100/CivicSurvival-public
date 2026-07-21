using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;

namespace CivicSurvival.Core.Features.CrossDomain.DamageAccounting
{
    // A7 taxonomy declaration for DamageAccountingSystem's post-load explosion-charge rebuild.
    [ReconciledOutcome("ExplosionCharge", typeof(EquipmentWear))]
    public partial class DamageAccountingSystem
    {
    }
}
