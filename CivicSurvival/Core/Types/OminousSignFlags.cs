using System;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Bitmask of triggered ominous signs during Village scenario pre-war phase.
    /// 7 signs (indices 0-6), each triggers at a specific day offset before war.
    /// </summary>
    [Flags]
#pragma warning disable CA1711, S2344 // Identifiers should not have incorrect suffix - this IS a flags enum
    public enum OminousSignFlags : byte
#pragma warning restore CA1711, S2344
    {
        None = 0,
        MilitaryConvoy  = 1 << 0,  // Sign 0: Day -20
        GasShortage     = 1 << 1,  // Sign 1: Day -15
        InternetProblems = 1 << 2, // Sign 2: Day -10
        BankLimits      = 1 << 3,  // Sign 3: Day -7
        EmptyShelves    = 1 << 4,  // Sign 4: Day -5
        PhoneLinesDown  = 1 << 5,  // Sign 5: Day -3
        MayorWarning    = 1 << 6,  // Sign 6: Day -1
    }
}
