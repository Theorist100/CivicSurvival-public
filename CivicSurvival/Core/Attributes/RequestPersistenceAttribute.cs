using System;

namespace CivicSurvival.Core.Attributes
{
    public enum RequestPersistenceKind
    {
        TransientInput = 0,
        RetainedInput = 1,
        ReconciledOutcome = 2,
    }

    public enum RetainedRequestTtlPolicy
    {
        None = 0,
        SimFramesAfterCreation = 1,
        SimFramesAfterMetaCreatedTime = 2,
        UntilTerminalResult = 3,
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class RequestPersistenceAttribute : Attribute
    {
        public const int DefaultTtlFrames = 600;

        public RequestPersistenceAttribute(RequestPersistenceKind kind)
        {
            Kind = kind;
        }

        public RequestPersistenceAttribute(RequestPersistenceKind kind, Type purgeOwner)
        {
            Kind = kind;
            PurgeOwner = purgeOwner;
        }

        public RequestPersistenceAttribute(RequestPersistenceKind kind, RetainedRequestTtlPolicy ttlPolicy, int ttlFrames = DefaultTtlFrames)
        {
            Kind = kind;
            TtlPolicy = ttlPolicy;
            TtlFrames = ttlFrames;
        }

        public RequestPersistenceKind Kind { get; }

        public Type? PurgeOwner { get; }

        public RetainedRequestTtlPolicy TtlPolicy { get; }

        public int TtlFrames { get; } = DefaultTtlFrames;
    }
}
