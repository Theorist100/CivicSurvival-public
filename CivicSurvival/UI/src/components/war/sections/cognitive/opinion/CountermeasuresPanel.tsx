/**
 * CountermeasuresPanel — the compact launcher for the selected stratum.
 *
 * Battle axis (wealth): optional threat card (live raid sample) → best-answer line
 * → a row of tool launchers. Each launcher opens the tool in a full-panel
 * drill-down (ToolSubView) so the controls get real room instead of cramming into
 * a third of a column: AID = Buckwheat (ReliefTab), BROADCAST = Telemarathon
 * (InfoWarTab), HERO = the citywide hero lever (HeroPicker). Analysis axes show a note.
 */

import React, { memo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { bindingDataOrDefault, useBuckwheat, useCognitive, HeroStatus } from "@hooks/domain";
import { DEFAULT_BUCKWHEAT_DTO, DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { ProgressBar } from "../../../../shared/ui";
import { IconAlert } from "../../../../shared/common/Icons";
import {
    type Axis, type Stratum, type ToolKind,
    ARCHETYPES, ARCHETYPE_BY_INT, RAID_TYPE_LABEL, TOOL_LABEL, raidStatus, raidAccentState,
} from "./opinionData";
import { createBoardStyles, raidAccent } from "./opinionStyles";
import { type CountermeasureTool } from "./ToolSubView";
import { Sep } from "./Sep";

interface CountermeasuresPanelProps {
    axis: Axis;
    stratum: Stratum;
    /** Open a tool in the full-panel drill-down. */
    onOpenTool: (tool: CountermeasureTool) => void;
}

export const CountermeasuresPanel = memo(({ axis, stratum, onOpenTool }: CountermeasuresPanelProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);

    // Live status for the launcher lines so the board reads state without drilling in.
    const buckwheat = bindingDataOrDefault(useBuckwheat(), DEFAULT_BUCKWHEAT_DTO);
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);


    // Status as discrete parts — rendered with the font-safe <Sep> hairline between
    // them (the Coherent mono font has no "·"/"•" glyph, both render as tofu).
    const toolStatus = (tool: ToolKind): string[] => {
        if (tool === "aid") {
            const tons = buckwheat?.BuckwheatTons ?? 0;
            const level = buckwheat?.ProcurementLevel ?? 0;
            const parts = tons >= 1
                ? [`${tons.toFixed(1)} ${l.t("UI_RELIEF_TONS")}`, l.t("UI_RELIEF_READY")]
                : [l.t("UI_RELIEF_NEED_MORE", (1 - tons).toFixed(1))];
            if (level > 0) parts.push(`${level}%`);
            return parts;
        }
        // broadcast
        return [cw?.TelemarathonActive ? l.t("UI_OB_BROADCAST_LIVE") : l.t("UI_OB_BROADCAST_IDLE")];
    };

    const launchBtn: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        width: "100%",
        padding: "11rem 13rem",
        backgroundColor: theme.colors.surface,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
        textAlign: "left" as const,
    };
    const btnName: React.CSSProperties = {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        letterSpacing: "0.6rem",
        textTransform: "uppercase" as const,
        color: theme.colors.textSecondary,
        whiteSpace: "nowrap" as const,
    };
    const statusLine: React.CSSProperties = {
        fontSize: theme.typography.sizeXS,
        // Same mono font as the name (both pixel-font) so they sit on one line.
        fontFamily: theme.typography.fontFamilyMono,
        color: theme.colors.textMuted,
        whiteSpace: "nowrap" as const,
    };
    const chev: React.CSSProperties = {
        marginLeft: "10rem",
        fontSize: theme.typography.sizeMD,
        color: theme.colors.textMuted,
    };

    // Hero status — a citywide lever, but the best-answer line recommends a hero
    // per stratum, so its launcher sits here (next to that recommendation), flagged
    // citywide. One hero covers the whole city; you can only field one at a time.
    const heroArchId = ARCHETYPE_BY_INT[cw.HeroArchetype] ?? "voice";
    const heroArchName = l.t(ARCHETYPES.find((a) => a.id === heroArchId)?.nameKey ?? "UI_OB_ARCH_VOICE");
    const heroStatus: string[] = [
        heroArchName,
        l.t(cw.HeroStatus !== HeroStatus.Inactive ? "UI_OB_HERO_ACTIVE" : "UI_OB_HERO_INACTIVE"),
        l.t("UI_OB_SCOPE_CITYWIDE"),
    ];

    // Single-line launcher: name + status inline (compact), so all tools + the hero
    // fit the column without clipping — no font shrink.
    const launcher = (key: CountermeasureTool, name: string, statusParts: string[]): React.ReactNode => (
        <button key={key} style={launchBtn} onClick={() => onOpenTool(key)}>
            <Row align="center" style={{ minWidth: 0, overflow: "hidden" }}>
                <span style={btnName}>{name}</span>
                {statusParts.map((part, i) => (
                    <React.Fragment key={i}>
                        <Sep />
                        <span style={statusLine}>{part}</span>
                    </React.Fragment>
                ))}
            </Row>
            <span style={chev}>{"›"}</span>
        </button>
    );

    return (
        <Column gap={theme.spacing.sm} style={{ width: "100%", minHeight: "120rem" }}>
            <Row align="center" justify="space-between">
                <span style={{ ...b.eyebrow, color: theme.colors.textSecondary, fontSize: theme.typography.sizeXS }}>
                    {l.t("UI_OB_COUNTERMEASURES")}<Sep />{l.t(stratum.nameKey)}
                </span>
                <span style={b.chip(accents.crisis.accent)}>{l.t("UI_OB_PER_STRATUM")}</span>
            </Row>

            {!axis.isBattle && (
                <div style={{ ...b.card }}>
                    <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted }}>
                        {l.t("UI_OB_ANALYSIS_ONLY")}
                    </span>
                </div>
            )}

            {axis.isBattle && (
                <>
                    {stratum.raid && (
                        <ThreatCard stratum={stratum} />
                    )}

                    {/* No "best answer" line here on purpose: the counter advice already sits on the
                        stratum card directly above this panel (StratumCard's counter chip — "need
                        Patriot / covers: Patriot"), which says the same thing AND adds whether the
                        deployed hero already covers the carrier. A second copy twenty pixels below
                        was pure noise. The RULE behind the advice lives in the "?" on the raid list;
                        the live recommendation for a fogged/unseen contact lives in the INTEL list,
                        where there are no stratum cards to carry it. */}

                    {/* Launchers with live status — each opens the tool in the full-panel
                        drill-down. Per-stratum tools first, then the citywide hero lever. */}
                    <Column gap={theme.spacing.xs}>
                        {stratum.tools.map((tool) => launcher(tool, l.t(TOOL_LABEL[tool]), toolStatus(tool)))}
                        {launcher("hero", l.t("UI_OB_HERO_PICKER"), heroStatus)}
                    </Column>
                </>
            )}
        </Column>
    );
});

CountermeasuresPanel.displayName = "CountermeasuresPanel";

const ThreatCard = memo(({ stratum }: { stratum: Stratum }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const raid = stratum.raid;
    if (!raid) return null;
    // Colour contract (raidAccentState): landed-blunted green ("contained"), landed-clean
    // red ("struck" — damage, never success-green), in-flight amber ("live").
    const color = raidAccent(raidAccentState(raid), theme, accents);
    const windowPct = Math.max(0, Math.min(100, raid.windowFraction * 100));
    const status = raidStatus(raid);

    return (
        <div style={{
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.surface,
            border: `2rem solid ${hexToRgba(color, 0.5)}`,
            borderLeft: `4rem solid ${color}`,
            borderRadius: theme.layout.borderRadiusLg,
        }}>
            <Row align="center" justify="space-between" style={{ marginBottom: "4rem" }}>
                <Row align="center">
                    <span style={{ color, marginRight: "5rem", display: "flex" }}><IconAlert /></span>
                    <span style={{
                        fontSize: theme.typography.sizeXS, fontWeight: 700, fontFamily: theme.typography.fontFamilyMono,
                        color, textTransform: "uppercase", letterSpacing: "0.4rem",
                    }}>{raid.known ? l.t(RAID_TYPE_LABEL[raid.type]) : l.t("UI_OB_RAID_UNKNOWN")}</span>
                </Row>
                <span style={{ fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono, color }}>
                    {l.t(status.key, ...(status.arg !== undefined ? [status.arg] : []))}
                </span>
            </Row>
            <ProgressBar value={windowPct} color={color} height="6rem" />
        </div>
    );
});

ThreatCard.displayName = "ThreatCard";
