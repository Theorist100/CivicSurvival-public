import React, { memo, useCallback, useMemo } from "react";
import { Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { type CognitiveDistrictDto, toggleDistrictInternet } from "@hooks/bindings/coreBindings";
import { InternetMode } from "@hooks/domain";
import { type DistrictIndex } from "../../../../../types/semantic";
import { IconWifi, IconWifiOff } from "../../../../shared/common/Icons";
import { HoverTipTarget } from "../../../../shared/common/HoverTip";
import { ProgressBar } from "../../../../shared/ui";

interface DistrictRowProps {
    district: CognitiveDistrictDto;
    globalMode: number;
    districtIndex: DistrictIndex;
    disabled?: boolean;
}

export const DistrictRow: React.FC<DistrictRowProps> = memo(({ district, globalMode, districtIndex, disabled = false }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const integrityPct = Math.round(district.Integrity * 100);

    const barColor = useMemo(() => {
        if (district.Integrity >= 0.7) return theme.colors.success;
        if (district.Integrity >= 0.5) return accents.resilience.accent;
        if (district.Integrity >= 0.3) return accents.crisis.accent;
        return theme.colors.error;
    }, [district.Integrity, theme, accents]);

    const isGlobalBlackout = globalMode === InternetMode.Blackout;
    const isGlobalFirewall = globalMode === InternetMode.Firewall;
    const canToggle = !disabled && globalMode === InternetMode.Open;

    const handleToggle = useCallback(() => {
        toggleDistrictInternet(districtIndex);
    }, [districtIndex]);

    const s = useMemo(() => ({
        districtRow: {
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            borderBottom: `2rem solid ${theme.colors.border}`,
            borderLeft: district.IsCompromised ? `3rem solid ${accents.crisis.accent}` : "none",
        } as React.CSSProperties,

        nameCol: {
            flex: 1,
            minWidth: 0,
        } as React.CSSProperties,

        districtName: {
            fontSize: "11rem",
            fontWeight: 600,
            color: theme.colors.textPrimary,
            whiteSpace: "nowrap" as const,
            overflow: "hidden" as const,
            textOverflow: "ellipsis",
        } as React.CSSProperties,

        integrityValue: {
            fontSize: "11rem",
            fontWeight: 600,
            fontFamily: theme.typography.fontFamilyMono,
            color: barColor,
            minWidth: "36rem",
            textAlign: "right" as const,
        } as React.CSSProperties,

        isolateButton: (isIsolated: boolean, disabled: boolean) => ({
            padding: "4rem 8rem",
            fontSize: "10rem",
            fontWeight: 600,
            border: `2rem solid ${isIsolated ? accents.crisis.accent : theme.colors.border}`,
            borderRadius: "4rem",
            backgroundColor: isIsolated ? hexToRgba(accents.crisis.accent, 0.12) : "transparent",
            color: disabled ? theme.colors.textMuted : isIsolated ? accents.crisis.accent : theme.colors.textSecondary,
            cursor: disabled ? "not-allowed" : "pointer",
            opacity: disabled ? 0.5 : 1,
            marginLeft: theme.spacing.sm,
            display: "flex",
            alignItems: "center",
        }) as React.CSSProperties,
    }), [theme, accents, barColor, district.IsCompromised]);

    const isIsolated = !district.HasInternet;

    return (
        <Row align="center" style={s.districtRow}>
            <div style={s.nameCol}>
                <span style={s.districtName}>{district.Name || l.t("UI_CW_DISTRICT_FALLBACK", district.DistrictIndex)}</span>
            </div>
            <ProgressBar
                value={integrityPct}
                color={barColor}
                height="8rem"
                style={{
                    flex: 2,
                    backgroundColor: theme.colors.surface,
                    marginLeft: theme.spacing.sm,
                    marginRight: theme.spacing.sm,
                }}
            />
            <span style={s.integrityValue}>{integrityPct}%</span>
            <HoverTipTarget text={isGlobalBlackout ? l.t("UI_CW_BLACKOUT_ACTIVE") : isGlobalFirewall ? l.t("UI_NET_FIREWALL") : isIsolated ? l.t("UI_CW_ENABLE_INTERNET") : l.t("UI_CW_DISABLE_INTERNET")}>
                <button
                    style={s.isolateButton(isIsolated, !canToggle)}
                    onClick={canToggle ? handleToggle : undefined}
                    disabled={!canToggle}
                >
                    {isIsolated ? <IconWifiOff /> : <IconWifi />}
                </button>
            </HoverTipTarget>
        </Row>
    );
});

DistrictRow.displayName = "DistrictRow";
