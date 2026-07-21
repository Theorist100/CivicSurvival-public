using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Services
{
    public static class RequestResultEmitter
    {
        public static Entity Emit(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            RequestKind kind,
            RequestStatus status,
            ReasonId reasonId,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            string metaDiscriminatorKind = meta.DiscriminatorKind.Length > 0
                ? meta.DiscriminatorKind.ToString()
                : discriminatorKind;
            string metaDiscriminatorValue = meta.DiscriminatorValue.Length > 0
                ? meta.DiscriminatorValue.ToString()
                : discriminatorValue;
            return Emit(
                ecb,
                meta.RequestId,
                kind,
                status,
                reasonId,
                createdTime,
                canonicalEcho,
                metaDiscriminatorKind,
                metaDiscriminatorValue);
        }

        public static Entity Emit(
            EntityCommandBuffer ecb,
            int requestId,
            RequestKind kind,
            RequestStatus status,
            ReasonId reasonId,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RequestResultEvent
            {
                RequestId = requestId,
                Kind = kind,
                Status = status,
                ReasonId = reasonId.ToFixedString(),
                CanonicalEcho = new FixedString64Bytes(canonicalEcho ?? ""),
                DiscriminatorKind = new FixedString32Bytes(string.IsNullOrEmpty(discriminatorKind) ? "none" : discriminatorKind),
                DiscriminatorValue = new FixedString64Bytes(discriminatorValue ?? ""),
                CreatedTime = createdTime
            });
            return entity;
        }

        public static Entity EmitSuccess(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            RequestKind kind,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            string metaDiscriminatorKind = meta.DiscriminatorKind.Length > 0
                ? meta.DiscriminatorKind.ToString()
                : discriminatorKind;
            string metaDiscriminatorValue = meta.DiscriminatorValue.Length > 0
                ? meta.DiscriminatorValue.ToString()
                : discriminatorValue;
            return Emit(
                ecb,
                meta.RequestId,
                kind,
                RequestStatus.Success,
                ReasonId.None,
                createdTime,
                canonicalEcho,
                metaDiscriminatorKind,
                metaDiscriminatorValue);
        }

        public static Entity EmitSuccess(
            EntityCommandBuffer ecb,
            int requestId,
            RequestKind kind,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            return Emit(
                ecb,
                requestId,
                kind,
                RequestStatus.Success,
                ReasonId.None,
                createdTime,
                canonicalEcho,
                discriminatorKind,
                discriminatorValue);
        }

        public static Entity EmitFixedReason(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            RequestKind kind,
            RequestStatus status,
            FixedString64Bytes reasonId,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            string metaDiscriminatorKind = meta.DiscriminatorKind.Length > 0
                ? meta.DiscriminatorKind.ToString()
                : discriminatorKind;
            string metaDiscriminatorValue = meta.DiscriminatorValue.Length > 0
                ? meta.DiscriminatorValue.ToString()
                : discriminatorValue;
            return EmitFixedReason(
                ecb,
                meta.RequestId,
                kind,
                status,
                reasonId,
                createdTime,
                canonicalEcho,
                metaDiscriminatorKind,
                metaDiscriminatorValue);
        }

        public static Entity EmitFixedReason(
            EntityCommandBuffer ecb,
            int requestId,
            RequestKind kind,
            RequestStatus status,
            FixedString64Bytes reasonId,
            double createdTime,
            string canonicalEcho = "",
            string discriminatorKind = "none",
            string discriminatorValue = "")
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RequestResultEvent
            {
                RequestId = requestId,
                Kind = kind,
                Status = status,
                ReasonId = reasonId,
                CanonicalEcho = new FixedString64Bytes(canonicalEcho ?? ""),
                DiscriminatorKind = new FixedString32Bytes(string.IsNullOrEmpty(discriminatorKind) ? "none" : discriminatorKind),
                DiscriminatorValue = new FixedString64Bytes(discriminatorValue ?? ""),
                CreatedTime = createdTime
            });
            return entity;
        }
    }
}
