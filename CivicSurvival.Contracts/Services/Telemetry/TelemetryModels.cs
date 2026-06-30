using System;
using System.Collections.Generic;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Single telemetry event with type and arbitrary data.
    /// </summary>
    public class TelemetryEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString("D");
        public string SessionId { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public object Data { get; set; } = null!;
    }

    /// <summary>
    /// Batch payload sent to telemetry server.
    /// </summary>
    public class TelemetryPayload
    {
        public string SessionId { get; set; } = "";
        public string ModVersion { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string ContractVersion { get; set; } = TelemetryContract.CurrentVersion;
        public DateTime Timestamp { get; set; }

        // Durable player GUID — links session_id to player_id server-side so the
        // Personal Chronicle can aggregate per player. Empty for anon / opt-out
        // clients (then the field is omitted from the wire payload entirely).
        public string PlayerId { get; set; } = "";
#pragma warning disable CA2227 // Collection properties should be read only - needed for reassignment in batching
        public List<TelemetryEvent> Events { get; set; } = new();
#pragma warning restore CA2227
    }

    /// <summary>
    /// Wrapper for raw JSON string (used in deserialization to preserve unknown data).
    /// </summary>
    public sealed class RawJson
    {
        public string Json { get; }
        public RawJson(string json) => Json = json;
    }
}
