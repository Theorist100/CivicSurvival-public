using System.Collections.Generic;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    // ============================================
    // THREAT TARGET DTOs
    // ============================================

    /// <summary>
    /// Single threat info for UI. Serialized via JsonBuilder by ThreatUISystem.
    /// </summary>
    public struct ThreatInfoDto
    {
        public string Type;      // "shahed" or "ballistic"
        public int EtaSeconds;
        public int DistanceMeters;
    }

    /// <summary>
    /// Threat target info for UI. Serialized via JsonBuilder by ThreatUISystem.
    /// </summary>
    public struct ThreatTargetDto
    {
        public int EntityIndex;
        public int EntityVersion;
        public string Name;
        public float3 Position;
        public int ThreatCount;
        public int MinEtaSeconds;
        public List<ThreatInfoDto> Threats;

        // Target-building geometry for the radar 2.5D box (zero when no geometry):
        // footprint width/height/length (ObjectGeometryData.m_Size) and yaw (radians).
        public float SizeX;
        public float SizeY;
        public float SizeZ;
        public float RotationY;
    }

    // ============================================
    // RADAR DTOs
    // ============================================

    /// <summary>
    /// Single threat for radar visualization. Real-time position and velocity.
    /// Serialized via JsonBuilder by ThreatUISystem.
    /// </summary>
    public struct RadarThreatDto
    {
        public EntityRef Entity;   // Index + Version identity for UI focus
        public float X, Y, Z;      // Current world position (Y = altitude)
        public float Vx, Vz;       // Normalized velocity direction
        public float Eta;          // Seconds to target
        public string Type;        // "shahed" or "ballistic"
        public string EvasionStatus; // "targeted", "evasive", "hardlock" - for UI color/animation
        public bool IsIdentified;  // true once player confirmed via camera tracking
    }

    /// <summary>
    /// Radar target for visualization. Serialized via JsonBuilder by ThreatUISystem.
    /// </summary>
    public struct RadarTargetDto
    {
        public EntityRef Entity;
        public float X, Z;
        public string Name;

        // Target-building geometry for the radar 2.5D box (zero when no geometry):
        // footprint width/height/length (meters) and yaw (radians).
        public float SizeX, SizeY, SizeZ;
        public float RotationY;
    }

    /// <summary>
    /// One air-defense coverage circle for the radar "defended zone" overlay: world
    /// ground position and engagement range (meters). Geometric only — no live readiness.
    /// Serialized via JsonBuilder by ThreatUISystem.
    /// </summary>
    public struct RadarDefenseDto
    {
        public float X, Z;
        public float Range;
    }

    // DistrictDto moved to Core/UI/DomainState/DistrictDto.cs in C4b2.
    // DistrictDtoFactory moved to Core/UI/DomainState/DistrictDtoFactory.cs.
    // PlantWearProducerData moved to
    // Core/Interfaces/Domain/Engineering/PlantWearProducerData.cs in C4b1.

}
