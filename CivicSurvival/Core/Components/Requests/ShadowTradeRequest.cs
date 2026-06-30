using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Type of shadow trade operation.
    /// </summary>
    public enum ShadowTradeType : byte
    {
        /// <summary>Set import MW amount.</summary>
        SetImportMW = 0,

        /// <summary>Set export percentage.</summary>
        SetExportPercent
    }

    /// <summary>
    /// Request to modify shadow import/export settings.
    /// Ephemeral entity pattern - created by UI, processed by ShadowTradeSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct ShadowTradeRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        public const int NoPresetPercent = -1;

        /// <summary>Type of trade operation.</summary>
        public ShadowTradeType TradeType;

        /// <summary>Value to set (MW for import, percent for export).</summary>
        public int Value;

        /// <summary>For SetImportMW: max MW visible to the UI when the request was created.</summary>
        public int ExpectedMaxMW;

        /// <summary>For SetImportMW: effective daily cost visible to the UI, including sanctions markup.</summary>
        public long ExpectedDailyCost;

        /// <summary>Non-zero when ExpectedMaxMW/ExpectedDailyCost are authoritative.</summary>
        public byte HasPriceLock;

        /// <summary>For preset import requests: selected preset percent, or NoPresetPercent for direct MW.</summary>
        public int PresetPercent;
    }
}
