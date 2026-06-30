using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an ECS system as pre-registered framework infrastructure: lifecycle
    /// hosts (<c>PostLoadValidationSystem</c>, <c>SaveMetadataSystem</c>,
    /// <c>DistrictLifecycleSystem</c>, …), scheduling markers
    /// (<c>PowerCapacityWriterGroup</c>, <c>PsyPressureWriterGroup</c>,
    /// <c>*ReadyMarker</c>, <c>InterceptBarrier</c>, …), service systems
    /// (<c>GameTimeSystem</c>, <c>TelemetryService</c>,
    /// <c>BudgetResolutionSystem</c>, <c>ToastUISystem</c>, …), and shared
    /// request/cleanup infrastructure. These systems are created by
    /// <c>SystemRegistrar.RegisterCoreSystems</c> /
    /// <c>RegisterSharedInfrastructureSystems</c> before any feature module
    /// loads and are never feature-gated.
    ///
    /// CIVIC400 (<c>BanGetOrCreateSystemManagedOutsideCoreKernel</c>) treats
    /// types carrying this attribute as exempt from the gameplay-system ban on
    /// <c>World.GetOrCreateSystemManaged&lt;T&gt;()</c>; cross-feature gameplay
    /// systems must continue to go through <c>FeatureRegistry.Require/Query</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class FrameworkSystemAttribute : Attribute
    {
    }
}
