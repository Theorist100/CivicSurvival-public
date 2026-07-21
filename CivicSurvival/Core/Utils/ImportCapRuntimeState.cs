namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Runtime bridge from the Harmony electricity hook to the ECS capacity writer.
    /// Keeps Patches out of domain assemblies while preserving a single capacity writer.
    /// </summary>
    public static class ImportCapRuntimeState
    {
        private static volatile int s_CurrentImportCapKW;
        private static volatile bool s_HasPublishedImportCap;
        private static volatile int s_CurrentExportCapKW;
        private static volatile bool s_HasPublishedExportCap;
        private static volatile int s_ExportInterconnectorCount;

        public static int CurrentImportCapKW => s_CurrentImportCapKW;
        public static bool HasPublishedImportCap => s_HasPublishedImportCap;

        public static int CurrentExportCapKW => s_CurrentExportCapKW;
        public static bool HasPublishedExportCap => s_HasPublishedExportCap;

        /// <summary>Trade-marker count from the resolver's last export-cap pass (sticky between ticks).</summary>
        public static int ExportInterconnectorCount => s_ExportInterconnectorCount;

        /// <summary>
        /// Hard ceiling for what the city can physically export right now: per-interconnector
        /// cap × interconnector count. The flow-difference proxy (RawBalance − ExternalPower)
        /// carries ±a-few-MW rounding noise, so every display/ceiling consumer clamps with this —
        /// with cap 0 the phantom "10 MW export" otherwise flickers in the UI.
        /// </summary>
        public static int CurrentExportCapTotalKW
        {
            get
            {
                long total = (long)s_CurrentExportCapKW * s_ExportInterconnectorCount;
                return total > int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        public static void SetExportInterconnectorCount(int value)
        {
            if (value < 0)
                value = 0;

            s_ExportInterconnectorCount = value;
        }

        public static void SetCurrentImportCapKW(int value)
        {
            if (value < 0)
                value = 0;

            s_CurrentImportCapKW = value;
            s_HasPublishedImportCap = true;
        }

        public static void SetCurrentExportCapKW(int value)
        {
            if (value < 0)
                value = 0;

            s_CurrentExportCapKW = value;
            s_HasPublishedExportCap = true;
        }

        public static void Reset()
        {
            s_CurrentImportCapKW = 0;
            s_HasPublishedImportCap = false;
            s_CurrentExportCapKW = 0;
            s_HasPublishedExportCap = false;
            s_ExportInterconnectorCount = 0;
        }
    }
}
