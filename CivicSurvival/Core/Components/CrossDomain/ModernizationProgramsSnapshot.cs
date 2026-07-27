using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct ModernizationProgramsSnapshot : IEquatable<ModernizationProgramsSnapshot>
    {
        public ModernizationProgramsSnapshot(int programCount, int pendingCleanupDistrictCount, int pendingCleanupBuildingCount, int contentHash)
        {
            ProgramCount = programCount;
            PendingCleanupDistrictCount = pendingCleanupDistrictCount;
            PendingCleanupBuildingCount = pendingCleanupBuildingCount;
            ContentHash = contentHash;
        }

        public int ProgramCount { get; }
        public int PendingCleanupDistrictCount { get; }
        public int PendingCleanupBuildingCount { get; }
        public int ContentHash { get; }

        public static ModernizationProgramsSnapshot Empty { get; } = new(0, 0, 0, 0);

        public bool Equals(ModernizationProgramsSnapshot other)
            => ProgramCount == other.ProgramCount
                && PendingCleanupDistrictCount == other.PendingCleanupDistrictCount
                && PendingCleanupBuildingCount == other.PendingCleanupBuildingCount
                && ContentHash == other.ContentHash;

        public override bool Equals(object? obj)
            => obj is ModernizationProgramsSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(ProgramCount, PendingCleanupDistrictCount, PendingCleanupBuildingCount, ContentHash);

        public static bool operator ==(ModernizationProgramsSnapshot left, ModernizationProgramsSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ModernizationProgramsSnapshot left, ModernizationProgramsSnapshot right)
            => !left.Equals(right);
    }
}
