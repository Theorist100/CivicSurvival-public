using Unity.Entities;
using Unity.Collections;
using CivicSurvival.Core.Components.Requests;

namespace CivicSurvival.Core.Services
{
    public static class RequestMetaWriter
    {
        public static void AddInternal(
            EntityCommandBuffer ecb,
            Entity entity,
            string discriminatorKind,
            string discriminatorValue = "",
            double createdTime = double.PositiveInfinity,
            uint createdFrame = 0u)
        {
            ecb.AddComponent(entity, CreateInternal(discriminatorKind, discriminatorValue, createdTime, createdFrame));
        }

        public static void AddInternal(
            EntityManager entityManager,
            Entity entity,
            string discriminatorKind,
            string discriminatorValue = "",
            double createdTime = double.PositiveInfinity,
            uint createdFrame = 0u)
        {
            entityManager.AddComponentData(entity, CreateInternal(discriminatorKind, discriminatorValue, createdTime, createdFrame));
        }

        public static void Add(EntityCommandBuffer ecb, Entity entity, RequestToken token, double createdTime, uint createdFrame = 0u)
        {
            if (!token.IsValid)
                throw new System.InvalidOperationException("RequestMetaWriter.Add received an invalid RequestToken.");

            ecb.AddComponent(entity, Create(token, createdTime, createdFrame));
        }

        public static void Add(EntityManager entityManager, Entity entity, RequestToken token, double createdTime, uint createdFrame = 0u)
        {
            if (!token.IsValid)
                throw new System.InvalidOperationException("RequestMetaWriter.Add received an invalid RequestToken.");

            entityManager.AddComponentData(entity, Create(token, createdTime, createdFrame));
        }

        private static RequestMeta Create(RequestToken token, double createdTime, uint createdFrame)
            => new RequestMeta
            {
                RequestId = token.RequestId,
                CreatedTime = createdTime,
                CreatedFrame = createdFrame,
                DiscriminatorKind = new FixedString32Bytes(token.Discriminator.KindWire),
                DiscriminatorValue = new FixedString64Bytes(token.Discriminator.ValueWire)
            };

        private static RequestMeta CreateInternal(string discriminatorKind, string discriminatorValue, double createdTime, uint createdFrame)
            => new RequestMeta
            {
                RequestId = RequestRegistrar.NextRequestId(),
                CreatedTime = createdTime,
                CreatedFrame = createdFrame,
                DiscriminatorKind = new FixedString32Bytes(string.IsNullOrEmpty(discriminatorKind) ? "internal" : discriminatorKind),
                DiscriminatorValue = new FixedString64Bytes(discriminatorValue ?? string.Empty)
            };
    }
}
