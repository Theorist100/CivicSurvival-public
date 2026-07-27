using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Narrative.Quests
{
    /// <summary>
    /// QuestSystem save/load: the slot list round-trips through QuestStateCodec.
    /// Slots are matched by QuestId on read — saves carrying quests this build does
    /// not know are skipped, saves missing a quest leave its boot-default slot.
    /// Deadlines/cooldowns are absolute TotalGameHours timestamps, so no post-load
    /// reconciliation is needed: the clock they compare against persists with the save.
    /// </summary>
    public partial class QuestSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            // Belongs to the intro that opened the window; a value left over from the previous
            // city would size the next one's target (CS2 reuses system instances across loads).
            m_ActivistWindowManpower = 0;

            for (int i = 0; i < m_Slots.Length; i++)
            {
                m_Slots[i] = new QuestSlot { Id = m_Slots[i].Id, Anchor = QuestSlot.NO_ANCHOR };
                // Pending calls are process-transient (toast ids do not survive a load, and
                // CS2 reuses this system instance across loads) — a call caught by a load
                // reads as "missed", without a cooldown.
                m_PendingOfferToastIds[i] = 0;
                m_PendingOfferAnchors[i] = 0;
            }
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                QuestStateCodec.Write(m_Slots, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, GetType().Name))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                ResetState();
                int applied = 0;
                int unknown = 0;
                QuestStateCodec.Read(reader, slot =>
                {
                    int index = IndexOf(slot.Id);
                    if (index >= 0)
                    {
                        m_Slots[index] = slot;
                        applied++;
                    }
                    else
                    {
                        unknown++;
                    }
                });

                Log.Info($"Deserialized: {applied} quest slot(s) restored{(unknown > 0 ? $", {unknown} unknown skipped" : "")}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
