import React from "react";
import { Row } from "@coherent";
import { useTheme, useAccents, formatMoney } from "@themes";
import { useLocale } from "@locales";
import { bindingDataOrDefault, isBindingLive, useAttention } from "@hooks/domain";
import { DEFAULT_ATTENTION_DTO } from "../../../../../types/domainDtos";
import { IconAlert } from "@shared/common/Icons";
import { StatRow } from "../../../../shared/ui";
import { styles } from "./infoSections.styles";

export const ExodusSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const attentionState = useAttention();
    const attention = bindingDataOrDefault(attentionState, DEFAULT_ATTENTION_DTO);
    // Default ExodusActive=false would claim a green STABLE city before the
    // attention feed delivers — show a muted "no data" status until it is live.
    const live = isBindingLive(attentionState);

    const exodusActive = attention.ExodusActive;
    const exodusRatePercentPerDay = attention.ExodusRatePercentPerDay;
    const psyRatePercentPerDay = attention.PsyRatePercentPerDay;
    const kineticRatePercentPerDay = attention.KineticRatePercentPerDay;
    const totalExodus = attention.TotalExodus;
    const monacoFamilies = attention.MonacoFamiliesFled;
    const monacoCapital = attention.MonacoCapitalFled;

    const statusColor = !live ? theme.colors.textMuted
        : exodusActive ? accents.crisis.accent : theme.colors.success;

    // "Batalion Monako" — the swayed-wealthy capital-flight tell. Persistent once any family has
    // fled: stays on the board after the crisis passes (rendered in both the STABLE and ACTIVE
    // branches, like the totalExodus tail).
    const monacoRow = monacoFamilies > 0 ? (
        <StatRow
            compact
            label={l.t("UI_CW_MONACO")}
            value={`${monacoFamilies.toLocaleString()} · ${formatMoney(monacoCapital)}`}
            color={accents.crisis.accent}
            style={{ marginTop: theme.spacing.xs }}
            valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
        />
    ) : null;

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
                            {live ? l.t("UI_CW_STABLE") : l.t("UI_NO_DATA")}
                        </span>
                        {totalExodus > 0 && (
                            <span style={{
                                fontSize: "10rem",
                                color: theme.colors.textMuted,
                                marginLeft: theme.spacing.sm,
                            }}>
                                {`| ${l.t("UI_CW_LEFT")} ${totalExodus.toLocaleString()}`}
                            </span>
                        )}
                        </Row>
                    )}
                    color={statusColor}
                    valueStyle={{ fontSize: "11rem", fontWeight: 700 }}
                />
                {monacoRow}
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
                label={l.t("UI_CW_EXODUS_DRIVERS")}
                value={`${l.t("UI_CW_EXODUS_DRIVER_PSY")} ${psyRatePercentPerDay.toFixed(1)}%${l.t("UI_UNIT_PER_DAY")} · ${l.t("UI_CW_EXODUS_DRIVER_KINETIC")} ${kineticRatePercentPerDay.toFixed(1)}%${l.t("UI_UNIT_PER_DAY")}`}
                color={theme.colors.textMuted}
                style={{ marginTop: theme.spacing.xs }}
                valueStyle={{ fontSize: "10rem", fontWeight: 500 }}
            />
            <StatRow
                compact
                label={l.t("UI_CW_TOTAL_LEFT")}
                value={totalExodus.toLocaleString()}
                color={accents.crisis.accent}
                style={{ marginTop: theme.spacing.xs }}
                valueStyle={{ fontSize: "12rem", fontWeight: 600 }}
            />
            {monacoRow}
        </div>
    );
};
