using CivicSurvival.Core.Services;

namespace CivicSurvival.Domains.Cognitive.Ops.Countermeasures
{
    /// <summary>
    /// Chirper messages for Buckwheat Protocol (social stabilization).
    /// Uses SatireRegistry instead of direct SatireMessageHelper.
    ///
    /// The most cynical social reactions in city-builder history:
    /// - Babushkas praising the mayor for food while electricity is out
    /// - Valera exposing the buckwheat procurement scheme
    /// - Students complaining but accepting free food
    /// </summary>
    public static class BuckwheatMessages
    {
        /// <summary>
        /// Babushka praising the mayor for food aid.
        /// </summary>
        public static string GetBabushkaMessage(string districtName)
            => SatireRegistry.GetMessage("SATIRE_BUCKWHEAT_BABUSHKA", districtName);

        /// <summary>
        /// Valera exposing the buckwheat scheme.
        /// </summary>
        public static string GetValeraMessage()
            => SatireRegistry.GetMessage("SATIRE_BUCKWHEAT_VALERA");

        /// <summary>
        /// Student complaining but accepting food.
        /// </summary>
        public static string GetStudentMessage()
            => SatireRegistry.GetMessage("SATIRE_BUCKWHEAT_STUDENT");

        /// <summary>
        /// Messages about suspicious procurement.
        /// </summary>
        public static string GetProcurementMessage(double tons)
            => SatireRegistry.GetMessage("SATIRE_BUCKWHEAT_PROCUREMENT", tons);
    }
}
