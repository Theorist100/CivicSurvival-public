using System;
using System.Threading;
using Colossal.Serialization.Entities;
using Game;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Saves mod version metadata with game saves.
    /// Enables compatibility checking when loading older saves.
    /// </summary>
    [ActIndependent]
    public partial class SaveMetadataSystem : CivicSystemBase, IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        private static readonly LogContext Log = new("SaveMetadataSystem");

        private string m_SavedModVersion = "";
        private byte m_SavedFormatVersion = 0;
        private EntityQuery m_CurrentActQuery;

        // Flag pattern: Serialize runs off main thread, so set flag there and publish on next OnUpdate
        private int m_JustSaved; // 0 = no, 1 = yes (Interlocked access)


        /// <summary>
        /// Diagnostic only: true if loaded save was created with older mod format.
        /// Gameplay migrations must use per-domain SerializationGuard version blocks.
        /// </summary>
        public bool IsOlderSave => m_SavedFormatVersion < Mod.SAVE_FORMAT_VERSION;

        /// <summary>
        /// Diagnostic only: true if loaded save was created with newer mod format.
        /// Gameplay migrations must use per-domain SerializationGuard version blocks.
        /// </summary>
        public bool IsNewerSave => m_SavedFormatVersion > Mod.SAVE_FORMAT_VERSION;

        /// <summary>
        /// Version of mod that created the loaded save.
        /// </summary>
        public string SavedModVersion => m_SavedModVersion;

        /// <summary>
        /// Format version of the loaded save.
        /// </summary>
        public byte SavedFormatVersion => m_SavedFormatVersion;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            // CurrentActSingleton is foundational always-on (Scenario not gated); a save
            // only happens with the game running, so the act is always present — hard read.
            RequireForUpdate<CurrentActSingleton>();
            Log.Info("System created");
        }

        protected override void OnUpdateImpl()
        {
            // Publish GameSavedEvent on main thread (flag set by Serialize on serialization thread)
            var bus = EventBus;
            if (bus == null)
                return;

            if (Interlocked.CompareExchange(ref m_JustSaved, 0, 1) != 1)
                return;

            int day = 0;
            var gts = GameTimeSystem.Instance;
            if (gts != null)
                day = gts.Current.CurrentDay;

            var act = m_CurrentActQuery.GetSingleton<CurrentActSingleton>().CurrentAct;

            bus.SafePublish(new GameSavedEvent(day, act));
        }

        public void ResetState()
        {
            m_SavedModVersion = Mod.VERSION;
            m_SavedFormatVersion = Mod.SAVE_FORMAT_VERSION;
            Interlocked.Exchange(ref m_JustSaved, 0);
            Log.Info($"ResetState: v{Mod.VERSION} format={Mod.SAVE_FORMAT_VERSION}");
        }

        public void SetDefaults(Context context) => ResetState();

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                SaveMetadataCodec.Write(new SaveMetadataState(Mod.SAVE_FORMAT_VERSION, Mod.VERSION), writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }

            // Signal GameSavedEvent (published on next OnUpdateImpl — main thread safety)
            Interlocked.Exchange(ref m_JustSaved, 1);

            Log.Info($"Saved: v{Mod.VERSION} format={Mod.SAVE_FORMAT_VERSION}");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(SaveMetadataSystem)))
            {
                // Block marker not found — pre-guard save or unrecognized format.
                // SetDefaults leaves IsOlderSave/IsNewerSave as false (treated as current).
                Log.Warn("SerializationGuard block not found — treating as pre-guard save");
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                SaveMetadataCodec.Read(reader, out var metadata);
                ApplyMetadata(metadata);

                Log.Info($"Loaded save from v{m_SavedModVersion} format={m_SavedFormatVersion}");

                // Check compatibility
                if (IsNewerSave)
                {
                    Log.Warn($"Save is from NEWER mod version! " +
                        $"Save: v{m_SavedModVersion} (format {m_SavedFormatVersion}) → " +
                        $"Current: v{Mod.VERSION} (format {Mod.SAVE_FORMAT_VERSION}). " +
                        $"Some features may not work correctly.");
                }
                else if (IsOlderSave)
                {
                    Log.Info($"Upgrading save from format {m_SavedFormatVersion} → {Mod.SAVE_FORMAT_VERSION}");
                }
                else
                {
                    Log.Info($"Save format matches current version");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to deserialize metadata, treating as new save: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private void ApplyMetadata(in SaveMetadataState metadata)
        {
            m_SavedFormatVersion = metadata.FormatVersion;
            m_SavedModVersion = metadata.ModVersion;
        }
    }
}
