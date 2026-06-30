using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a service interface or concrete service class as cross-cutting infrastructure —
    /// not owned by any feature, registered in <see cref="CivicSurvival.Core.Infrastructure.ServiceRegistry"/>
    /// at boot (step 3 of <c>ARCHITECTURE.md §10</c>) and consumed via plain
    /// <c>ServiceRegistry.Instance.Require&lt;T&gt;()</c> / <c>Get&lt;T&gt;()</c> without
    /// feature-ownership checks.
    ///
    /// Mutually exclusive with <see cref="OwnedByFeatureIdAttribute"/>. Carriers:
    /// <list type="bullet">
    ///   <item><description>Interface form: <c>IEventBus</c>, <c>IRenderWriteBarrier</c>,
    ///   <c>IVanillaWriteBarrier</c>, <c>IDistrictStateReader</c>/<c>Writer</c>/<c>Serialization</c>,
    ///   <c>IAutoDispatchStateWriter</c>.</description></item>
    ///   <item><description>Class form: concrete services registered without an interface
    ///   (<c>ModSettings</c>, <c>AudioManager</c>, <c>ActEpochClock</c>,
    ///   <c>ThreatGenerationClock</c>,
    ///   <c>TelemetryIdentityService</c>, <c>ToastService</c>).</description></item>
    /// </list>
    ///
    /// For world-bound vanilla refs (<c>Game.UI.NameSystem</c>,
    /// <c>Game.Rendering.CameraUpdateSystem</c>, per-World <c>EntityQuery</c> etc.)
    /// use the façade+host pattern with <c>[Facade]</c> — these refs cannot live
    /// in a process-lifetime <c>[InfrastructureService]</c>. Enforced by
    /// <c>CIVIC466</c>.
    ///
    /// <c>CIVIC463</c> (<c>EnforceServiceRegistryConsumerShape</c>) reads this marker to
    /// decide whether a <c>ServiceRegistry</c> call site is correctly classified — see
    /// <c>ARCHITECTURE.md §5</c> «Infrastructure Registries» and <c>§3 I4</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class InfrastructureServiceAttribute : Attribute
    {
    }
}
