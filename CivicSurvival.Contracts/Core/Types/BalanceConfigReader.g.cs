// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/balance.contract.yaml
// SourceHash:       sha256:ec89b3f14a795ec42e45b560fa7c9689d103898fefc42d8941e2c2e35fd7b5db
// Generator:        scripts/generators/balance.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.6.3
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
