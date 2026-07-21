/**
 * ToolSubView — the full-panel drill-down for one countermeasure.
 *
 * The countermeasures block is a compact launcher (CountermeasuresPanel); picking
 * a tool opens it here, filling the whole PSYOPS panel so the controls get real
 * room (the hero screen no longer cramps into a third of a column). A back button
 * returns to the board. Tool bodies are the LIVE controls, reused verbatim:
 * AID = ReliefTab, BROADCAST = InfoWarTab, HERO = HeroPicker (citywide lever).
 */

import React, { memo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { type useCognitiveActions } from "@hooks/actions";
import { type Stratum, type ToolKind, TOOL_LABEL } from "./opinionData";
import { createBoardStyles } from "./opinionStyles";
import { ReliefTab, InfoWarTab } from "../tabs";
import { HeroPicker } from "./HeroPicker";
import { BackButton } from "./BackButton";
import { Sep } from "./Sep";

/** The launcher's tool ids: the stratum's live tools plus the citywide hero lever. */
export type CountermeasureTool = ToolKind | "hero";

interface ToolSubViewProps {
    tool: CountermeasureTool;
    stratum: Stratum;
    actions: ReturnType<typeof useCognitiveActions>;
    onBack: () => void;
}

const TOOL_COMPONENT: Record<ToolKind, typeof ReliefTab> = {
    aid: ReliefTab,
    broadcast: InfoWarTab,
};

export const ToolSubView = memo(({ tool, stratum, actions, onBack }: ToolSubViewProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);

    const isHero = tool === "hero";
    const toolName = isHero ? l.t("UI_OB_HERO_PICKER") : l.t(TOOL_LABEL[tool]);
    const scope = isHero ? l.t("UI_OB_SCOPE_CITYWIDE") : l.t(stratum.nameKey);

    return (
        <Column gap={theme.spacing.sm} style={{ width: "100%", height: "100%", minHeight: 0 }}>
            {/* Back header — tool + scope. */}
            <Row align="center" gap={theme.spacing.sm} style={{ flexShrink: 0 }}>
                <BackButton onBack={onBack} />
                <span style={{
                    ...b.eyebrow,
                    color: theme.colors.textSecondary,
                    fontSize: theme.typography.sizeSM,
                    letterSpacing: "0.6rem",
                }}>
                    {toolName}<Sep />{scope}
                </span>
            </Row>

            {/* Tool body — the whole remaining panel height, its own scroll. */}
            <div style={{
                ...b.region,
                flex: 1,
                minHeight: 0,
                overflowY: "auto",
                overflowX: "hidden",
                padding: theme.spacing.md,
            }}>
                {isHero ? (
                    <HeroPicker actions={actions} />
                ) : (() => {
                    const ToolComponent = TOOL_COMPONENT[tool];
                    return <ToolComponent actions={actions} />;
                })()}
            </div>
        </Column>
    );
});

ToolSubView.displayName = "ToolSubView";
