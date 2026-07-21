#if DEBUG
using System;
using System.Linq;
using Colossal.Logging;
using CivicSurvival.Core.Utils;
using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.Requests
{
    /// <summary>
    /// Debug-only system that logs pending request counts each frame.
    /// Helps identify stuck or accumulating requests during development.
    ///
    /// Only compiled in DEBUG builds.
    /// </summary>
    [ActIndependent]
    public partial class RequestDebugSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("RequestDebug");

        private float m_ThrottleTimer;
        private const float LOG_INTERVAL_SECONDS = 5.0f;

        private EntityQuery[] m_RequestQueries = null!;
        private string[] m_RequestTypeNames = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            var requestTypes = typeof(ICommandRequest).Assembly
                .GetTypes()
                .Where(t => t.IsValueType && !t.IsAbstract
                    && (typeof(ICommandRequest).IsAssignableFrom(t) || HasRequestMetaProducerShape(t)))
                .OrderBy(t => t.FullName)
                .ToArray();

            m_RequestQueries = new EntityQuery[requestTypes.Length];
            m_RequestTypeNames = new string[requestTypes.Length];

            var readOnlyMethod = typeof(ComponentType)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .First(m => m.Name == "ReadOnly" && m.IsGenericMethod && m.GetParameters().Length == 0);

            for (int i = 0; i < requestTypes.Length; i++)
            {
                var genericMethod = readOnlyMethod.MakeGenericMethod(requestTypes[i]);
                var componentType = (ComponentType)genericMethod.Invoke(null, null);
                m_RequestQueries[i] = GetEntityQuery(componentType);
                m_RequestTypeNames[i] = requestTypes[i].Name;
            }

            Log.Info($"[RequestDebugSystem] Created (DEBUG only, {m_RequestQueries.Length} request types)");
        }

        private static bool HasRequestMetaProducerShape(Type type) =>
            type.Namespace == "CivicSurvival.Core.Components.Requests"
            && type.Name.EndsWith("Request", StringComparison.Ordinal)
            && type != typeof(RequestMeta)
            && type != typeof(RequestResultEvent);

        [CompletesDependency("RequestDebugSystem.OnUpdateImpl: DEBUG-only throttled diagnostic (LOG_INTERVAL_SECONDS); CalculateEntityCount drives request-pile log line for developers")]
        protected override void OnUpdateImpl()
        {
            m_ThrottleTimer += SystemAPI.Time.DeltaTime;

            // Only log every N seconds to avoid spam
            if (m_ThrottleTimer < LOG_INTERVAL_SECONDS)
                return;

            m_ThrottleTimer = 0f;

            int total = 0;

            for (int i = 0; i < m_RequestQueries.Length; i++)
            {
                int count = m_RequestQueries[i].CalculateEntityCount();

                if (count <= 0)
                    continue;

                total += count;
                if (Log.IsDebugEnabled) Log.Debug($"[RequestDebug] {m_RequestTypeNames[i]} pending: {count}");
            }

            if (total > 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[RequestDebug] Total pending requests: {total}");
            }
        }

    }
}
#endif
