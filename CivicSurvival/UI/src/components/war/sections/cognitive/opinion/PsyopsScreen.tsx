/**
 * PsyopsScreen — the operate board. Group-by axis switch, whole-city opinion
 * line, the crowd view, the live RISK ribbon, and a context row of
 * countermeasures (for the selected stratum) + the network rail. The spatial
 * (map) view lives on the sibling RADAR tab, so the board is crowd-only —
 * MapView is kept in source pending a decision on a RADAR link.
 */

import React, { memo, useEffect, useMemo, useState } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { type useCognitiveActions } from "@hooks/actions";
import { bindingDataOrDefault, useCognitive, HeroStatus } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { firstStratum, cityOpinion, ARCHETYPE_BY_INT, type AxisId, type ArchetypeId } from "./opinionData";
import { useOpinion } from "./useOpinion";
import { createBoardStyles } from "./opinionStyles";
import { HelpSection } from "../../../../shared/common/HelpSection";
import { CrowdBoard } from "./CrowdBoard";
import { CityOpinionLine } from "./CityOpinionLine";
import { CountermeasuresPanel } from "./CountermeasuresPanel";
import { ToolSubView, type CountermeasureTool } from "./ToolSubView";
import { ManageCenterSubView } from "./ManageCenterSubView";
import { NetworkPanel } from "./NetworkPanel";

/** Drill-down target: a countermeasure tool, the hero picker, or the Center's manage view. */
type DrillTarget = CountermeasureTool | "manage";

interface PsyopsScreenProps {
    actions: ReturnType<typeof useCognitiveActions>;
    disabled: boolean;
}

// Households one dot represents. Larger dots read better at fewer-but-bigger, so
// each dot stands for more households than the original spec default (500).
const HOUSEHOLDS_PER_DOT = 950;

const legendDot = (color: string): React.CSSProperties => ({
    width: "8rem",
    height: "8rem",
    borderRadius: "4rem",
    backgroundColor: color,
    marginRight: "4rem",
});

export const PsyopsScreen = memo(({ actions, disabled }: PsyopsScreenProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);
    const control = accents.crisis.accent;

    const { axes, battleAxis, raids } = useOpinion();

    // Contacts whose target seam is still fogged. buildLiveWealthAxis attaches only REVEALED
    // ones to a card (the board must not leak the enemy's aim), so these belong to no stratum
    // — and without them the cards would report "no incoming ops" with raids in the air.
    const seamUnknownCount = useMemo(
        () => raids.filter((r) => !r.targetKnown).length,
        [raids]
    );

    // The currently deployed hero — one global lever. Read once here (not per card)
    // and threaded down so each stratum can show whether that hero covers its attack.
    // Inactive status = no hero deployed (null); Deployed/Lecturing carry an archetype.
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const currentArchetypeId: ArchetypeId | null =
        cw.HeroStatus === HeroStatus.Inactive ? null : (ARCHETYPE_BY_INT[cw.HeroArchetype] ?? null);

    const [axisId, setAxisId] = useState<AxisId>(battleAxis.id);
    const [selectedId, setSelectedId] = useState(firstStratum(battleAxis).id);
    // Drill-down: a chosen countermeasure fills the whole panel (ToolSubView); null
    // shows the board. Owned here so the sub-view replaces cards + rail, not a column.
    const [activeTool, setActiveTool] = useState<DrillTarget | null>(null);

    // Re-gate the open manage drill-down: the entry button is built-gated, but the Center can
    // stop existing while the view is open (bulldoze). Going OFFLINE is not a close reason —
    // upgrading a struck Center is valid; the allocation section inside gates itself on online.
    useEffect(() => {
        if (activeTool === "manage" && !cw.CenterBuilt) setActiveTool(null);
    }, [activeTool, cw.CenterBuilt]);

    const axis = useMemo(() => axes.find((a) => a.id === axisId) ?? battleAxis, [axes, axisId, battleAxis]);
    const stratum = useMemo(
        () => axis.strata.find((s) => s.id === selectedId) ?? firstStratum(axis),
        [axis, selectedId]
    );
    const opinion = useMemo(() => cityOpinion(axis), [axis]);
    // Whole-city population — axis-invariant (the same people, regrouped), so read
    // off the battle axis. This is the board's single home for the city size.
    const cityCount = useMemo(
        () => battleAxis.strata.reduce((sum, s) => sum + s.count, 0),
        [battleAxis]
    );

    const switchAxis = (next: AxisId): void => {
        const target = axes.find((a) => a.id === next) ?? battleAxis;
        setAxisId(next);
        setSelectedId(firstStratum(target).id);
    };

    const axisBtn = (active: boolean): React.CSSProperties => ({
        padding: "4rem 10rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color: active ? control : theme.colors.textMuted,
        backgroundColor: active ? hexToRgba(control, 0.12) : "transparent",
        border: active ? `3rem solid ${control}` : `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        textTransform: "uppercase",
        letterSpacing: "0.3rem",
        cursor: "pointer",
    });

    // Drill-down active → the chosen tool fills the whole PSYOPS panel; the board
    // (cards + countermeasures launcher + network rail) is hidden until "back".
    if (activeTool === "manage") {
        return (
            <Column gap={theme.spacing.sm} style={{ width: "100%", flex: 1, minHeight: 0 }}>
                <ManageCenterSubView actions={actions} onBack={() => setActiveTool(null)} />
            </Column>
        );
    }
    if (activeTool) {
        return (
            <Column gap={theme.spacing.sm} style={{ width: "100%", flex: 1, minHeight: 0 }}>
                <ToolSubView
                    tool={activeTool}
                    stratum={stratum}
                    actions={actions}
                    onBack={() => setActiveTool(null)}
                />
            </Column>
        );
    }

    return (
        // minHeight 100% (of the OpinionBoard scroller): cohtml does NOT stretch a
        // flex:1 item inside an overflow:auto container — without this the board
        // sizes to content and leaves a dead zone above the panel bottom.
        <Column gap={theme.spacing.sm} style={{ width: "100%", flex: 1, minHeight: "100%" }}>
            {/* BOARD region — natural height, never squished by the context row below */}
            <div style={{ ...b.region, flexShrink: 0 }}>
                {/* Top strip: the whole-city opinion bar, full width. The group-by
                    switch and the allegiance legend used to share this row; both moved
                    down (the switch above the cards, the legend beside the crowd dots
                    it explains) so the header carries only the city read. The region
                    title and shared header are dropped; the war tab + GlassCase already
                    name the board. */}
                {/* The "?" explains what an opinion IS made of (strata, infection vs education-born
                    resistance, blackout suggestibility, aid that backfires on the wrong stratum) —
                    it sits on the city read it explains. */}
                <Row align="center" style={b.headerStrip}>
                    <div style={{ flex: 1, minWidth: 0 }}>
                        <CityOpinionLine opinion={opinion} cityCount={cityCount} />
                    </div>
                    <HelpSection id="opinion" title={l.t("UI_OB_CITY_OPINION")}>{l.t("HELP_OPINION")}</HelpSection>
                </Row>

                <Column gap={theme.spacing.sm} style={{ padding: theme.spacing.sm, minHeight: "40rem" }}>
                    {/* Axis switcher (left) + allegiance legend (right) — both sit right
                        above the crowd: the switch regroups it, the legend names its dot
                        colours. The analysis hint lives on each button's tooltip. */}
                    <Row align="center" justify="space-between" wrap="wrap" gap={theme.spacing.sm}>
                        {/* Group-by switcher — hidden while there is a single axis (no dead
                            one-button control); a growth container for a second live lens. */}
                        {axes.length > 1 && (
                            <Row align="center" gap={theme.spacing.sm} wrap="wrap">
                                <span style={b.eyebrow}>{l.t("UI_OB_GROUP_BY")}</span>
                                <Row gap={theme.spacing.xs}>
                                    {axes.map((a) => (
                                        <button
                                            key={a.id}
                                            style={axisBtn(a.id === axisId)}
                                            title={l.t("UI_OB_AXIS_HINT")}
                                            onClick={() => switchAxis(a.id)}
                                        >
                                            {l.t(a.labelKey)}
                                        </button>
                                    ))}
                                </Row>
                            </Row>
                        )}
                        <Row align="center" gap={theme.spacing.md} wrap="wrap">
                            <Row align="center"><div style={legendDot(theme.colors.success)} /><span style={b.eyebrow}>{l.t("UI_OB_LEGEND_LOYAL")}</span></Row>
                            <Row align="center"><div style={legendDot(theme.colors.textMuted)} /><span style={b.eyebrow}>{l.t("UI_OB_LEGEND_UNDECIDED")}</span></Row>
                            <Row align="center"><div style={legendDot(theme.colors.error)} /><span style={b.eyebrow}>{l.t("UI_OB_LEGEND_ENEMY")}</span></Row>
                        </Row>
                    </Row>
                    <CrowdBoard
                        axis={axis}
                        selectedId={selectedId}
                        householdsPerDot={HOUSEHOLDS_PER_DOT}
                        dimDoubters={false}
                        currentArchetypeId={currentArchetypeId}
                        seamUnknownCount={seamUnknownCount}
                        onSelect={setSelectedId}
                    />
                </Column>
            </div>

            {/* CONTEXT row: countermeasures (flex) + fixed network rail. Basis auto
                (never below its own content) + grow — both columns always render
                whole, NO inner overflow:auto (a hidden clipped scroll is exactly the
                conditional interface this board bans). When board + row genuinely
                exceed the panel, the WHOLE board scrolls in the OpinionBoard
                scroller — the only scroll this screen is allowed. */}
            <Row gap={theme.spacing.sm} align="stretch" style={{ flex: "1 0 auto" }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                    <CountermeasuresPanel axis={axis} stratum={stratum} onOpenTool={setActiveTool} />
                </div>
                <div style={{ width: "330rem", flexShrink: 0 }}>
                    <NetworkPanel actions={actions} disabled={disabled} onManage={() => setActiveTool("manage")} />
                </div>
            </Row>
        </Column>
    );
});

PsyopsScreen.displayName = "PsyopsScreen";
