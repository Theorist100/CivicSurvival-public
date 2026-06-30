import React from "react";
import { Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { bindingDataOrDefault, useAttention } from "@hooks/domain";
import { DEFAULT_ATTENTION_DTO } from "../../../../../types/domainDtos";
import { IconAlert } from "@shared/common/Icons";
import { StatRow } from "../../../../shared/ui";
import { styles } from "../CognitiveInfoSection.styles";

export const ExodusSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const attentionState = useAttention();
    const attention = bindingDataOrDefault(attentionState, DEFAULT_ATTENTION_DTO);

    const exodusActive = attention.ExodusActive;
    const exodusRatePercentPerDay = attention.ExodusRatePercentPerDay;
    const totalExodus = attention.TotalExodus;

    const statusColor = exodusActive ? accents.crisis.accent : theme.colors.success;

    if (!exodusActive) {
        return (
            <div style={styles.statusBox(theme)}>
                <StatRow
                    compact
                    label={l.t("UI_CW_STATUS")}
                    value={(
                        <Row align="center">
                        <span style={{
                            fontSize: "11rem",
                            fontWeight: 700,
                            color: statusColor,
                        }}>
                            {l.t("UI_CW_STABLE")}
                        </span>
                        {totalExodus > 0 && (
                            <span style={{
                                fontSize: "10rem",
                                color: theme.colors.textMuted,
                                marginLeft: theme.spacing.sm,
                            }}>
                                | {l.t("UI_CW_LEFT")} {totalExodus.toLocaleString()}
                            </span>
                        )}
                        </Row>
                    )}
                    color={statusColor}
                    valueStyle={{ fontSize: "11rem", fontWeight: 700 }}
                />
            </div>
        );
    }

    return (
        <div style={styles.statusBox(theme)}>
            <StatRow
                compact
                label={l.t("UI_CW_STATUS")}
                value={<><span style={{ marginRight: "4rem" }}><IconAlert /></span>{l.t("STATUS_ACTIVE")}</>}
                color={statusColor}
                valueStyle={{ display: "flex", alignItems: "center", fontSize: "11rem", fontWeight: 700 }}
            />
            <StatRow
                compact
                label={l.t("UI_CW_RATE")}
                value={`${exodusRatePercentPerDay.toFixed(1)}%${l.t("UI_UNIT_PER_DAY")}`}
                color={accents.crisis.accent}
                style={{ marginTop: theme.spacing.xs }}
                valueStyle={{ fontSize: "12rem", fontWeight: 600 }}
            />
            <StatRow
                compact
                label={l.t("UI_CW_TOTAL_LEFT")}
                value={totalExodus.toLocaleString()}
                color={accents.crisis.accent}
                style={{ marginTop: theme.spacing.xs }}
                valueStyle={{ fontSize: "12rem", fontWeight: 600 }}
            />
        </div>
    );
};
