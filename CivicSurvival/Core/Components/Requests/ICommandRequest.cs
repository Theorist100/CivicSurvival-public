using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>Shared log for request component deserialization failures.</summary>
    internal static class RequestDeserializeLog
    {
        public static readonly LogContext Log = new("RequestDeserialize");
    }

    /// <summary>
    /// Marker interface for internal command request entities.
    ///
    /// Request persistence is not inferred from this marker. Every request input
    /// must also declare <c>[RequestPersistence(...)]</c>:
    /// transient inputs are purged after load by their declared owner, while
    /// retained inputs are eligible for sim-frame TTL cleanup.
    ///
    /// To add a new command request type:
    /// 1. Define struct: <c>public struct MyRequest : IComponentData, ICommandRequest { ... }</c>
    /// 2. Add the matching <c>[RequestPersistence]</c> category declaration.
    ///
    /// NOTE: This is a pure marker interface (not extending IComponentData) to avoid
    /// S1939 "redundant interface" warnings. Unity ECS source generators require explicit
    /// IComponentData on each struct — so both must be listed in the inheritance.
    /// </summary>
#pragma warning disable CA1040 // Empty marker interface — intentional for auto-discovery
    public interface ICommandRequest { }
#pragma warning restore CA1040
}
