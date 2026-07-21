using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Contract types of the mirror-city generator (types only, no logic).
    ///
    /// The deterministic generator (<c>Core.Logic.MirrorCityGenerator</c>) and
    /// <c>Domains.GridWarfare.Systems.MirrorCitySystem</c> reference these managed PODs so the
    /// hand-off is explicit:
    ///   generator INPUT  = a <see cref="MirrorCityLandMask"/> (baked land contour, loaded on the
    ///                       domain side via <c>Core.Services.MirrorCityMapCatalog</c> and passed
    ///                       in — Core → Domain is banned by Axiom 5, so the generator never loads
    ///                       resources itself);
    ///   generator OUTPUT = a <see cref="GeneratedCity"/> (the placed targets);
    ///   system           = maps <see cref="GeneratedCity"/> → <c>DynamicBuffer&lt;EnemyTarget&gt;</c>
    ///                       (each <see cref="GeneratedTarget"/> becomes one <see cref="EnemyTarget"/>,
    ///                       <c>Id = index</c>, full hp, <c>RebuildProgress = 1</c>).
    /// </summary>
    public struct GeneratedTarget
    {
        /// <summary>Which axis this target feeds (AA sites are Kinetic/physical with zero <see cref="Contrib"/>).</summary>
        public AttackCategory Axis;

        /// <summary>Life-cycle tier (reserve/key/regular/aa).</summary>
        public MirrorTargetTier Tier;

        /// <summary>Map X position.</summary>
        public float X;

        /// <summary>Map Z position.</summary>
        public float Z;

        /// <summary>Full axis contribution when operational (0 for AA sites).</summary>
        public float Contrib;

        /// <summary>Full hit points; the system seeds the live target at full hp.</summary>
        public float MaxHp;

        /// <summary>Air-defence coverage radius (AA sites only; 0 otherwise).</summary>
        public float AaRange;
    }

    /// <summary>
    /// Generator output — the placed targets of one mirror city. Managed POD (a plain list) crossing
    /// the pure-logic → system boundary once at generation time; the live, per-tick state lives in the
    /// <c>EnemyTarget</c> buffer, not here.
    /// </summary>
    public class GeneratedCity
    {
        /// <summary>Placed targets, in a stable order. The system assigns each target's <c>Id</c> from its index here.</summary>
        public List<GeneratedTarget> Targets { get; } = new();
    }
}
