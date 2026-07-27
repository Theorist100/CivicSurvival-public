/**
 * QuestResultModal — how a narrative quest ends.
 *
 * One component behind two coordinator ids (QuestCompleted / QuestFailed): the outcome
 * only changes the accent, the icon and which text keys are read, so a single definition
 * keeps them from drifting apart. Registered twice in scenarioModalRegistry.
 *
 * Before this existed a resolved quest spoke only through a social-feed post and a chip
 * that cleared itself after a few game hours — at normal speed players met outcomes they
 * were never told about. The payload carries the counter because the slot zeroes it when
 * the chip clears, so "3 / 4" has to be a snapshot, not a live read.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { StatCard, StatCardRow, defineModal } from "../shared/modal";
import { dismissQuestResult } from "../../hooks/bindings/modalCoordinatorBindings";
import { useLocale } from "../../locales";
import { IconShield, IconTarget } from "../shared/common/Icons";
import {
    isQuestResultModalPayloadDto,
    type QuestResultModalPayloadDto,
} from "../../types/domainDtos";
import { QUEST_TEXTS } from "../Dashboard/GlobalStatus/questTexts";

interface QuestResultModalViewProps {
    payload: QuestResultModalPayloadDto;
    succeeded: boolean;
}

const QuestResultModalView: React.FC<QuestResultModalViewProps> = ({ payload, succeeded }) => {
    const l = useLocale();
    const m = useModalPalette();
    const ACCENT = succeeded ? m.accents.success : m.accents.crisis;

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: succeeded ? m.accents.success : m.accents.crisis,
        overlayOpacity: 0.85,
        width: "420rem",
    }), [m, succeeded]);

    const texts = QUEST_TEXTS[payload.Id];
    // A save from a newer build can carry a quest this bundle has no texts for. Render
    // nothing rather than a modal full of raw keys — the same rule the chip follows.
    if (!texts) return null;

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <span style={base.headerIcon}>{succeeded ? <IconShield /> : <IconTarget />}</span>
                    <h2 style={base.title}>
                        {l.t(succeeded ? "MODAL_QUEST_DONE_TITLE" : "MODAL_QUEST_FAILED_TITLE")}
                    </h2>
                    <div style={base.subtitle}>{l.tDynamic(texts.title)}</div>
                </div>

                <div style={base.body}>
                    <p style={base.text}>
                        {l.t(succeeded ? "MODAL_QUEST_DONE_TEXT" : "MODAL_QUEST_FAILED_TEXT")}
                    </p>

                    {payload.Target > 0 && (
                        <StatCardRow>
                            <StatCard
                                label={l.t("MODAL_QUEST_RESULT_PROGRESS")}
                                value={`${payload.Progress} / ${payload.Target}`}
                                valueColor={ACCENT}
                            />
                        </StatCardRow>
                    )}

                    <p style={base.textItalicDimmed}>{l.tDynamic(texts.reward)}</p>

                    <div style={base.buttonContainer}>
                        <button style={base.primaryButton} onClick={dismissQuestResult}>
                            {l.t("MODAL_CLOSE")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const QuestCompletedModalDef = defineModal({
    id: "QuestCompleted",
    render: (payload) => isQuestResultModalPayloadDto(payload)
        ? <QuestResultModalView payload={payload} succeeded />
        : null,
});

export const QuestFailedModalDef = defineModal({
    id: "QuestFailed",
    render: (payload) => isQuestResultModalPayloadDto(payload)
        ? <QuestResultModalView payload={payload} succeeded={false} />
        : null,
});
