// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/balance.contract.yaml
// SourceHash:       sha256:f9d6838969f7d67e2bc44e9f392ba4bbc0b483d0e50a02dabe6cb7d766f92d41
// Generator:        scripts/generators/balance.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.17.0
// GeneratedAt:      2026-05-14T00:00:00Z

#nullable enable

using System;
using Newtonsoft.Json;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Types
{
    public static class BalanceConfigReader
    {
        public static RemoteBalanceConfig Parse(string json)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<RemoteBalanceConfig>(json)
                    ?? throw new ContractValidationException("RemoteBalanceConfig is empty or null");
                if (config.SchemaRevision != RemoteBalanceConfig.CURRENT_SCHEMA_REVISION)
                {
                    throw new ContractValidationException($"RemoteBalanceConfig schema revision {config.SchemaRevision} does not match expected {RemoteBalanceConfig.CURRENT_SCHEMA_REVISION}");
                }
                config.Validate();
                return config;
            }
            catch (ContractValidationException) { throw; }
            catch (Exception ex)
            {
                throw new ContractValidationException("RemoteBalanceConfig is not valid JSON", ex);
            }
        }
    }
}
