/**
 * WarRoomCityStatus — the right-column CITY STATUS block: a stability bar, the ops-cost discount it
 * grants, the current shock tier, weekly casualties, and the per-axis respite windows.
 *
 * Grid-warfare fields arrive as a prop (already read once by WarRoomContent); shock tier + weekly
 * casualties come from the attention singleton read here. Pure presentational reader — no triggers.
 * Flex-grows to fill the bottom of the column so there is no empty gap.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba } from "@themes";
import { useLocale, type TranslationKey } from "@locales";
import { type GridWarfareDto, useAttention, bindingDataOrDefault } from "@hooks/domain";
import { DEFAULT_ATTENTION_DTO } from "../../../../../types/domainDtos";

interface WarRoomCityStatusProps {
    gw: GridWarfareDto;
}

export const WarRoomCityStatus: React.FC<WarRoomCityStatusProps> = memo(({ gw }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const attention = bindingDataOrDefault(useAttention(), DEFAULT_ATTENTION_DTO);

    const stability = Math.max(0, Math.min(100, gw.CityStability ?? 0));
    const stabilityColor = stability >= 66 ? accents.schemes.accent
        : stability >= 33 ? accents.resilience.accent
        : accents.crisis.accent;
    const discountPct = Math.round((gw.StabilityDiscount ?? 0) * 100);

    const shockTier = attention.ShockTier;
    const shockKey: TranslationKey = shockTier === "GlobalShock" ? "UI_WARROOM_SHOCK_GLOBAL"
        : shockTier === "Headlines" ? "UI_WARROOM_SHOCK_HEADLINES"
        : "UI_WARROOM_SHOCK_DEEP";
    const shockColor = shockTier === "GlobalShock" ? accents.crisis.accent
        : shockTier === "Headlines" ? accents.resilience.accent
        : theme.colors.textSecondary;

    const respites = useMemo(() => {
        const rows: Array<{ axisKey: TranslationKey; hours: number }> = [];
        if (gw.RespitePhysicalActive) rows.push({ axisKey: "UI_WARROOM_AXIS_PHYSICAL", hours: gw.RespitePhysicalHoursLeft });
        if (gw.RespiteDigitalActive) rows.push({ axisKey: "UI_WARROOM_AXIS_DIGITAL", hours: gw.RespiteDigitalHoursLeft });
        if (gw.RespiteSocialActive) rows.push({ axisKey: "UI_WARROOM_AXIS_SOCIAL", hours: gw.RespiteSocialHoursLeft });
        return rows;
    }, [gw]);

    const s = useMemo(() => ({
        block: {
            display: "flex",
            flexDirection: "column" as const,
            flex: 1,
            minHeight: "60rem",
            marginTop: theme.spacing.xs,
            paddingTop: theme.spacing.xs,
            borderTop: `1rem solid ${theme.colors.border}`,
        } as React.CSSProperties,
        title: {
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            color: accents.schemes.accent,
            marginBottom: theme.spacing.sm,
            flexShrink: 0,
        } as React.CSSProperties,
        row: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: "2rem",
        } as React.CSSProperties,
        label: {
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
        } as React.CSSProperties,
        value: (color: string): React.CSSProperties => ({
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            color,
            fontFamily: mono,
        }),
        track: {
            height: "8rem",
            marginTop: "2rem",
            marginBottom: "5rem",
            borderRadius: "4rem",
            background: hexToRgba(theme.colors.textMuted, 0.18),
            overflow: "hidden" as const,
        } as React.CSSProperties,
        fill: (color: string, pct: number): React.CSSProperties => ({
            width: `${pct}%`,
            height: "100%",
            background: color,
            boxShadow: `0 0 6rem ${hexToRgba(color, 0.5)}`,
        }),
        respiteRow: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginTop: "3rem",
        } as React.CSSProperties,
        empty: {
            fontSize: "10rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
            marginTop: "4rem",
        } as React.CSSProperties,
    }), [theme, accents, mono]);

    return (
        <div style={s.block}>
            <div style={s.title}>{l.t("UI_WARROOM_CITY_STATUS")}</div>

            <div style={s.row}>
                <span style={s.label}>{l.t("UI_WARROOM_CITY_STABILITY")}</span>
                <span style={s.value(stabilityColor)}>{`${Math.round(stability)}%`}</span>
            </div>
            <div style={s.track}>
                <div style={s.fill(stabilityColor, stability)} />
            </div>

            <div style={s.row}>
                <span style={s.label}>{l.t("UI_WARROOM_CITY_DISCOUNT")}</span>
                <span style={s.value(accents.schemes.accent)}>{`-${discountPct}%`}</span>
            </div>
            <div style={s.row}>
                <span style={s.label}>{l.t("UI_WARROOM_CITY_SHOCK")}</span>
                <span style={s.value(shockColor)}>{l.t(shockKey)}</span>
            </div>
            <div style={s.row}>
                <span style={s.label}>{l.t("UI_WARROOM_CITY_CASUALTIES")}</span>
                <span style={s.value(attention.CasualtiesThisWeek > 0 ? accents.crisis.accent : theme.colors.textMuted)}>
                    {`+${attention.CasualtiesThisWeek}`}
                </span>
            </div>

            <div style={{ ...s.label, marginTop: "4rem", marginBottom: "2rem" }}>{l.t("UI_WARROOM_RESPITE")}</div>
            {respites.length === 0 ? (
                <span style={s.empty}>{l.t("UI_WARROOM_RESPITE_NONE")}</span>
            ) : (
                respites.map((r) => (
                    <div key={r.axisKey} style={s.respiteRow}>
                        <span style={s.label}>{l.t(r.axisKey)}</span>
                        <span style={s.value(accents.schemes.accent)}>
                            {`${l.t("UI_WARROOM_GAUGE_REGROUP")} ${Math.max(1, Math.round(r.hours))}h`}
                        </span>
                    </div>
                ))
            )}
        </div>
    );
});

WarRoomCityStatus.displayName = "WarRoomCityStatus";
