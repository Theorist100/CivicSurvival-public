using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Game.Common;
using Game.Objects;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Damage calculation result for a single building.
    /// </summary>
    public struct DamageResult
    {
        public Entity Building;
        public DamageType Type;
        public float3 Position;
        public float Severity;
    }

    /// <summary>
    /// Type of damage to apply to a building.
    /// </summary>
    public enum DamageType : byte
    {
        None,
        Fire,
        Destroy
    }

    /// <summary>
    /// Area damage calculation utilities.
    /// Separates damage calculation from damage application.
    /// </summary>
    public static class DamageHelper
    {
        /// <summary>
        /// Calculate area damage for buildings within radius.
        /// Returns list of buildings with their damage type based on severity thresholds.
        /// </summary>
        /// <param name="center">Impact center position</param>
        /// <param name="radius">Damage radius</param>
        /// <param name="baseSeverity">Base severity at center (1.0 = full)</param>
        /// <param name="buildings">List of buildings in radius</param>
        /// <param name="transformLookup">Transform lookup for distance calculation</param>
        /// <param name="excludeTarget">Entity to exclude (e.g., primary target)</param>
        /// <param name="destructionThreshold">Severity above which building is destroyed</param>
        /// <param name="fireThreshold">Severity above which building catches fire</param>
        /// <param name="results">Output list of damage results</param>
        public static void CalculateAreaDamage(
            float3 center,
            float radius,
            float baseSeverity,
            NativeList<Entity> buildings,
            ComponentLookup<Transform> transformLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup,
            RenderWriteTicket renderTicket,
            Entity excludeTarget,
            float destructionThreshold,
            float fireThreshold,
            ref NativeList<DamageResult> results)
        {
            EnsureRenderTicket(renderTicket, RenderWriteComponentMask.BuildingTransform);

            foreach (var building in buildings)
            {
                if (building == excludeTarget) continue;
                if (deletedLookup.HasComponent(building) || destroyedLookup.HasComponent(building)) continue;

                if (!transformLookup.TryGetComponent(building, out var transform))
                    continue;

#pragma warning disable CIVIC078 // Needs actual distance for severity = 1 - (distance / radius)
                float distance = math.distance(
                    new float2(center.x, center.z),
                    new float2(transform.m_Position.x, transform.m_Position.z));
#pragma warning restore CIVIC078

                float localSeverity = radius > 0
                    ? baseSeverity * math.max(0f, 1f - (distance / radius))
                    : 0f;
                if (localSeverity <= 0f) continue;

                DamageType type = DamageType.None;
                if (localSeverity > destructionThreshold)
                    type = DamageType.Destroy;
                else if (localSeverity > fireThreshold)
                    type = DamageType.Fire;

                if (type != DamageType.None)
                {
                    results.Add(new DamageResult
                    {
                        Building = building,
                        Type = type,
                        Position = transform.m_Position,
                        Severity = localSeverity
                    });
                }
            }
        }

        private static void EnsureRenderTicket(RenderWriteTicket renderTicket, RenderWriteComponentMask requiredMask)
        {
            if (!renderTicket.Covers(requiredMask))
                throw new System.InvalidOperationException($"Render write ticket does not cover {requiredMask}");
        }
    }
}

