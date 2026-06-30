using System;
namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Feature-aware service lookup. Companion to <see cref="ServiceRegistry.TryGet{TInterface}"/>
    /// that distinguishes legitimate absence (owner feature unavailable) from a
    /// registration bug (owner feature available but service missing).
    ///
    /// Behaviour:
    /// <list type="bullet">
    /// <item>Service registered → returns real service</item>
    /// <item>Owner feature closed / dep-skipped / failed → returns <paramref name="nullObject"/></item>
    /// <item>Owner feature available but service missing → throws <see cref="InvalidOperationException"/></item>
    /// <item>Boot (registration not yet complete) → throws — wire in OnStartRunning, not OnCreate</item>
    /// </list>
    ///
    /// Pair with <c>[GenerateNullObject]</c> on the interface; consumer passes
    /// <c>Null{Name}.Instance</c> as <paramref name="nullObject"/>.
    /// </summary>
    public static class ServiceRegistryFeatureExtensions
    {
        /// <summary>
        /// Get registered service for <typeparamref name="T"/>, or return
        /// <paramref name="nullObject"/> when the owning feature is unavailable.
        /// Throws when the owning feature is available but the service is not
        /// registered — surfaces producer init failures instead of silent no-ops.
        /// Also throws if called before <see cref="FeatureRegistry.IsRegistrationComplete"/>
        /// because boot-time "feature unavailable" is indistinguishable from a
        /// registration race that will resolve a tick later. Wire in OnStartRunning,
        /// not OnCreate.
        /// </summary>
        public static T TryGetOrNullObject<T>(T nullObject)
            where T : class
        {
            var service = ServiceRegistry.TryGet<T>();
            if (service != null) return service;

            var ownerFeatureId = OwnerMetadata.Of<T>();

            if (!FeatureRegistry.IsInitialized
                || !FeatureRegistry.Instance.IsRegistrationComplete)
            {
                throw new InvalidOperationException(
                    $"ServiceRegistryFeatureExtensions.TryGetOrNullObject<{typeof(T).Name}>(" +
                    $"'{ownerFeatureId}') called before FeatureRegistry registration completed. " +
                    "Wire in OnStartRunning, not OnCreate — otherwise the null-object is cached " +
                    "permanently even if the real service becomes available.");
            }

            if (!FeatureRegistry.Instance.IsKnownFeatureId(ownerFeatureId))
            {
                throw new InvalidOperationException(
                    $"ServiceRegistryFeatureExtensions.TryGetOrNullObject<{typeof(T).Name}>(" +
                    $"'{ownerFeatureId}'): no feature module registered with that id. " +
                    "Probably a typo or a reference to a deleted feature — fix the [OwnedByFeatureId] attribute.");
            }

            if (FeatureRegistry.Instance.IsAvailable(ownerFeatureId))
            {
                throw new InvalidOperationException(
                    $"Service {typeof(T).Name} not registered although owning feature " +
                    $"'{ownerFeatureId}' is available. Producer registration order or init failed.");
            }

            return nullObject;
        }
    }
}
