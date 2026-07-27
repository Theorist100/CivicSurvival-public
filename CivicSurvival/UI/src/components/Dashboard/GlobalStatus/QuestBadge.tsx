/**
 * QuestBadge — situational GlobalStatus chips for active narrative quests.
 *
 * Renders nothing while no quest is running (the bar's conditional-badge
 * pattern: ThreatBadge, CrisisEconomyBadge). One chip per entry in the
 * quest list: label + live detail (deadline countdown / progress / terminal
 * status). Click opens a details modal (HelpSection-style portal).
 *
 * Adding a quest = a row in QUEST_TEXTS + localization; the render is generic.
 */

import React, { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Z_INDEX, createBaseModalStyles, useAccents, useModalPalette, useTheme } from "../../../themes";
import { useLocale } from "../../../locales";
import { bindingDataOrDefault, isBindingLive, useQuests } from "@hooks/domain";
import { DEFAULT_QUEST_DTO } from "../../../types/domainDtos";
import { type QuestEntryDto } from "../../../types/domainDtos.generated";
import { getModRoot } from "@utils/modRoot";
import { HoverTip } from "../../shared/common/HoverTip";
import { type createGlobalStatusStyles } from "./GlobalStatus.styles";
import { QUEST_TEXTS } from "./questTexts";

// Mirror of C# QuestPhase (Core/Types/QuestTypes.cs).
const PHASE_ACTIVE = 1;
const PHASE_COMPLETED = 2;
const PHASE_FAILED = 3;

// Text keys live in questTexts so the chip and the quest-outcome modals read the same
// row. A quest missing there renders no chip (a save from a newer build must not
// surface an untranslatable chip).

interface BadgeProps {
    styles: ReturnType<typeof createGlobalStatusStyles>;
}

export const QuestBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const questState = useQuests();
    const quests = bindingDataOrDefault(questState, DEFAULT_QUEST_DTO);
    const live = isBindingLive(questState);
    const theme = useTheme();
    const accents = useAccents();
    const m = useModalPalette();
    const l = useLocale();
    const [openId, setOpenId] = useState<number | null>(null);
    // Multi-quest list modal (the bar carries ONE quest slot: a single quest renders its
    // full chip, two+ collapse into a "QUESTS N" counter whose click opens this list —
    // two full chips next to CRISIS/AFTERMATH clipped the bar, live report 2026-07-22).
    const [listOpen, setListOpen] = useState(false);
    const backdropPointerDownRef = useRef(false);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: theme.colors.accent,
        overlayOpacity: 0.85,
        zIndex: Z_INDEX.modal,
        width: "560rem",
    }), [m, theme.colors.accent]);

    const close = useCallback(() => { setOpenId(null); setListOpen(false); }, []);

    // The modal's visibility is derived from openId + a matching live entry. Without this
    // reset, an id left behind by a resolved-then-cleared quest re-MATCHES when the same
    // repeatable quest re-arms later — and the modal pops open with no player action.
    // Dropping openId the moment its entry leaves the list makes closing terminal.
    const openIdLive = openId !== null && quests.Quests.some((q) => q.Id === openId);
    useEffect(() => {
        if (openId !== null && !openIdLive) setOpenId(null);
    }, [openId, openIdLive]);
    // The list modal follows the same rule: when the roster drops back to 0..1 quests the
    // counter chip it belongs to is gone — a lingering open list would be an orphan.
    const questCount = quests.Quests.length;
    useEffect(() => {
        if (listOpen && questCount < 2) setListOpen(false);
    }, [listOpen, questCount]);
    const handleBackdropMouseDown = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        backdropPointerDownRef.current = e.target === e.currentTarget;
    }, []);
    const handleBackdropClick = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        const shouldClose = backdropPointerDownRef.current && e.target === e.currentTarget;
        backdropPointerDownRef.current = false;
        if (shouldClose) close();
    }, [close]);

    // DEFAULT list is empty, so the not-yet-live feed renders nothing —
    // no fabricated quest can appear before the first publish.
    if (!live) return null;

    const entries = quests.Quests.filter((q) => QUEST_TEXTS[q.Id] !== undefined);
    if (entries.length === 0) return null;

    const accentFor = (phase: number) =>
        phase === PHASE_COMPLETED ? accents.schemes
        : phase === PHASE_FAILED ? accents.crisis
        : accents.operations;

    const detailFor = (q: QuestEntryDto): string =>
        q.Phase === PHASE_ACTIVE
            ? (q.Target > 0
                ? `${q.Progress}/${q.Target}`
                : l.t("UI_QUEST_CHIP_HOURS_LEFT", Math.max(0, Math.ceil(q.HoursLeft))))
            : q.Phase === PHASE_COMPLETED
            ? l.t("UI_QUEST_STATUS_DONE")
            : l.t("UI_QUEST_STATUS_FAILED");

    const openEntry = openId === null ? undefined : entries.find((q) => q.Id === openId);
    const openTexts = openEntry ? QUEST_TEXTS[openEntry.Id] : undefined;

    // ONE quest slot in the bar. A lone quest gets its full chip; two+ collapse into a
    // counter chip — the list modal carries the detail. Counter accent = the most urgent
    // phase across entries (failed > active > completed), so a failing quest still tints
    // the bar red without its own chip.
    const counterAccent =
        entries.some((q) => q.Phase === PHASE_FAILED) ? accents.crisis
        : entries.some((q) => q.Phase === PHASE_ACTIVE) ? accents.operations
        : accents.schemes;

    return (
        <>
            {entries.length === 1 ? entries.map((q) => {
                const accent = accentFor(q.Phase);
                const texts = QUEST_TEXTS[q.Id];
                if (!texts) return null; // filtered above; narrows the type for tsc
                return (
                    <React.Fragment key={q.Id}>
                        <div
                            role="button"
                            tabIndex={0}
                            style={s.questBadge(accent.accent, accent.accentDim)}
                            onClick={() => setOpenId(q.Id)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    setOpenId(q.Id);
                                }
                            }}
                        >
                            <HoverTip text={l.tDynamic(texts.title)} style={s.questBadgeLabel(accent.accent)}>
                                {l.tDynamic(texts.chip)}
                            </HoverTip>
                            <span style={s.questBadgeDetail}>{detailFor(q)}</span>
                        </div>
                        <div style={s.separator} />
                    </React.Fragment>
                );
            }) : (
                <>
                    <div
                        role="button"
                        tabIndex={0}
                        style={s.questBadge(counterAccent.accent, counterAccent.accentDim)}
                        onClick={() => setListOpen(true)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                                e.preventDefault();
                                setListOpen(true);
                            }
                        }}
                    >
                        <span style={s.questBadgeLabel(counterAccent.accent)}>{l.t("UI_QUEST_CHIP_MANY")}</span>
                        <span style={s.questBadgeDetail}>{entries.length}</span>
                    </div>
                    <div style={s.separator} />
                </>
            )}
            {listOpen && createPortal(
                <div
                    style={base.overlay}
                    role="button"
                    tabIndex={0}
                    data-glasscase-allow=""
                    onMouseDown={handleBackdropMouseDown}
                    onClick={handleBackdropClick}
                    onKeyDown={(e) => {
                        if (e.key === "Escape" || e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            close();
                        }
                    }}
                >
                    <div style={base.modal} data-civic-delegated-click onClick={(e) => e.stopPropagation()}>
                        <div style={base.header}>
                            <h2 style={base.title}>{l.t("UI_QUEST_CHIP_MANY")}</h2>
                        </div>
                        <div style={base.body}>
                            {entries.map((q) => {
                                const texts = QUEST_TEXTS[q.Id];
                                if (!texts) return null;
                                const accent = accentFor(q.Phase);
                                return (
                                    <div key={q.Id} style={{ marginBottom: "16rem" }}>
                                        <p style={{
                                            color: accent.accent,
                                            fontSize: "15rem",
                                            fontWeight: 700,
                                            margin: 0,
                                            marginBottom: "4rem",
                                        }}>{`${l.tDynamic(texts.title)} — ${detailFor(q)}`}</p>
                                        {l.tDynamic(texts.desc).split("\n\n").map((p, i) => (
                                            <p key={i} style={{
                                                color: m.textBody,
                                                fontSize: "13rem",
                                                lineHeight: 1.5,
                                                margin: 0,
                                                marginBottom: "8rem",
                                            }}>{p}</p>
                                        ))}
                                        <p style={{
                                            color: accent.accent,
                                            fontSize: "12rem",
                                            fontWeight: 600,
                                            margin: 0,
                                        }}>{l.tDynamic(texts.reward)}</p>
                                    </div>
                                );
                            })}
                            <div style={base.buttonContainer}>
                                <button style={base.primaryButton} onClick={close}>
                                    {l.t("MODAL_CLOSE")}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>,
                getModRoot()
            )}
            {openEntry && openTexts && createPortal(
                <div
                    style={base.overlay}
                    role="button"
                    tabIndex={0}
                    data-glasscase-allow=""
                    onMouseDown={handleBackdropMouseDown}
                    onClick={handleBackdropClick}
                    onKeyDown={(e) => {
                        if (e.key === "Escape" || e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            close();
                        }
                    }}
                >
                    <div style={base.modal} data-civic-delegated-click onClick={(e) => e.stopPropagation()}>
                        <div style={base.header}>
                            <h2 style={base.title}>{l.tDynamic(openTexts.title)}</h2>
                        </div>
                        <div style={base.body}>
                            {l.tDynamic(openTexts.desc).split("\n\n").map((p, i) => (
                                <p key={i} style={{
                                    color: m.textBody,
                                    fontSize: "14rem",
                                    lineHeight: 1.6,
                                    margin: 0,
                                    marginBottom: "12rem",
                                }}>{p}</p>
                            ))}
                            <p style={{
                                color: accentFor(openEntry.Phase).accent,
                                fontSize: "13rem",
                                fontWeight: 600,
                                margin: 0,
                            }}>
                                {`${l.tDynamic(openTexts.reward)} — ${detailFor(openEntry)}`}
                            </p>
                            <div style={base.buttonContainer}>
                                <button style={base.primaryButton} onClick={close}>
                                    {l.t("MODAL_CLOSE")}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>,
                getModRoot()
            )}
        </>
    );
});
QuestBadge.displayName = "QuestBadge";
