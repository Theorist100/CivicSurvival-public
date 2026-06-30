using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Last observable result for UI commands that can succeed or fail.
    /// </summary>
    public readonly struct RequestResult
    {
        private const int JsonInitialCapacity = 160;

        public readonly int RequestId;
        public readonly RequestStatus Status;
        public readonly string ReasonId;
        public readonly string CanonicalEcho;
        public readonly RequestDiscriminator Discriminator;

        public RequestResult(
            int requestId,
            RequestStatus status,
            string reasonId = "",
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            RequestId = requestId;
            Status = status;
            ReasonId = reasonId ?? "";
            CanonicalEcho = canonicalEcho ?? "";
            Discriminator = RequestDiscriminator.FromWire(discriminatorKind, discriminatorValue ?? "");
        }

        public string DiscriminatorKind => Discriminator.KindWire;

        public string DiscriminatorValue => Discriminator.ValueWire;

        public string ToJson()
        {
            // RequestResult JSON is often embedded while a DTO writer is active;
            // keep this builder independent from the shared DTO builder.
            var sb = new System.Text.StringBuilder(JsonInitialCapacity);
            var w = new DomainJsonHelper.JsonWriter(sb);
            w.Int("RequestId", RequestId);
            w.Str("Status", StatusToString(Status));
            w.Str("ReasonId", ReasonId);
            w.Str("CanonicalEcho", CanonicalEcho);
            w.Str("DiscriminatorKind", DiscriminatorKind);
            w.Str("DiscriminatorValue", DiscriminatorValue);
            w.End();
            return sb.ToString();
        }

        private static string StatusToString(RequestStatus status) => status switch
        {
            RequestStatus.Idle => "idle",
            RequestStatus.Pending => "pending",
            RequestStatus.Success => "success",
            RequestStatus.Failed => "failed",
            _ => "idle"
        };
    }

    internal static class RequestResultBridge
    {
        private static readonly LogContext Log = new("RequestResultBridge");
        private const int MaxTerminalResultsByRequestId = 2048;

        public const string EmergencyResupply = "EmergencyResupplyRequest";
        public const string PlantRepair = "PlantRepairRequest";
        public const string CivilianRepair = "CivilianRepairRequest";
        public const string CallToArms = "CallToArmsRequest";
        public const string ConscriptionToggle = "ConscriptionToggleRequest";
        public const string DonorSelection = "DonorSelectionRequest";
        public const string CountermeasureChoice = "LastChoiceRequestResult";
        public const string Modernization = "ModernizationRequest";
        public const string SpotterAction = "SpotterActionRequest";
        public const string AirDefensePlacement = "AirDefensePlacementRequest";
        public const string BackupPolicy = "BackupPolicyRequest";
        public const string DefensePolicy = "DefensePolicyRequest";
        public const string PatriotDroneToggle = "PatriotDroneToggleRequest";
        public const string DistrictToggle = "DistrictToggleRequest";
        public const string DistrictInternetToggle = "DistrictInternetToggleRequest";
        public const string CitySchedule = "CitySchedulePeriodRequest";
        public const string InternetMode = "InternetModeRequest";
        public const string ProcurementLevel = "ProcurementLevelRequest";
        public const string CorruptionScheme = "CorruptionSchemeRequest";
        public const string MaintenanceContract = "MaintenanceContractRequest";
        public const string ShadowTradeImport = "ShadowTradeImportRequest";
        public const string ShadowTradeExport = "ShadowTradeExportRequest";
        public const string TelemarathonMode = "TelemarathonModeRequest";
        public const string TelemarathonActive = "TelemarathonActiveRequest";
        public const string AutoDispatchToggle = "AutoDispatchToggleRequest";
        public const string Nickname = "NicknameRequest";
        public const string ArenaRefresh = "ArenaLastRefreshResult";
        public const string DistributeAid = "LastDistributeResult";
        public const string HeroAction = "HeroActionRequest";
        public const string InsiderPurchase = "InsiderRequest";
        public const string IntelUpgrade = "IntelUpgradeRequest";
        public const string GridOperation = "OperationRequest";
        public const string DonorDialog = "DonorDialogRequest";
        public const string Locale = "LocaleRequest";
        public const string OneMoreYear = "OneMoreYearRequest";
        public const string EndlessMode = "EndlessModeRequest";
        // Key == request name (KindForKey maps by name) so ValidateKnownKey resolves the kind.
        public const string CrisisSweep = "CrisisSweepRequest";

        private static readonly Dictionary<string, RequestResult> s_Results = new Dictionary<string, RequestResult>();
        private static readonly Dictionary<TokenSlot, RequestToken> s_Tokens = new Dictionary<TokenSlot, RequestToken>();
        private static readonly Dictionary<TokenSlot, TerminalResultEntry> s_TerminalResultsByRequestId = new Dictionary<TokenSlot, TerminalResultEntry>();
        private static readonly List<TokenSlot> s_TerminalRequestOrder = new List<TokenSlot>();
        private static string s_RejectToastJson = "";
        private static double s_RejectToastPublishedAt;
        [CivicSurvival.Core.Attributes.RequestTokenGeneration("Static request-token generation for UI command correlation; no snapshot payload is published.")]
        private static int s_Generation;

        internal static RequestToken Begin(
            string key,
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            int requestId = RequestRegistrar.NextRequestId();
            int generation = ++s_Generation;
            if (generation <= 0)
                generation = s_Generation = 1;

            var token = new RequestToken(
                requestId,
                KindForKey(key),
                key,
                generation,
                discriminatorKind,
                discriminatorValue);
            ValidateKnownKey(token);
            EvictSupersededTerminalResults(token);
            s_Tokens[new TokenSlot(requestId)] = token;
            Publish(key, new RequestResult(
                requestId,
                RequestStatus.Pending,
                discriminatorKind: token.Discriminator.KindWire,
                discriminatorValue: token.Discriminator.ValueWire));
            return token;
        }

        internal static bool Complete(RequestToken token, RequestStatus status, FixedString64Bytes reason)
        {
            string reasonId = reason.ToString();
            ValidateToken(token);
            var result = new RequestResult(
                token.RequestId,
                status,
                reasonId,
                discriminatorKind: token.Discriminator.KindWire,
                discriminatorValue: token.Discriminator.ValueWire);
            if (status != RequestStatus.Pending)
                StoreTerminalResult(token);

            if (!Publish(token.ResultKey, result))
            {
                RetireTokenIfTerminal(token, status);
                return false;
            }

            RetireTokenIfTerminal(token, status);
            if (status == RequestStatus.Failed)
                PublishRejectToast(token.RequestId, token.Kind, reasonId);
            return true;
        }

        internal static bool Complete(RequestToken token, RequestStatus status, string reasonId = "", string canonicalEcho = "")
        {
            ValidateToken(token);
            var result = new RequestResult(
                token.RequestId,
                status,
                reasonId,
                canonicalEcho,
                token.Discriminator.KindWire,
                token.Discriminator.ValueWire);
            if (status != RequestStatus.Pending)
                StoreTerminalResult(token);

            if (!Publish(token.ResultKey, result))
            {
                RetireTokenIfTerminal(token, status);
                return false;
            }

            RetireTokenIfTerminal(token, status);
            if (status == RequestStatus.Failed)
                PublishRejectToast(token.RequestId, token.Kind, reasonId);
            return true;
        }

        internal static bool Complete(in RequestResultEvent resultEvent, out bool published)
        {
            string key = KeyForKind(resultEvent.Kind);
            if (key.Length == 0)
            {
                published = false;
                return false;
            }

            var result = new RequestResult(
                resultEvent.RequestId,
                resultEvent.Status,
                resultEvent.ReasonId.ToString(),
                resultEvent.CanonicalEcho.ToString(),
                resultEvent.DiscriminatorKind.ToString(),
                resultEvent.DiscriminatorValue.ToString());

            var slot = new TokenSlot(resultEvent.RequestId);
            if (resultEvent.Status != RequestStatus.Pending
                && s_Tokens.TryGetValue(slot, out var token)
                && token.ResultKey == key)
            {
                StoreTerminalResult(token);
                published = Publish(key, result);
                s_Tokens.Remove(slot);
                return true;
            }

            published = Publish(key, result);
            return true;
        }

        internal static void PublishRejectToast(in RequestResultEvent resultEvent)
        {
            PublishRejectToast(resultEvent.RequestId, resultEvent.Kind, resultEvent.ReasonId.ToString());
        }

        internal static void PublishRejectToast(int requestId, RequestKind kind, string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId))
                return;

            var sb = DomainJsonHelper.GetBuilder();
            var w = new DomainJsonHelper.JsonWriter(sb);
            w.Int("RequestId", requestId);
            w.Str("ReasonId", reasonId);
            w.Str("Kind", kind.ToString());
            w.End();
            s_RejectToastJson = sb.ToString();
            s_RejectToastPublishedAt = Time.realtimeSinceStartupAsDouble;
        }

        internal static bool FailNow(RequestToken token, string reasonId)
        {
            return Complete(token, RequestStatus.Failed, reasonId);
        }

        internal static void RejectNew(string key, string reasonId)
        {
            var token = Begin(key);
            FailNow(token, reasonId);
        }

        internal static void RejectToastOnly(string key, string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId))
                return;

            var current = Get(key);
            int requestId = current.RequestId > 0
                ? current.RequestId
                : RequestRegistrar.NextRequestId();

            PublishRejectToast(requestId, KindForKey(key), reasonId);
        }

        internal static bool FailCurrent(string key, FixedString64Bytes reason)
        {
            var prior = Get(key);
            if (prior.Status != RequestStatus.Pending)
                return false;

            if (!s_Tokens.TryGetValue(new TokenSlot(prior.RequestId), out var token))
                throw new System.InvalidOperationException($"Pending request '{key}' has no live RequestToken state.");

            ValidateToken(token);
            return Complete(token, RequestStatus.Failed, reason);
        }

        internal static bool PublishTerminalForBegun(
            string key,
            int requestId,
            RequestStatus status,
            string reasonId = "",
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            if (status == RequestStatus.Pending || string.IsNullOrEmpty(key) || requestId <= 0)
                return false;

            var slot = new TokenSlot(requestId);
            if (!s_Tokens.TryGetValue(slot, out var token) || token.ResultKey != key)
                return false;

            var result = new RequestResult(
                requestId,
                status,
                reasonId,
                canonicalEcho,
                discriminatorKind,
                discriminatorValue);
            StoreTerminalResult(token);
            if (!s_Results.TryGetValue(key, out var current) || current.RequestId <= requestId)
                s_Results[key] = result;

            s_Tokens.Remove(slot);
            if (status == RequestStatus.Failed)
                PublishRejectToast(requestId, token.Kind, reasonId);
            return true;
        }

        public static RequestResult Get(string key)
        {
            return s_Results.TryGetValue(key, out var result) ? result : default;
        }

        public static string GetRejectToastJson() => s_RejectToastJson;

        public static void TickRejectToastTtl(double now, double ttl = 3.0)
        {
            if (s_RejectToastJson.Length == 0)
                return;

            if (now - s_RejectToastPublishedAt <= ttl)
                return;

            s_RejectToastJson = "";
            s_RejectToastPublishedAt = 0d;
        }

        internal static void Reset()
        {
            s_Results.Clear();
            s_Tokens.Clear();
            s_TerminalResultsByRequestId.Clear();
            s_TerminalRequestOrder.Clear();
            s_RejectToastJson = "";
            s_RejectToastPublishedAt = 0d;
            s_Generation = 0;
        }

        private static string KeyForKind(RequestKind kind) => RequestLifecycleContract.KeyForKind(kind);

        private static RequestKind KindForKey(string key) => RequestLifecycleContract.KindForKey(key);

        private static void ValidateKnownKey(RequestToken token)
        {
            if (token.Kind == RequestKind.Unknown || string.IsNullOrEmpty(token.ResultKey))
                throw new System.InvalidOperationException($"Unknown request result key '{token.ResultKey}'.");
        }

        private static void ValidateToken(RequestToken token)
        {
            if (!token.IsValid)
                throw new System.InvalidOperationException("Invalid RequestToken.");

            ValidateKnownKey(token);
            if (KindForKey(token.ResultKey) != token.Kind)
                throw new System.InvalidOperationException($"RequestToken kind mismatch for '{token.ResultKey}'.");

            if (!s_Tokens.TryGetValue(new TokenSlot(token.RequestId), out var current)
                || current.Generation != token.Generation
                || current.Kind != token.Kind
                || current.ResultKey != token.ResultKey)
            {
                throw new System.InvalidOperationException($"Stale or mismatched RequestToken for '{token.ResultKey}'.");
            }
        }

        private static void RetireTokenIfTerminal(RequestToken token, RequestStatus status)
        {
            if (status != RequestStatus.Pending)
                s_Tokens.Remove(new TokenSlot(token.RequestId));
        }

        private static void StoreTerminalResult(RequestToken token)
        {
            var slot = new TokenSlot(token.RequestId);
            bool isNew = !s_TerminalResultsByRequestId.ContainsKey(slot);
            s_TerminalResultsByRequestId[slot] = new TerminalResultEntry(
                token.ResultKey,
                token.Discriminator);
            if (isNew)
                s_TerminalRequestOrder.Add(slot);

            EnforceSecondaryTerminalCap();
        }

        private static void EvictSupersededTerminalResults(RequestToken token)
        {
            for (int i = s_TerminalRequestOrder.Count - 1; i >= 0; i--)
            {
                var slot = s_TerminalRequestOrder[i];
                if (!s_TerminalResultsByRequestId.TryGetValue(slot, out var entry))
                {
                    s_TerminalRequestOrder.RemoveAt(i);
                    continue;
                }

                if (slot.RequestId >= token.RequestId || !entry.IsSameVisibleTarget(token))
                    continue;

                s_TerminalResultsByRequestId.Remove(slot);
                s_TerminalRequestOrder.RemoveAt(i);
            }
        }

        private static void EnforceSecondaryTerminalCap()
        {
            while (s_TerminalResultsByRequestId.Count > MaxTerminalResultsByRequestId
                && s_TerminalRequestOrder.Count > 0)
            {
                var evicted = s_TerminalRequestOrder[0];
                s_TerminalRequestOrder.RemoveAt(0);
                if (s_TerminalResultsByRequestId.Remove(evicted))
                    Log.Warn($"Secondary terminal result cap overflow ({MaxTerminalResultsByRequestId}); evicted requestId {evicted.RequestId}. This indicates a retention invariant pressure bug.");
            }
        }

        private static bool Publish(string key, RequestResult result)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (s_Results.TryGetValue(key, out var current)
                && current.RequestId > result.RequestId)
                return false;

            s_Results[key] = result;
            return true;
        }

        private readonly struct TokenSlot : System.IEquatable<TokenSlot>
        {
            private readonly int _requestId;
            public int RequestId => _requestId;

            public TokenSlot(int requestId)
            {
                _requestId = requestId;
            }

            public bool Equals(TokenSlot other) => _requestId == other._requestId;

            public override bool Equals(object obj) => obj is TokenSlot other && Equals(other);

            public override int GetHashCode() => _requestId;
        }

        private readonly struct TerminalResultEntry
        {
            private readonly string _resultKey;
            private readonly RequestDiscriminator _discriminator;

            public TerminalResultEntry(
                string resultKey,
                RequestDiscriminator discriminator)
            {
                _resultKey = resultKey ?? string.Empty;
                _discriminator = discriminator;
            }

            public bool IsSameVisibleTarget(RequestToken token)
            {
                return _resultKey == token.ResultKey
                    && _discriminator.Equals(token.Discriminator);
            }
        }
    }
}
