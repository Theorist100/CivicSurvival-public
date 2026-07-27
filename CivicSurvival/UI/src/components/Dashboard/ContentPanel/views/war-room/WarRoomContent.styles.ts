/**
 * WarRoomContent styles — fullscreen command-theatre layout (flex-only, COHTML-safe).
 * Layers: header (title | axis chips | objective/clock/ESC) -> body (traces | radar |
 * launch console) -> C2 ticker. Cyan command-post identity shared with the radar.
 * Vertical budget: the two side columns are sized to FIT the overlay height without
 * scrolling (overflowY stays as a backstop only) — keep paddings/margins tight here and
 * in the war-room sub-components when adding rows.
 */

import { type Theme, type Accents, hexToRgba } from "@themes";
import { radarThemes } from "@themes/radar";

const cmd = radarThemes.command;

export const createWarRoomStyles = (theme: Theme, accents: Accents) => ({
    // Root: vertical stack — header / enemy strip / body / event-bus log (flex-only, no grid).
    root: {
        display: "flex",
        flexDirection: "column" as const,
        width: "100%",
        height: "100%",
        minHeight: 0,
        overflow: "hidden" as const,
        background: theme.colors.surface,
    } as React.CSSProperties,

    // ---- Header (operation / axis chips / objective ring / clock / close) --
    // Three groups distributed with justify-content — NOT stacked auto-margins: cohtml
    // ignores auto margins in flex rows (documented collision precedent in
    // ArenaProgressHeader), so a double marginLeft:"auto" layout can collapse the center
    // and right groups onto the title.
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        flexShrink: 0,
        height: "78rem",
        padding: `0 ${theme.spacing.lg}`,
        borderBottom: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
        background: hexToRgba(cmd.ring, 0.45),
    } as React.CSSProperties,

    // Left group: accent bar + title/operation + INCOMING/TRACKS chips, kept compact.
    headerLeft: {
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
    } as React.CSSProperties,

    // Center group: the three compact enemy-axis chips.
    headerAxisWrap: {
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
        padding: `0 ${theme.spacing.sm}`,
    } as React.CSSProperties,

    // Right group: objective ring + label, clock, ESC-close.
    headerRight: {
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
    } as React.CSSProperties,

    headerObjective: {
        display: "flex",
        alignItems: "center",
        marginRight: theme.spacing.md,
    } as React.CSSProperties,

    headerObjLabel: {
        maxWidth: "130rem",
        marginLeft: "8rem",
        fontSize: "10rem",
        lineHeight: 1.3,
        fontWeight: theme.typography.weightBold,
        letterSpacing: "0.5rem",
        textTransform: "uppercase" as const,
        color: cmd.compass,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    headerObjValue: {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        color: cmd.sweep,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    headerAccent: {
        width: "4rem",
        height: "34rem",
        borderRadius: "2rem",
        background: cmd.sweep,
        boxShadow: `0 0 12rem ${hexToRgba(cmd.sweep, 0.7)}`,
        marginRight: theme.spacing.md,
        flexShrink: 0,
    } as React.CSSProperties,

    headerTitleWrap: {
        display: "flex",
        flexDirection: "column" as const,
        minWidth: 0,
        minHeight: 0,
    } as React.CSSProperties,

    headerLabel: {
        fontSize: "11rem",
        letterSpacing: "3rem",
        textTransform: "uppercase" as const,
        color: cmd.compass,
    } as React.CSSProperties,

    headerOperation: {
        fontSize: "23rem",
        fontWeight: theme.typography.weightBold,
        letterSpacing: "3rem",
        textTransform: "uppercase" as const,
        color: cmd.sweep,
        textShadow: `0 0 18rem ${hexToRgba(cmd.sweep, 0.35)}`,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    chipRow: {
        display: "flex",
        alignItems: "center",
        marginLeft: theme.spacing.md,
    } as React.CSSProperties,

    chip: (accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        padding: "4rem 11rem",
        marginLeft: theme.spacing.sm,
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(accentColor, 0.12),
        border: `1rem solid ${hexToRgba(accentColor, 0.45)}`,
    } as React.CSSProperties),

    chipDot: (accentColor: string, glow: boolean) => ({
        width: "8rem",
        height: "8rem",
        borderRadius: "50%",
        background: accentColor,
        boxShadow: glow ? `0 0 7rem ${accentColor}` : "none",
        marginRight: "7rem",
        flexShrink: 0,
    } as React.CSSProperties),

    chipLabel: (accentColor: string) => ({
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: accentColor,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    clock: {
        fontSize: "14rem",
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
        marginLeft: theme.spacing.sm,
        letterSpacing: "1rem",
    } as React.CSSProperties,

    // Close pill (ESC): outlined, sits at the right end of the chip row.
    closeButton: {
        display: "flex",
        alignItems: "center",
        padding: "5rem 12rem",
        marginLeft: theme.spacing.md,
        borderRadius: theme.layout.borderRadiusLg,
        background: "transparent",
        border: `1rem solid ${hexToRgba(theme.colors.textSecondary, 0.4)}`,
        cursor: "pointer",
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // ---- Body (left traces / center radar / right launch console) ----------
    body: {
        display: "flex",
        flex: 1,
        minHeight: 0,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    // Tight vertical padding: both columns must fit the overlay height without scrolling
    // (overflowY is a backstop, not the design).
    sidePanel: (side: "left" | "right") => ({
        display: "flex",
        flexDirection: "column" as const,
        width: side === "right" ? "360rem" : "260rem",
        flexShrink: 0,
        minHeight: 0,
        overflowY: "auto" as const,
        overflowX: "hidden" as const,
        // xs vertical padding: the right column must fit unscrolled (overflowY
        // stays as a safety net for extreme UI scales only).
        padding: `${theme.spacing.xs} ${theme.spacing.md}`,
        background: hexToRgba(cmd.ring, 0.15),
        ...(side === "left"
            ? { borderRight: `2rem solid ${theme.colors.border}` }
            : { borderLeft: `2rem solid ${theme.colors.border}` }),
    } as React.CSSProperties),

    panelTitle: {
        display: "flex",
        alignItems: "center",
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "2.5rem",
        color: cmd.sweep,
        paddingBottom: "3rem",
        marginBottom: theme.spacing.xs,
        borderBottom: `1rem solid ${hexToRgba(cmd.sweep, 0.2)}`,
        flexShrink: 0,
    } as React.CSSProperties,

    // Center column: SITUATION|STRIKE view toggle on top, the map area filling the rest.
    center: {
        display: "flex",
        flexDirection: "column" as const,
        flex: 1,
        minWidth: 0,
        minHeight: 0,
        position: "relative" as const,
        padding: theme.spacing.sm,
    } as React.CSSProperties,

    // Map area — holds either the fit-height radar (DEFENSE) or the enemy projection (ATTACK),
    // centered, and hosts the absolute INCOMING WAVE banner.
    mapArea: {
        display: "flex",
        flex: 1,
        minWidth: 0,
        minHeight: 0,
        position: "relative" as const,
        alignItems: "center",
        justifyContent: "center",
    } as React.CSSProperties,

    // ---- SITUATION | STRIKE view toggle (above the map) --------------------
    // The toggle is the ONLY in-flow child, centered by the row; the corner status
    // chrome (clock chip left, INCOMING WAVE right) sits in absolute slots OUTSIDE
    // the flow. Flex side-zones looked equivalent but cohtml sizes flex:1 zones by
    // their content, so the chip's ×1↔PAUSED width change (and the banner appearing)
    // nudged the toggle left/right. Absolute slots cannot move the toggle, and the
    // row's minHeight still pins the map-area height.
    toggleRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        position: "relative" as const,
        flexShrink: 0,
        minHeight: "30rem",
        marginBottom: theme.spacing.sm,
    } as React.CSSProperties,

    toggleSlotLeft: {
        display: "flex",
        position: "absolute" as const,
        left: 0,
        top: 0,
        bottom: 0,
        alignItems: "center",
    } as React.CSSProperties,

    toggleSlotRight: {
        display: "flex",
        position: "absolute" as const,
        right: 0,
        top: 0,
        bottom: 0,
        alignItems: "center",
    } as React.CSSProperties,

    // ---- Simulation clock chip (pause / speed) ----------------------------
    // The fullscreen War Room covers the vanilla toolbar, so the overlay restates the
    // simulation state: PAUSED is a loud amber (the player must not misread a frozen
    // enemy board as live), the running ×N multiplier is quiet chrome.
    clockChip: (paused: boolean) => {
        const accent = paused ? accents.resilience.accent : theme.colors.textMuted;
        return {
            display: "flex",
            alignItems: "center",
            padding: "4rem 10rem",
            borderRadius: theme.layout.borderRadiusLg,
            border: `1rem solid ${hexToRgba(accent, paused ? 0.8 : 0.35)}`,
            background: paused ? hexToRgba(accents.resilience.accent, 0.14) : "transparent",
            color: accent,
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            letterSpacing: "1rem",
            textTransform: "uppercase" as const,
            fontFamily: theme.typography.fontFamilyMono,
        } as React.CSSProperties;
    },

    // Pause glyph as two plain div bars (no inline <svg> — see the chip's comment in
    // WarRoomContent.tsx: swapping SVG subtrees AV'd the cohtml layout thread).
    clockPauseBars: {
        display: "flex",
        flexShrink: 0,
        marginRight: "6rem",
    } as React.CSSProperties,

    clockPauseBar: {
        display: "flex",
        width: "2.4rem",
        height: "8rem",
        marginRight: "1.6rem",
        background: accents.resilience.accent,
    } as React.CSSProperties,

    viewToggle: {
        display: "flex",
        flexShrink: 0,
        border: `1rem solid ${hexToRgba(cmd.sweep, 0.35)}`,
        borderRadius: theme.layout.borderRadiusLg,
        overflow: "hidden" as const,
        background: hexToRgba(cmd.bg, 0.85),
    } as React.CSSProperties,

    viewSeg: (active: boolean, kind: "situation" | "strike") => {
        const accent = kind === "strike" ? accents.crisis.accent : cmd.sweep;
        return {
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "6rem 22rem",
            cursor: "pointer",
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            letterSpacing: "2rem",
            textTransform: "uppercase" as const,
            fontFamily: theme.typography.fontFamilyMono,
            color: active ? accent : theme.colors.textMuted,
            background: active ? hexToRgba(accent, 0.16) : "transparent",
        } as React.CSSProperties;
    },

    // ---- INCOMING WAVE banner (ATTACK view only) --------------------------
    // Full-width row so the pill centers without transform (cohtml auto-margin/transform
    // centering is unreliable); the row is click-through, only the pill takes clicks.
    // STRIKE target card floats OVER the map bottom instead of sitting in flow
    // below it: an in-flow card changed mapArea's height between views (and per
    // selection), while useSquareFit measures once — the stale square then
    // overflowed the shrunken container and covered the SITUATION|STRIKE
    // toggle. As an overlay the map geometry is identical in both views.
    // Fixed 520rem centered (left 50% − half width): a full-width bar stuck out
    // past the fit-height map square's left/right edges — the plate must sit
    // WITHIN the map, and the square is height-driven so its width is unknown
    // to CSS; 520rem is comfortably narrower than any playable square.
    strikeCardOverlay: {
        position: "absolute" as const,
        bottom: "10rem",
        left: "50%",
        width: "520rem",
        marginLeft: "-260rem",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(cmd.bg, 0.92),
    } as React.CSSProperties,

    incomingBanner: {
        display: "flex",
        alignItems: "center",
        padding: "4rem 10rem",
        borderRadius: theme.layout.borderRadiusLg,
        border: `1rem solid ${hexToRgba(accents.crisis.accent, 0.6)}`,
        background: hexToRgba(accents.crisis.accent, 0.18),
        boxShadow: `0 0 16rem ${hexToRgba(accents.crisis.accent, 0.4)}`,
    } as React.CSSProperties,

    incomingText: {
        fontSize: "12rem",
        fontWeight: theme.typography.weightBold,
        letterSpacing: "1rem",
        textTransform: "uppercase" as const,
        color: accents.crisis.accent,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // ---- Arsenal (right console) ------------------------------------------
    arsenalBlock: {
        display: "flex",
        flexDirection: "column" as const,
        flexShrink: 0,
        minHeight: 0,
        marginTop: theme.spacing.sm,
        paddingTop: theme.spacing.xs,
        borderTop: `1rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    arsenalTitle: {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "1.5rem",
        color: cmd.sweep,
        marginBottom: theme.spacing.xs,
        flexShrink: 0,
    } as React.CSSProperties,

    arsenalButton: (accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "5rem 0",
        marginTop: "4rem",
        background: "transparent",
        border: `1rem solid ${accentColor}`,
        borderRadius: theme.layout.borderRadiusLg,
        boxShadow: `inset 0 0 18rem ${hexToRgba(accentColor, 0.08)}`,
        cursor: "pointer",
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: accentColor,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    counterRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "3rem 8rem",
        marginBottom: "2rem",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(cmd.ring, 0.4),
        border: `1rem solid ${hexToRgba(cmd.sweep, 0.18)}`,
    } as React.CSSProperties,

    counterLabel: {
        fontSize: "10rem",
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    counterValue: (color: string) => ({
        fontSize: "14rem",
        fontWeight: theme.typography.weightBold,
        color,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    // ---- Quantity stepper (−/value/+) -------------------------------------
    stepperRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginTop: "4rem",
    } as React.CSSProperties,

    stepperLabel: {
        fontSize: "10rem",
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    stepperControls: {
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    stepperButton: (enabled: boolean) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "22rem",
        height: "22rem",
        background: "transparent",
        border: `1rem solid ${hexToRgba(cmd.sweep, enabled ? 0.55 : 0.18)}`,
        borderRadius: theme.layout.borderRadius,
        cursor: enabled ? "pointer" : "default",
        fontSize: "13rem",
        fontWeight: theme.typography.weightBold,
        color: enabled ? cmd.sweep : theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    stepperValue: {
        // Flex centering, not textAlign: cohtml left-aligns text inside a
        // min-width span, so the digit sat next to the "-" button instead of
        // between the two.
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minWidth: "34rem",
        margin: "0 6rem",
        fontSize: "14rem",
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textPrimary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // "No city intel" placeholder (STRIKE view before the mirror snapshot arrives).
    eventEmpty: {
        fontSize: "11rem",
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,
});
