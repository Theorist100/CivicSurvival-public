/**
 * WarRoomAxisChips — the three compact enemy-axis chips that live in the War Room header
 * (replacing the old full-width AxisBar strip). One chip per axis: axis name in the axis colour,
 * the current value on the right, a thin 4rem health bar, and a sub-row pairing the strike-type
 * caption with an intel-confidence tag. When an axis is in its post-floor respite window the chip
 * carries a REGROUP timer.
 *
 * Value precision is intel-gated exactly like the old strip: coarse Lv0 reads show "~NN%" with a
 * LOW CONFIDENCE tag; Lv1+ reads show the exact number with a CONFIRMED tag. Flex-only, mono-typed.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba, type Accents, type Theme } from "@themes";
import { useLocale, type TranslationKey } from "@locales";
import { type GridWarfareDto } from "@hooks/domain";

interface AxisChipModel {
    axisKey: TranslationKey;
    strikeKey: TranslationKey;
    color: string;
    value: number;
    respiteActive: boolean;
    respiteHoursLeft: number;
}

const createChipStyles = (theme: Theme) => {
    const mono = theme.typography.fontFamilyMono;
    return {
        // stretch mode (dashboard tab): the three chips split the full row width so no dead
        // fourth slot is left; compact mode (overlay header) keeps the fixed 196rem chips.
        chip: (color: string, stretch: boolean, isLast: boolean): React.CSSProperties => ({
            display: "flex",
            flexDirection: "column",
            minHeight: "50rem",
            ...(stretch
                ? { flex: 1, minWidth: 0 }
                : { width: "196rem", flexShrink: 0 }),
            padding: "6rem 10rem",
            marginRight: isLast && stretch ? "0rem" : "8rem",
            borderRadius: theme.layout.borderRadiusLg,
            background: hexToRgba(color, 0.08),
            border: `1rem solid ${hexToRgba(color, 0.4)}`,
        }),
        topRow: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
        } as React.CSSProperties,
        axisName: (color: string): React.CSSProperties => ({
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            letterSpacing: "1rem",
            textTransform: "uppercase",
            color,
            fontFamily: mono,
        }),
        value: (color: string): React.CSSProperties => ({
            fontSize: "15rem",
            fontWeight: theme.typography.weightBold,
            lineHeight: 1,
            color,
            fontFamily: mono,
        }),
        track: {
            height: "4rem",
            marginTop: "5rem",
            borderRadius: "2rem",
            background: hexToRgba(theme.colors.textMuted, 0.18),
            overflow: "hidden" as const,
        } as React.CSSProperties,
        fill: (color: string, pct: number): React.CSSProperties => ({
            width: `${pct}%`,
            height: "100%",
            background: color,
            boxShadow: `0 0 6rem ${hexToRgba(color, 0.5)}`,
        }),
        subRow: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginTop: "4rem",
        } as React.CSSProperties,
        // Explicit gap + no-shrink tag: with space-between alone, a caption + tag wider than
        // the chip butt into each other ("KINETIC STRIKES" ran straight into "LOW CONFIDENCE");
        // the margin guarantees a minimum separation even at overflow.
        strikeLabel: {
            fontSize: "10rem",
            letterSpacing: "0.5rem",
            textTransform: "uppercase" as const,
            color: theme.colors.textMuted,
            fontFamily: mono,
            marginRight: "8rem",
        } as React.CSSProperties,
        confTag: (color: string): React.CSSProperties => ({
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            letterSpacing: "0.5rem",
            textTransform: "uppercase",
            color,
            fontFamily: mono,
            flexShrink: 0,
        }),
        respite: (accents: Accents): React.CSSProperties => ({
            marginLeft: "6rem",
            padding: "1rem 5rem",
            borderRadius: "3rem",
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase",
            color: accents.schemes.accent,
            background: hexToRgba(accents.schemes.accent, 0.15),
            border: `1rem solid ${hexToRgba(accents.schemes.accent, 0.5)}`,
            fontFamily: mono,
        }),
    };
};

interface WarRoomAxisChipsProps {
    gw: GridWarfareDto;
    intelLevel: number;
    /** Fill the whole row with three equal chips (dashboard tab) instead of fixed-width chips (overlay header). */
    stretch?: boolean;
}

export const WarRoomAxisChips: React.FC<WarRoomAxisChipsProps> = memo(({ gw, intelLevel, stretch = false }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createChipStyles(theme), [theme]);

    // Low intel never resolves the exact axis health — the producer only sends a 25%-quantized
    // estimate below Lv1, so the readout is honest about that with a "~" prefix and a LOW tag.
    const coarse = intelLevel < 1;

    const chips: AxisChipModel[] = useMemo(() => [
        {
            axisKey: "UI_WARROOM_AXIS_PHYSICAL",
            strikeKey: "UI_WARROOM_STRIKES_KINETIC",
            color: accents.crisis.accent,
            value: gw.EnemyPhysicalAxis,
            respiteActive: gw.RespitePhysicalActive,
            respiteHoursLeft: gw.RespitePhysicalHoursLeft,
        },
        {
            axisKey: "UI_WARROOM_AXIS_DIGITAL",
            strikeKey: "UI_WARROOM_STRIKES_CYBER",
            color: accents.operations.accent,
            value: gw.EnemyDigitalAxis,
            respiteActive: gw.RespiteDigitalActive,
            respiteHoursLeft: gw.RespiteDigitalHoursLeft,
        },
        {
            axisKey: "UI_WARROOM_AXIS_SOCIAL",
            strikeKey: "UI_WARROOM_STRIKES_PSYOPS",
            color: accents.schemes.accent,
            value: gw.EnemySocialAxis,
            respiteActive: gw.RespiteSocialActive,
            respiteHoursLeft: gw.RespiteSocialHoursLeft,
        },
    ], [gw, accents]);

    return (
        <div style={{ display: "flex", alignItems: "stretch" }}>
            {chips.map((c, i) => {
                const pct = Math.max(0, Math.min(100, c.value));
                const valueText = coarse ? `~${c.value.toFixed(0)}%` : `${c.value.toFixed(0)}%`;
                const showRespite = c.respiteActive;
                const respiteText = c.respiteHoursLeft >= 0
                    ? `${l.t("UI_WARROOM_GAUGE_REGROUP")} ${Math.max(1, Math.round(c.respiteHoursLeft))}h`
                    : l.t("UI_WARROOM_GAUGE_REGROUP");
                return (
                    <div key={c.axisKey} style={s.chip(c.color, stretch, i === chips.length - 1)}>
                        <div style={s.topRow}>
                            <div style={{ display: "flex", alignItems: "center" }}>
                                <span style={s.axisName(c.color)}>{l.t(c.axisKey)}</span>
                                {showRespite && <span style={s.respite(accents)}>{respiteText}</span>}
                            </div>
                            <span style={s.value(c.color)}>{valueText}</span>
                        </div>
                        <div style={s.track}>
                            <div style={s.fill(c.color, pct)} />
                        </div>
                        <div style={s.subRow}>
                            <span style={s.strikeLabel}>{l.t(c.strikeKey)}</span>
                            <span style={s.confTag(coarse ? theme.colors.textMuted : c.color)}>
                                {coarse ? l.t("UI_WARROOM_LOW_CONFIDENCE") : l.t("UI_WARROOM_CONF_CONFIRMED")}
                            </span>
                        </div>
                    </div>
                );
            })}
        </div>
    );
});

WarRoomAxisChips.displayName = "WarRoomAxisChips";
