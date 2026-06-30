/**
 * GridWarfarePanel - Main War Room UI (STRIKE view)
 * Layout: Header (Shadow Balance) | Left (three mirror axes + stability) | Right (Intel + Operations)
 */

import React, { memo, useMemo } from "react";
import { Column } from "../coherent";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles } from "./GridWarfare.styles";
import { type GridWarfareDto } from "../../types/domainDtos";
import { IconAlert } from "../shared/common/Icons";
import { useLocale } from "../../locales";
import { ShadowBalanceDisplay } from "./ShadowBalanceDisplay";
import { AxisBar, StabilityBar } from "./PressureBar";
import { OperationSlots } from "./OperationSlots";
import { IntelPreview } from "./IntelPreview";
import { GlassCase, StatRow } from "../shared/ui";
import { useGridWarfareActions, useIntelActions } from "../../hooks/actions";
import { bindingDataOrDefault, useGridWarfareDomain, useIntel } from "../../hooks/domain";
import { DEFAULT_GRID_WARFARE_DTO, DEFAULT_INTEL_DTO } from "../../types/domainDtos";
import { useSafeNumber } from "@hooks/useSafeBinding";
import { currentAct$, Act } from "@hooks/bindings/scenarioDirectorBindings";

const GridWarfarePanelReady = memo(({ gw }: { gw: GridWarfareDto }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();
    const gridWarfareActions = useGridWarfareActions();
    const interceptPercent = Math.round(gw.EnemyInterceptChance * 100);

    return (
        <div style={{ position: "relative" as const }}>
            {/* Locked overlay - dims content but keeps it visible.
                Inline because only Grid Warfare uses this lock-overlay UX. */}
            {!gw.GridWarfareUnlocked && (
                <Column align="center" justify="center" gap="8rem" style={{
                    position: "absolute" as const,
                    top: 0,
                    left: 0,
                    right: 0,
                    bottom: 0,
                    background: theme.effects.glassBackground,
                    zIndex: 10,
                    borderRadius: theme.layout.borderRadius,
                }}>
                    <div style={{
                        fontSize: "36rem",
                        opacity: 0.7,
                        color: theme.colors.textSecondary,
                    }}>
                        <IconAlert />
                    </div>
                    <div style={{
                        fontSize: "14rem",
                        fontWeight: 700,
                        color: theme.colors.textPrimary,
                        textTransform: "uppercase" as const,
                    }}>
                        {l.t("UI_GRIDWARFARE_LOCKED_TITLE")}
                    </div>
                    <div style={{
                        fontSize: "10rem",
                        color: theme.colors.textSecondary,
                        textAlign: "center" as const,
                        maxWidth: "260rem",
                        lineHeight: 1.4,
                    }}>
                        {l.t("UI_GRIDWARFARE_LOCKED_TEXT")}
                    </div>
                </Column>
            )}
            <Column gap="8rem" style={{ padding: "8rem" }}>
                {/* Header - Shadow Balance (full width) */}
                <ShadowBalanceDisplay
                    balance={gw.ShadowBalance}
                    locked={gw.ShadowLocked}
                    total={gw.ShadowTotal}
                />

                {/* Two-column layout — raw div, not Row (Row wraps children in extra divs that break flex) */}
                <div style={{ display: "flex", alignItems: "flex-start" }}>
                    {/* Left Column - three mirror axes + enemy defence + stability */}
                    <Column style={{
                        flex: 1,
                        minWidth: 0,
                        overflow: "hidden" as const,
                        borderRight: `2rem solid ${theme.colors.border}`,
                        paddingRight: "8rem",
                    }}>
                        {/* Enemy defence (intercept chance) */}
                        <Column gap="8rem" style={{ ...s.section, padding: "14rem" }}>
                            <div style={{
                                fontSize: "12rem",
                                fontWeight: 700,
                                color: accents.crisis.accent,
                                textTransform: "uppercase" as const,
                            }}>
                                {l.t("UI_GW_STANCE_EFFECTS")}
                            </div>
                            <StatRow
                                label={l.t("UI_GW_BLOCKS")}
                                value={`${interceptPercent}%`}
                                color={accents.crisis.accent}
                                compact
                                labelStyle={{ fontSize: "12rem", textTransform: "none" }}
                                valueStyle={{ fontSize: "12rem", fontWeight: 600 }}
                            />
                        </Column>

                        {/* Three mirror axes + Stability */}
                        <Column gap="8rem">
                            <AxisBar
                                labelText="Physical"
                                value={gw.EnemyPhysicalAxis}
                                accentColor={accents.crisis.accent}
                                suppressed={gw.RespitePhysicalActive}
                            />
                            <AxisBar
                                labelText="Digital"
                                value={gw.EnemyDigitalAxis}
                                accentColor={accents.operations.accent}
                                suppressed={gw.RespiteDigitalActive}
                            />
                            <AxisBar
                                labelText="Social"
                                value={gw.EnemySocialAxis}
                                accentColor={accents.schemes.accent}
                                suppressed={gw.RespiteSocialActive}
                            />
                            <ObjectiveProgressRow progress={gw.ObjectiveProgress} />
                            <StabilityBar
                                stability={gw.CityStability}
                                discount={gw.StabilityDiscount}
                            />
                        </Column>
                    </Column>

                    {/* Right Column - Intel Preview + Operation Slots */}
                    <Column gap="8rem" style={{
                        flex: 1,
                        minWidth: 0,
                        overflow: "hidden" as const,
                        paddingLeft: "8rem",
                    }}>
                        {/* Intel Preview */}
                        <IntelPreviewSlot />

                        {/* Operation Slots */}
                        <div style={s.section}>
                            <OperationSlots state={gw} actions={gridWarfareActions} />
                        </div>
                    </Column>
                </div>
            </Column>
        </div>
    );
});
GridWarfarePanelReady.displayName = "GridWarfarePanelReady";

const ObjectiveProgressRow = memo(({ progress }: { progress: number }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const pct = Math.round(Math.max(0, Math.min(1, progress)) * 100);
    return (
        <div style={{ padding: "6rem 0" }}>
            <StatRow
                label={l.t("UI_GW_OBJECTIVE_PROGRESS")}
                value={`${pct}%`}
                color={accents.operations.accent}
                compact
                labelStyle={{ fontSize: "11rem", textTransform: "none" }}
                valueStyle={{ fontSize: "11rem", fontWeight: 700 }}
            />
            <div style={{
                marginTop: "4rem",
                height: "4rem",
                borderRadius: "2rem",
                background: theme.colors.border,
                overflow: "hidden" as const,
            }}>
                <div style={{
                    width: `${pct}%`,
                    height: "100%",
                    background: accents.operations.accent,
                }} />
            </div>
        </div>
    );
});
ObjectiveProgressRow.displayName = "ObjectiveProgressRow";

const IntelPreviewSlot = memo(() => {
    const intelState = useIntel();
    const intelActions = useIntelActions();
    const intel = bindingDataOrDefault(intelState, DEFAULT_INTEL_DTO);
    // Act overlay: CanUpgradeIntel is local-facts-only; act-lock applied here.
    const currentAct = useSafeNumber(currentAct$, Act.PreWar, "currentAct");
    const isPreWar = currentAct < Act.Crisis;
    return (
        <IntelPreview
            intelLevel={intel.IntelUpgradeLevel}
            upgradeCost={intel.IntelUpgradeCost}
            canUpgrade={intel.CanUpgradeIntel && !isPreWar}
            lockedReasonId={intel.IntelUpgradeLockedReasonId}
            intelUpgradeRequest={intel.IntelUpgradeRequest}
            onUpgrade={intelActions.upgradeIntel}
        />
    );
});
IntelPreviewSlot.displayName = "IntelPreviewSlot";

export const GridWarfarePanelContent = GridWarfarePanelReady;

export const GridWarfarePanel = memo(() => {
    const gridState = useGridWarfareDomain();
    const gw = bindingDataOrDefault(gridState, DEFAULT_GRID_WARFARE_DTO);
    return (
        <GlassCase
            feature="GridWarfare"
            name="Grid Warfare"
            description="Offensive war room: strike the enemy's three axes (physical, digital, social), run shadow operations to weaken adversaries. Mod v2 — asymmetric attack mode."
        >
            <GridWarfarePanelReady gw={gw} />
        </GlassCase>
    );
});

GridWarfarePanel.displayName = "GridWarfarePanel";
