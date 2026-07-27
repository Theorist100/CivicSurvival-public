/**
 * WarRoomTicker — the bottom C2 EVENT BUS as a single-line ticker. Renders the most recent events
 * (newest-first) on one clipped row, separated by dividers, newest brightest.
 *
 * No CSS marquee / keyframe scroll: continuous transform animation is the expensive path in cohtml
 * and easy to jank on a busy overlay. Instead the row simply re-renders when the event buffer
 * changes and clips the overflow — the "scroll" is the buffer turning over as events land, which is
 * the cheapest COHTML-safe way to get a live single-line feed.
 */

import React, { memo, useMemo } from "react";
import { useTheme, hexToRgba } from "@themes";
import { useLocale } from "@locales";
import { radarThemes } from "@themes/radar";
import { type C2Event, type C2EventKind } from "./useWarRoomEventBus";

const VISIBLE_COUNT = 8;
const cmd = radarThemes.command;

interface WarRoomTickerProps {
    events: C2Event[];
    colorOf: (kind: C2EventKind) => string;
}

export const WarRoomTicker: React.FC<WarRoomTickerProps> = memo(({ events, colorOf }) => {
    const theme = useTheme();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const shown = useMemo(() => events.slice(0, VISIBLE_COUNT), [events]);

    const s = useMemo(() => ({
        bar: {
            display: "flex",
            alignItems: "center",
            flexShrink: 0,
            height: "34rem",
            padding: `0 ${theme.spacing.lg}`,
            borderTop: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
            background: hexToRgba(cmd.bg, 0.7),
            overflow: "hidden" as const,
        } as React.CSSProperties,
        title: {
            display: "flex",
            alignItems: "center",
            flexShrink: 0,
            marginRight: "12rem",
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "1.5rem",
            color: cmd.sweep,
            fontFamily: mono,
        } as React.CSSProperties,
        liveDot: {
            width: "7rem",
            height: "7rem",
            borderRadius: "50%",
            background: cmd.sweep,
            boxShadow: `0 0 7rem ${cmd.sweep}`,
            marginRight: "8rem",
            flexShrink: 0,
        } as React.CSSProperties,
        feed: {
            display: "flex",
            alignItems: "center",
            flex: 1,
            minWidth: 0,
            overflow: "hidden" as const,
            whiteSpace: "nowrap" as const,
        } as React.CSSProperties,
        item: {
            display: "flex",
            // Coherent rejects alignItems: "baseline" (falls back to stretch);
            // flex-end bottom-aligns the single-line time/text pair the same way.
            alignItems: "flex-end",
            flexShrink: 0,
            marginRight: "8rem",
        } as React.CSSProperties,
        sep: {
            flexShrink: 0,
            marginRight: "8rem",
            color: hexToRgba(cmd.sweep, 0.4),
            fontFamily: mono,
            fontSize: "11rem",
        } as React.CSSProperties,
        time: {
            flexShrink: 0,
            marginRight: "6rem",
            color: cmd.compass,
            fontFamily: mono,
            fontSize: "11rem",
        } as React.CSSProperties,
        text: (color: string, latest: boolean): React.CSSProperties => ({
            color,
            opacity: latest ? 1 : 0.72,
            fontWeight: latest ? theme.typography.weightBold : theme.typography.weightMedium,
            fontFamily: mono,
            fontSize: "11rem",
        }),
        empty: {
            fontSize: "11rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
        } as React.CSSProperties,
    }), [theme, mono]);

    return (
        <div style={s.bar}>
            <span style={s.title}>
                <span style={s.liveDot} />
                {l.t("UI_WARROOM_C2_EVENT_BUS")}
            </span>
            <div style={s.feed}>
                {shown.length === 0 ? (
                    <span style={s.empty}>{l.t("UI_WARROOM_AWAITING")}</span>
                ) : (
                    shown.map((ev, i) => (
                        <React.Fragment key={ev.id}>
                            {i > 0 && <span style={s.sep}>|</span>}
                            <span style={s.item}>
                                <span style={s.time}>{ev.time}</span>
                                <span style={s.text(colorOf(ev.kind), i === 0)}>{ev.text}</span>
                            </span>
                        </React.Fragment>
                    ))
                )}
            </div>
        </div>
    );
});

WarRoomTicker.displayName = "WarRoomTicker";
