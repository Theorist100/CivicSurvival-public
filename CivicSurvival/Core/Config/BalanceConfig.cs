using System.Threading;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// Immutable container for balance configuration state.
    /// Enables lock-free atomic updates via reference swap.
    /// </summary>
    internal sealed class BalanceContext
    {
        public readonly RemoteBalanceConfig Local;
        public readonly RemoteBalanceConfig? Arena;

        public BalanceContext(RemoteBalanceConfig local, RemoteBalanceConfig? arena)
        {
            Local = local;
            Arena = arena;
        }

        public RemoteBalanceConfig Active => Arena ?? Local;
        public bool IsArenaMode => Arena != null;
    }

    /// <summary>
    /// Static accessor for balance configuration with PvP Arena support.
    ///
    /// Architecture:
    /// - BalanceConfig.Current always returns the active config
    /// - In single-player: returns local JSON config (editable by players)
    /// - In PvP Arena: returns server-enforced config (anti-cheat)
    ///
    /// Usage:
    ///   BalanceConfig.Current.Threats.ShahedSpeed
    ///   BalanceConfig.Current.AirDefense.Range
    ///
    /// The switch between local/arena config is automatic:
    /// - SetArenaOverride() called when joining PvP match
    /// - ClearArenaOverride() called when leaving PvP match
    /// - All existing code continues to read from Current without changes
    ///
    /// Thread-safety: Lock-free via immutable BalanceContext + CAS pattern.
    /// </summary>
    public static class BalanceConfig
    {
        private static readonly LogContext Log = new("BalanceConfig");

        private static BalanceContext s_Context = CreateDefaultContext();

        /// <summary>
        /// Current balance configuration.
        /// Returns server config in PvP mode, local config otherwise.
        /// Never null - falls back to defaults if not set.
        /// Lock-free read.
        /// </summary>
        public static RemoteBalanceConfig Current => Volatile.Read(ref s_Context).Active;

        /// <summary>
        /// True when in PvP Arena mode (server config active).
        /// Local edits are ignored while this is true.
        /// Lock-free read.
        /// </summary>
        public static bool IsArenaMode => Volatile.Read(ref s_Context).IsArenaMode;

        /// <summary>
        /// Set the local configuration. Called by RemoteConfigService after loading.
        /// This config is used in single-player and as fallback.
        /// Thread-safe via CAS.
        /// </summary>
        public static RemoteBalanceConfig SetConfig(RemoteBalanceConfig config)
        {
            var newLocal = CreateValidatedCopy(config);
            BalanceContext oldContext, newContext;
            do
            {
                oldContext = Volatile.Read(ref s_Context);
                newContext = new BalanceContext(newLocal, oldContext.Arena);
            }
            while (Interlocked.CompareExchange(ref s_Context, newContext, oldContext) != oldContext);

            Log.Info($" Local config loaded: v{newLocal.Version}");
            return newLocal;
        }

        /// <summary>
        /// Set server-enforced config for PvP Arena.
        /// While active, Current returns this config instead of local.
        /// Call when player joins a PvP match.
        /// Thread-safe via CAS.
        /// </summary>
        public static RemoteBalanceConfig? SetArenaOverride(RemoteBalanceConfig serverConfig)
        {
            if (serverConfig == null)
            {
                Log.Warn(" SetArenaOverride called with null - ignored");
                return null;
            }

            var newArena = CreateValidatedCopy(serverConfig);

            BalanceContext oldContext, newContext;
            do
            {
                oldContext = Volatile.Read(ref s_Context);
                newContext = new BalanceContext(oldContext.Local, newArena);
            }
            while (Interlocked.CompareExchange(ref s_Context, newContext, oldContext) != oldContext);

            Log.Info($" Arena mode ACTIVE - server config v{newArena.Version} (local edits ignored)");
            return newArena;
        }

        /// <summary>
        /// Clear server config and return to local config.
        /// Call when player leaves PvP match.
        /// Thread-safe via CAS.
        /// </summary>
        public static void ClearArenaOverride()
        {
            BalanceContext oldContext, newContext;
            do
            {
                oldContext = Volatile.Read(ref s_Context);
                if (oldContext.Arena == null) return; // Already cleared
                newContext = new BalanceContext(oldContext.Local, null);
            }
            while (Interlocked.CompareExchange(ref s_Context, newContext, oldContext) != oldContext);

            Log.Info(" Arena mode ENDED - returned to local config");
        }

        /// <summary>
        /// Reset to default configuration (for testing).
        /// Also clears any arena override.
        /// Thread-safe via atomic assignment.
        /// </summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref s_Context, CreateDefaultContext());
        }

        private static BalanceContext CreateDefaultContext()
        {
            return new BalanceContext(CreateValidatedCopy(new RemoteBalanceConfig()), null);
        }

        /// <summary>
        /// Take ownership of a mutable config by deep-copying and validating before publication.
        /// The returned instance is the one stored in the active context; callers must not mutate it.
        /// Uses generated Clone() (no reflection); replaces former JSON.Dump/Load round-trip.
        /// </summary>
        public static RemoteBalanceConfig CreateValidatedCopy(RemoteBalanceConfig? config)
        {
            var copy = config?.Clone() ?? new RemoteBalanceConfig();
            copy.Validate();
            return copy;
        }
    }
}
