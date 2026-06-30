using System;
using System.Collections.Generic;
using System.Linq;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Components.Requests
{
    public readonly struct RequestClassification
    {
        public RequestClassification(
            Type requestType,
            RequestPersistenceKind kind,
            Type? purgeOwner,
            RetainedRequestTtlPolicy ttlPolicy,
            int ttlFrames)
        {
            RequestType = requestType;
            Kind = kind;
            PurgeOwner = purgeOwner;
            TtlPolicy = ttlPolicy;
            TtlFrames = ttlFrames;
        }

        public Type RequestType { get; }

        public RequestPersistenceKind Kind { get; }

        public Type? PurgeOwner { get; }

        public RetainedRequestTtlPolicy TtlPolicy { get; }

        public int TtlFrames { get; }
    }

    public static class RequestClassificationManifest
    {
        private static readonly Lazy<IReadOnlyList<RequestClassification>> s_All =
            new(BuildManifest);

        public static IReadOnlyList<RequestClassification> All => s_All.Value;

        public static IReadOnlyList<Type> GetRetainedInputTypes() =>
            All.Where(c => c.Kind == RequestPersistenceKind.RetainedInput)
                .Select(c => c.RequestType)
                .ToArray();

        public static IReadOnlyList<Type> GetTransientInputTypes() =>
            All.Where(c => c.Kind == RequestPersistenceKind.TransientInput)
                .Select(c => c.RequestType)
                .ToArray();

        private static IReadOnlyList<RequestClassification> BuildManifest()
        {
            return typeof(ICommandRequest).Assembly
                .GetTypes()
                .Where(t => t.IsValueType && !t.IsAbstract)
                .Select(t => (Type: t, Attribute: (RequestPersistenceAttribute?)Attribute.GetCustomAttribute(t, typeof(RequestPersistenceAttribute))))
                .Where(x => x.Attribute != null)
                .Select(x => new RequestClassification(
                    x.Type,
                    x.Attribute!.Kind,
                    x.Attribute.PurgeOwner,
                    x.Attribute.TtlPolicy,
                    x.Attribute.TtlFrames))
                .OrderBy(c => c.RequestType.FullName, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static class RetainedRequestCleanupPolicy
    {
        public static bool ShouldExpire(
            RetainedRequestTtlPolicy policy,
            uint currentFrame,
            uint firstSeenFrame,
            uint metaCreatedFrame,
            int ttlFrames,
            bool hasMeta,
            bool hasTerminalResult,
            out uint ageFrames)
        {
            ttlFrames = Math.Max(0, ttlFrames);
            ageFrames = 0u;

            switch (policy)
            {
                case RetainedRequestTtlPolicy.SimFramesAfterCreation:
                    ageFrames = ComputeNonWrappingAge(currentFrame, firstSeenFrame);
                    return ageFrames > ttlFrames;

                case RetainedRequestTtlPolicy.SimFramesAfterMetaCreatedTime:
                    if (!hasMeta)
                        return false;
                    uint createdFrame = ResolveMetaCreatedFrame(metaCreatedFrame, firstSeenFrame);
                    if (createdFrame > currentFrame)
                        createdFrame = firstSeenFrame;
                    ageFrames = ComputeNonWrappingAge(currentFrame, createdFrame);
                    return ageFrames > ttlFrames;

                case RetainedRequestTtlPolicy.UntilTerminalResult:
                    return hasTerminalResult;

                default:
                    return false;
            }
        }

        public static uint ResolveMetaCreatedFrame(uint metaCreatedFrame, uint fallbackFrame) =>
            metaCreatedFrame != 0u ? metaCreatedFrame : fallbackFrame;

        private static uint ComputeNonWrappingAge(uint currentFrame, uint anchorFrame) =>
            unchecked(currentFrame - anchorFrame);
    }
}
