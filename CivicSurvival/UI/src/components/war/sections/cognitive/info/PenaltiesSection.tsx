import React, { useMemo } from "react";
import { HoverTip } from "../../../../shared/common/HoverTip";
import { Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { InternetMode, bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { districts$, isDistrictData } from "@hooks/bindings/coreBindings";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { IconGlobe, IconLightning, IconShield } from "@shared/common/Icons";
import { StatRow } from "../../../../shared/ui";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { styles } from "../CognitiveInfoSection.styles";

export const PenaltiesSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const rawDistricts = useSafeJsonArray(districts$, [], "districts");
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    const currentMode = cw.InternetMode;
    const commercePenalty = cw.CommercePenalty;

    const happinessPenaltyTotal = useMemo(() => {
        const districts = Array.isArray(rawDistricts) ? rawDistricts.filter(isDistrictData) : [];
        if (districts.length === 0) return 0;

        let totalPenaltySum = 0;

        for (const d of districts) {
            totalPenaltySum += d.TotalHappinessPenalty ?? 0;
        }

        return totalPenaltySum / districts.length;
    }, [rawDistricts]);

    const modeLabel = useMemo(() => {
        switch (currentMode) {
            case InternetMode.Firewall: return "FIREWALL";
            case InternetMode.Blackout: return "BLACKOUT";
            default: return "OPEN";
        }
    }, [currentMode]);

    const modeColor = useMemo(() => {
        switch (currentMode) {
            case InternetMode.Firewall: return accents.resilience.accent;
            case InternetMode.Blackout: return accents.crisis.accent;
            default: return theme.colors.success;
        }
    }, [currentMode, accents, theme]);

    const modeIcon = useMemo(() => {
        switch (currentMode) {
            case InternetMode.Firewall: return <IconShield />;
            case InternetMode.Blackout: return <IconLightning />;
            default: return <IconGlobe />;
        }
    }, [currentMode]);

    const commercePenaltyPct = Math.round(commercePenalty * 100);
    const hasCommercePenalty = commercePenaltyPct > 0;
    const totalHappinessPct = Math.round(happinessPenaltyTotal * 100);
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
                value={hasCommercePenalty ? `-${commercePenaltyPct}%` : "0"}
                color={hasCommercePenalty ? accents.crisis.accent : theme.colors.textMuted}
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
                    color: hasHappinessPenalty ? accents.crisis.accent : theme.colors.success,
                }}>
                    {hasHappinessPenalty ? `-${totalHappinessPct}%` : l.t("UI_CW_OK")}
                </span>
            </Row>
        </div>
    );
};
