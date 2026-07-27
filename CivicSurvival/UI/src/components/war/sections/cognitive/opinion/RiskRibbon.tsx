/**
 * RiskRibbon — "why fight for minds": a verdict pill + the STAKES the city is
 * losing (commerce / happiness penalties, exodus, protest, the network mode),
 * all from LIVE data. It sits atop the IPSO screen above the enemy picture.
 *
 * Role split with the STATE-OF-THE-INFO-WAR tiles below it: this band carries the
 * stakes, the tiles carry the info-war damage (media trust, infection, food panic,
 * integrity). Nothing appears in both — the food-panic run used to be a chip here
 * AND a tile, and protest/commerce were duplicated outright, which is what pushed
 * the screen past the panel height.
 */

import React, { memo, useMemo } from "react";
import { Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import {
    bindingDataOrDefault, isBindingLive, useCognitive, useAttention,
    InternetMode, ProtestRisk, getProtestRiskLabelKey,
} from "@hooks/domain";
import { districts$, isDistrictData } from "@hooks/bindings/coreBindings";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { DEFAULT_COGNITIVE_DTO, DEFAULT_ATTENTION_DTO } from "../../../../../types/domainDtos";

interface QuietMetric {
    key: string;
    label: string;
    value: string;
    color: string;
}

interface LiveMetricInputs {
    commercePct: number;
    happinessPct: number;
    exodusActive: boolean;
    protest: number;
}

// Live readouts, extracted so the component body stays a flat "live or
// awaiting" switch (and under the cognitive-complexity lint budget).
const buildLiveMetrics = (
    m: LiveMetricInputs,
    l: ReturnType<typeof useLocale>,
    theme: ReturnType<typeof useTheme>,
    accents: ReturnType<typeof useAccents>,
): QuietMetric[] => [
    {
        key: "commerce",
        label: l.t("UI_OB_M_COMMERCE"),
        value: m.commercePct > 0 ? `-${m.commercePct}%` : "0%",
        color: m.commercePct > 0 ? theme.colors.error : theme.colors.textMuted,
    },
    {
        key: "happiness",
        label: l.t("UI_OB_M_HAPPINESS"),
        value: m.happinessPct > 0 ? `-${m.happinessPct}%` : "0%",
        color: m.happinessPct > 0 ? accents.resilience.accent : theme.colors.textMuted,
    },
    {
        key: "exodus",
        label: l.t("UI_OB_M_EXODUS"),
        value: m.exodusActive ? l.t("STATUS_ACTIVE") : l.t("UI_CW_STABLE"),
        color: m.exodusActive ? theme.colors.error : theme.colors.success,
    },
    {
        key: "protest",
        label: l.t("UI_OB_M_PROTEST"),
        value: l.t(getProtestRiskLabelKey(m.protest)),
        color: m.protest >= ProtestRisk.High ? theme.colors.error
            : m.protest >= ProtestRisk.Medium ? accents.resilience.accent
                : theme.colors.success,
    },
];

const liveVerdictOf = (
    avgIntegrity: number,
    theme: ReturnType<typeof useTheme>,
    accents: ReturnType<typeof useAccents>,
): { labelKey: "UI_OB_VERDICT_STABLE" | "UI_OB_VERDICT_STRAINED" | "UI_OB_VERDICT_CRITICAL"; color: string } =>
    avgIntegrity >= 0.7
        ? { labelKey: "UI_OB_VERDICT_STABLE", color: theme.colors.success }
        : avgIntegrity >= 0.4
            ? { labelKey: "UI_OB_VERDICT_STRAINED", color: accents.resilience.accent }
            : { labelKey: "UI_OB_VERDICT_CRITICAL", color: theme.colors.error };

const liveInternetLabelOf = (mode: number, l: ReturnType<typeof useLocale>): string =>
    mode === InternetMode.Blackout ? l.t("UI_NET_BLACKOUT")
        : mode === InternetMode.Firewall ? l.t("UI_NET_FIREWALL")
            : l.t("UI_NET_OPEN");

export const RiskRibbon = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const cwState = useCognitive();
    const attentionState = useAttention();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    const attention = bindingDataOrDefault(attentionState, DEFAULT_ATTENTION_DTO);
    // Until BOTH feeds have delivered, every number below is a locally
    // constructed default (AvgIntegrity 0 reads as city collapse, ExodusActive
    // false reads as green STABLE). Render the same geometry in a muted
    // "awaiting data" posture instead of a fake verdict.
    const live = isBindingLive(cwState) && isBindingLive(attentionState);
    const rawDistricts = useSafeJsonArray(districts$, [], "districts");

    // Worst-district happiness hit, not a citywide average: a strike on a few
    // districts must not be diluted to a green "STABLE" by the untouched majority
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

    const verdict = live
        ? liveVerdictOf(cw.AvgIntegrity, theme, accents)
        : { labelKey: "UI_NO_DATA" as const, color: theme.colors.textMuted };

    const liveMetrics = buildLiveMetrics({
        commercePct: Math.round((cw.CommercePenalty ?? 0) * 100),
        happinessPct: Math.round(happinessPenaltyPeak * 100),
        exodusActive: attention.ExodusActive,
        protest: cw.ProtestRisk,
    }, l, theme, accents);
    // Awaiting posture: same rows, muted em-dash values — the geometry must not
    // jump when the feed arrives, and no colour may claim a verdict yet.
    const metrics: QuietMetric[] = live
        ? liveMetrics
        : liveMetrics.map((m) => ({ ...m, value: "—", color: theme.colors.textMuted }));

    const internetLabel = live ? liveInternetLabelOf(cw.InternetMode, l) : "—";

    return (
        // ONE wrapping row, no space-between: the verdict is simply the first item in
        // the flow. Splitting it from a flex:1 metric block pushed the pill down and
        // broke the band into two lines as soon as the metrics wrapped.
        <Row align="center" wrap="wrap" gap={theme.spacing.md} style={{
            width: "100%",
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            backgroundColor: theme.colors.surface,
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadiusLg,
        }}>
            <span style={{
                padding: "3rem 10rem",
                fontSize: theme.typography.sizeXS,
                fontWeight: 800,
                fontFamily: theme.typography.fontFamilyMono,
                color: verdict.color,
                backgroundColor: hexToRgba(verdict.color, 0.14),
                borderRadius: theme.layout.borderRadius,
                textTransform: "uppercase",
                letterSpacing: "0.6rem",
            }}>
                {l.t(verdict.labelKey)}
            </span>

            {/* Metrics are direct children of the band — no flex:1 wrapper row, which is
                what let them wrap away from the verdict. The food-panic run is NOT here:
                it is a STATE tile below (it needs the % and the red flip). */}
            {metrics.map((m) => (
                <Row key={m.key} align="center">
                    <div style={{
                        width: "6rem", height: "6rem", borderRadius: "3rem",
                        backgroundColor: m.color, marginRight: "5rem",
                    }} />
                    <span style={{
                        fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono,
                        color: theme.colors.textMuted, textTransform: "uppercase", letterSpacing: "0.4rem",
                        marginRight: "5rem",
                    }}>{m.label}</span>
                    <span style={{
                        fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono,
                        fontWeight: 700, color: m.color,
                    }}>{m.value}</span>
                </Row>
            ))}

            {/* Same shape as the metrics beside it — dot + label + value. */}
            <Row align="center">
                <div style={{
                    width: "6rem", height: "6rem", borderRadius: "3rem",
                    backgroundColor: theme.colors.textMuted, marginRight: "5rem",
                }} />
                <span style={{
                    fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono,
                    color: theme.colors.textMuted, textTransform: "uppercase", letterSpacing: "0.4rem",
                    marginRight: "5rem",
                }}>{l.t("UI_OB_M_NETWORK")}</span>
                <span style={{
                    fontSize: theme.typography.sizeXS, fontFamily: theme.typography.fontFamilyMono,
                    fontWeight: 700, color: theme.colors.textSecondary,
                }}>{internetLabel}</span>
            </Row>
        </Row>
    );
});

RiskRibbon.displayName = "RiskRibbon";
