// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/balance.contract.yaml
// SourceHash:       sha256:faebbb0857685cd78043a1cf08879707de01073cce5ffb0fb1a59b71579f37c7
// Generator:        scripts/generators/balance.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.23.0
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
