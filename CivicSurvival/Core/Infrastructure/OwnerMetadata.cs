using System;
using System.Collections.Concurrent;
using System.Reflection;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Infrastructure
{
    public static class OwnerMetadata
    {
        private static readonly ConcurrentDictionary<Type, string> s_Cache = new();

        public static string Of<T>() where T : class
            => Of(typeof(T));

        public static string Of(Type serviceType)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            return s_Cache.GetOrAdd(serviceType, Resolve);
        }

        public static bool TryGet(Type serviceType, out string featureId)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            var attr = serviceType.GetCustomAttribute<OwnedByFeatureIdAttribute>(inherit: false);
            if (attr == null)
            {
                featureId = null!;
                return false;
            }

            featureId = attr.FeatureId;
            return true;
        }

        public static void Clear()
        {
            s_Cache.Clear();
        }

        private static string Resolve(Type serviceType)
        {
            var attr = serviceType.GetCustomAttribute<OwnedByFeatureIdAttribute>(inherit: false);
            if (attr == null || string.IsNullOrEmpty(attr.FeatureId))
            {
                throw new InvalidOperationException(
                    $"Service interface {serviceType.FullName} is missing [OwnedByFeatureId]. " +
                    "Feature-owned null-object services must declare their producer feature on the interface.");
            }

            return attr.FeatureId;
        }
    }
}
