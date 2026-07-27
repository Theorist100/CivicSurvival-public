using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Narrative.Quests;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.Narrative.UI
{
    /// <summary>
    /// Publishes narrative quest state for the GlobalStatus quest chip: a JSON list
    /// with one row per non-Inactive quest slot. Read-only projection of QuestSystem —
    /// commands do not exist (quests auto-start and auto-resolve), so no triggers.
    /// </summary>
    [ActIndependent]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CivicSurvival", "CIVIC098",
        Justification = "ModalCoordinator.Instance is static readonly and never null.")]
    public partial class QuestUISystem : CivicUIPanelSystem
    {
        private QuestSystem? m_QuestSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            // The screen side of a resolved quest: QuestSystem owns the state and announces
            // the outcome, this system decides what the player sees (and so keeps modal
            // ownership out of a serialized domain system — CIVIC317).
            SubscribeRequired<QuestResolvedEvent>(OnQuestResolved);
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<QuestResolvedEvent>(OnQuestResolved);
            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Feature-gate-safe sibling resolve (CIVIC400/403): registration-site creation only.
            m_QuestSystem ??= FeatureRegistry.Instance.Query<QuestSystem>();
        }

        private static void OnQuestResolved(QuestResolvedEvent evt)
        {
#pragma warning disable CIVIC239 // Result intentionally ignored: a louder modal wins the slot and this queues behind it
            Core.Services.ModalCoordinator.Instance.TryShow(
                evt.Success ? "QuestCompleted" : "QuestFailed",
                new QuestResultModalPayloadDto
                {
                    Id = evt.QuestId,
                    Progress = evt.Progress,
                    Target = evt.Target,
                });
#pragma warning restore CIVIC239
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(QuestState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            // Quests still auto-start and auto-resolve; the only player command is closing the
            // outcome modal (QuestCompleted / QuestFailed share it — one is active at a time).
            Triggers.Add(DismissQuestResult, FeatureIds.Narrative, OnDismissQuestResult);
        }

        private static void OnDismissQuestResult()
        {
            Core.Services.ModalCoordinator.Instance.Dismiss("QuestCompleted");
            Core.Services.ModalCoordinator.Instance.Dismiss("QuestFailed");
        }

        protected override void OnPanelUpdate()
        {
            var quests = m_QuestSystem;
            // Disabled/gated source → publish the empty list so the chip hides
            // instead of freezing on stale phases (CIVIC108).
            string json = quests != null && quests.Enabled
                ? BuildQuestsJson(quests)
                : JsonBuilder.EmptyArray;

            var dto = new QuestDto { QuestsJson = json };
            PublishWhenComplete(QuestState, NoSourceChecks, () => dto);
        }

        // Contract pipeline (CIVIC471): each entry serializes through the generated
        // QuestEntryDto.WriteTo — the SerializeRadarThreats pattern. Only the array
        // frame is assembled here; the wire shape stays owned by ui-dto.contract.yaml.
        private static string BuildQuestsJson(QuestSystem quests)
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < quests.SlotCount; i++)
            {
                var slot = quests.GetSlot(i);
                if (slot.Phase == QuestPhase.Inactive)
                    continue;

                var entry = new QuestEntryDto
                {
                    Id = (int)slot.Id,
                    Phase = (int)slot.Phase,
                    Progress = slot.Progress,
                    Target = slot.Target,
                    HoursLeft = quests.GetHoursLeft(i),
                    Anchor = slot.Anchor,
                };
                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
