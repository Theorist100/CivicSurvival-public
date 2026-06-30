/**
 * GlobalNetSection - Zone 1 of Cognitive Warfare right panel
 * Global internet mode selector: OPEN / FIREWALL / BLACKOUT
 */

import React, { memo, useMemo, useRef } from "react";
import { Row, Column } from "../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../themes";
import { useLocale } from "../../../../locales";
import { bindingDataOrDefault, useCognitive, InternetMode, type InternetModeType } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../types/domainDtos";
import { IconGlobe, IconShield, IconLightning } from "../../../shared/common/Icons";
import { StatRow } from "../../../shared/ui";
import { useRequestAction } from "@hooks/actions";
import { type useCognitiveActions } from "@hooks/actions";

interface ModeButtonProps {
    mode: number;
    currentMode: number;
    label: string;
    icon: React.ReactNode;
    color: string;
    onClick: () => void;
    disabled?: boolean;
}

const ModeButton: React.FC<ModeButtonProps> = memo(({
    mode,
    currentMode,
    label,
    icon,
    color,
    onClick,
    disabled = false,
}) => {
    const theme = useTheme();
    const isActive = mode === currentMode;

    const style: React.CSSProperties = {
        flex: 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "8rem 12rem",
        fontSize: "11rem",
        fontWeight: 700,
        textTransform: "uppercase",
        border: isActive ? `3rem solid ${color}` : `2rem solid ${theme.colors.border}`,
        borderRadius: "4rem",
        backgroundColor: isActive ? hexToRgba(color, 0.12) : "transparent",
        color: isActive ? color : theme.colors.textMuted,
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.55 : 1,
        transition: "color 0.15s ease, background-color 0.15s ease, border-color 0.15s ease",
    };

    const iconStyle: React.CSSProperties = {
        marginRight: "6rem",
    };

    return (
        <button style={style} disabled={disabled} onClick={onClick}>
            <span style={iconStyle}>{icon}</span>
            {label}
        </button>
    );
});

ModeButton.displayName = "ModeButton";

interface GlobalNetSectionProps {
    actions: ReturnType<typeof useCognitiveActions>;
    disabled?: boolean;
}

export const GlobalNetSection = memo(({ actions, disabled = false }: GlobalNetSectionProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const currentMode = cw?.InternetMode ?? InternetMode.Open;
    const infectionRate = cw?.InfectionRate ?? 0;
    const recoveryRate = cw?.RecoveryRate ?? 0;
    const penalty = cw?.CommercePenalty ?? 0;
    const internetModeRef = useRef<InternetModeType>(InternetMode.Open);
    const internetModeAction = useRequestAction(() => {
        actions.setInternetMode(internetModeRef.current);
        return true;
    }, cw?.InternetModeRequest);
    const internetModePending = internetModeAction.isPending || cw?.InternetModeRequest.Status === "pending";
    const internetModeError =
        cw?.InternetModeRequest.Status === "failed" && cw.InternetModeRequest.ReasonId
            ? cw.InternetModeRequest.ReasonId
            : "";

    const s = useMemo(() => ({
        container: {
            width: "100%",
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.paper,
            borderBottom: `2rem solid ${theme.colors.border}`,
        } as React.CSSProperties,

        title: {
            fontSize: "10rem",
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            marginBottom: theme.spacing.xs,
        } as React.CSSProperties,

        buttonsRow: {
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,

        statsRow: {
            fontSize: "11rem",
            color: theme.colors.textSecondary,
        } as React.CSSProperties,

        separator: {
            margin: "0 8rem",
            color: theme.colors.border,
        } as React.CSSProperties,

        requestHint: {
            marginTop: theme.spacing.xs,
            fontSize: "10rem",
            color: theme.colors.textMuted,
            textAlign: "center" as const,
        } as React.CSSProperties,
    }), [theme]);

    // Format rate as percentage per hour
    const formatRate = (rate: number) => {
        const pctPerHour = rate * 100;
        if (pctPerHour === 0) return "0%";
        return `${pctPerHour > 0 ? "+" : ""}${pctPerHour.toFixed(1)}%${l.t("UI_UNIT_PER_HR")}`;
    };

    // Determine colors based on values
    const infectionColor = infectionRate > 0 ? accents.crisis.accent : theme.colors.textMuted;
    const recoveryColor = recoveryRate > 0 ? theme.colors.success : theme.colors.textMuted;
    const penaltyColor = penalty > 0 ? accents.resilience.accent : theme.colors.textMuted;
    const setMode = (mode: InternetModeType) => {
        if (disabled || internetModePending) return;
        internetModeRef.current = mode;
        internetModeAction.execute();
    };

    return (
        <Column style={s.container}>
            <div style={s.title}>{l.t("UI_CW_NETWORK_MODE")}</div>

            <Row gap={theme.spacing.xs} style={s.buttonsRow}>
                <ModeButton
                    mode={InternetMode.Open}
                    currentMode={currentMode}
                    label={l.t("UI_NET_OPEN")}
                    icon={<IconGlobe />}
                    color={theme.colors.success}
                    disabled={disabled || internetModePending}
                    onClick={() => setMode(InternetMode.Open)}
                />
                <ModeButton
                    mode={InternetMode.Firewall}
                    currentMode={currentMode}
                    label={l.t("UI_NET_FIREWALL")}
                    icon={<IconShield />}
                    color={accents.resilience.accent}
                    disabled={disabled || internetModePending}
                    onClick={() => setMode(InternetMode.Firewall)}
                />
                <ModeButton
                    mode={InternetMode.Blackout}
                    currentMode={currentMode}
                    label={l.t("UI_NET_BLACKOUT")}
                    icon={<IconLightning />}
                    color={accents.crisis.accent}
                    disabled={disabled || internetModePending}
                    onClick={() => setMode(InternetMode.Blackout)}
                />
            </Row>
            {(internetModePending || internetModeError) && (
                <div style={s.requestHint}>
                    {internetModePending ? l.t("UI_PROCESSING") : l.tDynamic(internetModeError)}
                </div>
            )}

            <Row justify="center" style={s.statsRow}>
                <StatRow
                    compact
                    label={l.t("UI_CW_INFECTION")}
                    value={formatRate(infectionRate)}
                    color={infectionColor}
                    labelStyle={{ minWidth: 0, textTransform: "none", marginRight: "4rem" }}
                />
                <span style={s.separator}>|</span>
                <StatRow
                    compact
                    label={l.t("UI_CW_RECOVERY")}
                    value={formatRate(recoveryRate)}
                    color={recoveryColor}
                    labelStyle={{ minWidth: 0, textTransform: "none", marginRight: "4rem" }}
                />
                {penalty > 0 && (
                    <>
                        <span style={s.separator}>|</span>
                        <StatRow
                            compact
                            label={l.t("UI_CW_COMMERCE_STAT")}
                            value={`-${Math.round(penalty * 100)}%`}
                            color={penaltyColor}
                            labelStyle={{ minWidth: 0, textTransform: "none", marginRight: "4rem" }}
                        />
                    </>
                )}
            </Row>
        </Column>
    );
});

GlobalNetSection.displayName = "GlobalNetSection";
