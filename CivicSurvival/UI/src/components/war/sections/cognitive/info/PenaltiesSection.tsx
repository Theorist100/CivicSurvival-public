import React, { useMemo } from "react";
import { HoverTip } from "../../../../shared/common/HoverTip";
import { Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { InternetMode, bindingDataOrDefault, isBindingLive, useCognitive } from "@hooks/domain";
import { districts$, isDistrictData } from "@hooks/bindings/coreBindings";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { IconGlobe, IconLightning, IconShield } from "@shared/common/Icons";
import { StatRow } from "../../../../shared/ui";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { styles } from "./infoSections.styles";

export const PenaltiesSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const rawDistricts = useSafeJsonArray(districts$, [], "districts");
    const cwState = useCognitive();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    // Defaults here claim a healthy city (green OPEN mode, green happiness OK)
    // before the cognitive feed delivers — keep those verdicts muted until live.
    const live = isBindingLive(cwState);

    const currentMode = cw.InternetMode;
    const commercePenalty = cw.CommercePenalty;

    // Worst-district happiness hit, not a citywide average: a strike on a few
    // districts must not be diluted to a green "OK" by the untouched majority
    // (mirrors the sim-side MaxHappinessPenalty peak).
    const happinessPenaltyPeak = useMemo(() => {
        const districts = Array.isArray(rawDistricts) ? rawDistricts.filter(isDistrictData) : [];
        if (districts.length === 0) return 0;

        let peak = 0;

        for (const d of districts) {
            const penalty = d.TotalHappinessPenalty ?? 0;
            if (penalty > peak) peak = penalty;
        }

        return peak;
    }, [rawDistricts]);

    const modeLabel = useMemo(() => {
        if (!live) return "—";
        switch (currentMode) {
            case InternetMode.Firewall: return l.t("UI_NET_FIREWALL");
            case InternetMode.Blackout: return l.t("UI_NET_BLACKOUT");
            default: return l.t("UI_NET_OPEN");
        }
    }, [live, currentMode, l]);

    const modeColor = useMemo(() => {
        if (!live) return theme.colors.textMuted;
        switch (currentMode) {
            case InternetMode.Firewall: return accents.resilience.accent;
            case InternetMode.Blackout: return accents.crisis.accent;
            default: return theme.colors.success;
        }
    }, [live, currentMode, accents, theme]);

    const modeIcon = useMemo(() => {
        switch (currentMode) {
            case InternetMode.Firewall: return <IconShield />;
            case InternetMode.Blackout: return <IconLightning />;
            default: return <IconGlobe />;
        }
    }, [currentMode]);

    const commercePenaltyPct = Math.round(commercePenalty * 100);
    const hasCommercePenalty = commercePenaltyPct > 0;
    const totalHappinessPct = Math.round(happinessPenaltyPeak * 100);
    const hasHappinessPenalty = totalHappinessPct > 0;

    return (
        <div style={styles.statusBox(theme)}>
            <Row justify="space-between" align="center" style={{ marginBottom: theme.spacing.xs }}>
                <HoverTip text={l.t("TIP_CW_COMMERCE")} style={{ ...styles.miniLabel(theme), fontWeight: 700 }}>
                    {l.t("UI_CW_COMMERCE")}
                </HoverTip>
                <Row align="center">
                    <span style={{ color: modeColor, fontSize: "10rem", marginRight: "4rem" }}>
                        {modeIcon}
                    </span>
                    <span style={{ fontSize: "10rem", color: modeColor, fontWeight: 600 }}>
                        {modeLabel}
                    </span>
                </Row>
            </Row>

            <StatRow
                compact
                label={l.t("UI_CW_INTERNET_MODE")}
                value={!live ? "—" : hasCommercePenalty ? `-${commercePenaltyPct}%` : "0"}
                color={live && hasCommercePenalty ? accents.crisis.accent : theme.colors.textMuted}
                valueStyle={{ fontWeight: 600, fontSize: "12rem" }}
            />

            <div style={styles.divider(theme)} />

            <Row justify="space-between" align="center" style={{ marginBottom: theme.spacing.xs }}>
                <HoverTip text={l.t("TIP_CW_HAPPINESS")} style={{ ...styles.miniLabel(theme), fontWeight: 700 }}>
                    {l.t("UI_CW_HAPPINESS")}
                </HoverTip>
                <span style={{
                    fontSize: "11rem",
                    fontWeight: 700,
                    fontFamily: theme.typography.fontFamilyMono,
                    color: !live ? theme.colors.textMuted
                        : hasHappinessPenalty ? accents.crisis.accent : theme.colors.success,
                }}>
                    {!live ? "—" : hasHappinessPenalty ? `-${totalHappinessPct}%` : l.t("UI_CW_OK")}
                </span>
            </Row>
        </div>
    );
};
