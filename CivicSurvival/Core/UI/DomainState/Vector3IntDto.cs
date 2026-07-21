namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Three-component integer vector for embedding inside contract DTOs
    /// (e.g. ThreatTargetDto.Position). Wire layout: {X, Y, Z}.
    /// </summary>
    public partial struct Vector3IntDto
    {
        public int X;
        public int Y;
        public int Z;

        public Vector3IntDto(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
