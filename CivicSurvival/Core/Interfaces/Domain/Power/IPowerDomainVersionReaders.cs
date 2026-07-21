using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Power
{
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IImportCapVersionReader
    {
        [NullReturnNull]
        IVersionedView<int>? ImportCapView { get; }
    }

    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.PowerGridName)]
    public interface IServedLoadVersionReader
    {
        [NullReturnNull]
        IVersionedView<ServedLoadSnapshot>? ServedLoadView { get; }
    }

    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.BlackoutName)]
    public interface IBlackoutStateVersionReader
    {
        [NullReturnNull]
        IVersionedView<int>? BlackoutStateView { get; }
    }

    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.PowerGridName)]
    public interface IAutoDispatchVersionReader
    {
        [NullReturnNull]
        IVersionedView<AutoDispatchOwnershipSnapshot>? AutoDispatchOwnershipView { get; }
    }

    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface ICollapseOwnerVersionReader
    {
        [NullReturnNull]
        IVersionedView<CollapseOwnerSnapshot>? CollapseOwnerView { get; }
    }
}
