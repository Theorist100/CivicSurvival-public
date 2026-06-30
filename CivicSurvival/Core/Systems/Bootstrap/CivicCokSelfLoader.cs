using System.Collections.Generic;
using System.IO;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Disk-presence check for the mod's core threat models (<c>AttackDrone.cok</c> /
    /// <c>Rocket.cok</c>), used to distinguish a broken/incomplete download (file absent) from a
    /// load failure (file on disk but never registered into <c>PrefabSystem</c>). Resolved against
    /// <see cref="ModPaths.ModInstallDirectory"/> so it works for both a dev deploy and a Paradox
    /// Mods subscriber.
    /// </summary>
    internal static class CivicCokSelfLoader
    {
        // Core threat models whose absence kills the mod's gameplay (drones + ballistics). The .cok
        // filename is the prefab name + ".cok" (build copies them to the mod root).
        private static readonly string[] s_CoreCokFiles = { "AttackDrone.cok", "Rocket.cok" };

        // Returns the comma-joined core .cok NOT on disk (empty string = all present).
        // ParadoxNativeLoader gates on this (wait until the files arrive); FinalizeMissing uses it to
        // classify a genuine miss (file absent = bad download vs file present = load/registration failure).
        public static string MissingCoreCokOnDisk()
        {
            string dir = ModPaths.ModInstallDirectory;
            var missing = new List<string>(s_CoreCokFiles.Length);
            foreach (var cok in s_CoreCokFiles)
            {
                if (!File.Exists(Path.Combine(dir, cok)))
                    missing.Add(cok);
            }
            return string.Join(", ", missing);
        }
    }
}
