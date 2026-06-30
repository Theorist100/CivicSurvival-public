namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One radar threat wire entry for the ThreatUI radar binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Distinct from the runtime carrier CivicSurvival.Core.Utils.RadarThreatDto.
    /// </summary>
    public partial struct RadarThreatDto
    {
        // World-Y that maps to full radar brightness. Ballistics arc to ~2 km at apogee;
        // shaheds cruise low (~0.1–1 km) → dimmer track, which is the intended read.
        // Referenced by the generated FromRuntime to normalize carrier Y into 0..1.
        public const float AltitudeCeiling = 2000f;

        public EntityRefDto Entity;
        public float X;
        public float Z;
        public float Vx;
        public float Vz;
        public float Eta;
        // Normalized 0..1 altitude (world Y / ALTITUDE_CEILING, clamped). Drives radar
        // track brightness, not hue: hue carries status (ballistic/identified/evasive),
        // altitude must not collide with it. 0 = ground / unknown → full brightness.
        public float Altitude;
        public string Type;
        public string EvasionStatus;
        public bool IsIdentified;
    }
}
