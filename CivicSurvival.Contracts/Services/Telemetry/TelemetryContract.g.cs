// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/telemetry.contract.yaml
// SourceHash:       sha256:84423530e595b058b7ed45bb78ecb1410ae7c1ab85e0a49d93a4f1731872a743
// Generator:        scripts/generators/telemetry.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.34
// GeneratedAt:      2026-05-14T00:00:00Z

namespace CivicSurvival.Services.Telemetry
{
    public static class TelemetryContract
    {
        /// <summary>Contract version stamped on every telemetry envelope. Emitted from the
        /// contract `version` field — the same source as the server's SUPPORTED_CONTRACT_VERSION,
        /// so the version a client claims and the version the server speaks cannot drift.</summary>
        public const string CurrentVersion = "1.34";
        public const string RecoveryFormatVersion = "telemetry-recovery-v1";
        public const int MaxEventsPerBatch = 1000;
    }
}
