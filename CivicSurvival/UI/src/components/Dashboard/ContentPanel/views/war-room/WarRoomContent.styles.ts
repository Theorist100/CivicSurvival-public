/**
 * WarRoomContent styles — PRISMA command-room layout (flex-only, COHTML-safe).
 * Cyan command-post identity shared with the radar (radarThemes.command), danger
 * stays on per-track colors / phase chip, not the whole chrome.
 */

import { type Theme, type Accents, hexToRgba } from "@themes";
import { radarThemes } from "@themes/radar";

const cmd = radarThemes.command;

export const createWarRoomStyles = (theme: Theme, _accents: Accents) => ({
    // Root: vertical stack — header / body / event-bus log (flex-only, no grid).
    root: {
        display: "flex",
        flexDirection: "column" as const,
        width: "100%",
        height: "100%",
        minHeight: 0,
        overflow: "hidden" as const,
        background: theme.colors.surface,
    } as React.CSSProperties,

    // ---- Header (operation / clock / status chips) -------------------------
    // Mockup: 56px bar, ring-tinted backplate, cyan operation title @ 20px.
    header: {
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
        height: "56rem",
        padding: `0 ${theme.spacing.lg}`,
        borderBottom: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
        background: hexToRgba(cmd.ring, 0.45),
    } as React.CSSProperties,

    // Cyan accent bar anchoring the operation title (depth cue, mockup v2).
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
        marginLeft: "auto",
    } as React.CSSProperties,

    // Mockup chip: 1px hairline border, pill, 11px mono label.
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

    // ---- Body (left traces / center radar / right fleet) -------------------
    body: {
        display: "flex",
        flex: 1,
        minHeight: 0,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    // Mockup side rail: 236px, 14px pad, 2px command-tinted divider.
    sidePanel: (side: "left" | "right") => ({
        display: "flex",
        flexDirection: "column" as const,
        width: "236rem",
        flexShrink: 0,
        minHeight: 0,
        overflowY: "auto" as const,
        overflowX: "hidden" as const,
        padding: theme.spacing.lg,
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
        paddingBottom: theme.spacing.sm,
        marginBottom: theme.spacing.md,
        borderBottom: `1rem solid ${hexToRgba(cmd.sweep, 0.2)}`,
        flexShrink: 0,
    } as React.CSSProperties,

    center: {
        display: "flex",
        flex: 1,
        minWidth: 0,
        minHeight: 0,
        alignItems: "center",
        justifyContent: "center",
        padding: theme.spacing.sm,
    } as React.CSSProperties,

    // ---- Right fleet counters ---------------------------------------------
    // Wave tallies as a dense 2-column card grid (mockup v2): small mono label
    // stacked over a large value, two cards per row via flex-wrap.
    statGrid: {
        display: "flex",
        flexWrap: "wrap" as const,
        minHeight: 0,
        marginTop: theme.spacing.md,
    } as React.CSSProperties,

    statCell: {
        display: "flex",
        flexDirection: "column" as const,
        flex: "1 1 45%",
        padding: "8rem 10rem",
        margin: "0 4rem 6rem 0",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(cmd.ring, 0.4),
        border: `1rem solid ${hexToRgba(cmd.sweep, 0.15)}`,
    } as React.CSSProperties,

    statCellLabel: {
        fontSize: "10rem",
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    statCellValue: (color: string) => ({
        fontSize: "20rem",
        fontWeight: theme.typography.weightBold,
        lineHeight: 1.1,
        marginTop: "3rem",
        color,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    counterRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "6rem 8rem",
        marginBottom: "4rem",
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

    // ---- Bottom C2 EVENT BUS log ------------------------------------------
    eventBus: {
        display: "flex",
        flexDirection: "column" as const,
        flexShrink: 0,
        height: "122rem",
        borderTop: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
        background: hexToRgba(cmd.bg, 0.7),
    } as React.CSSProperties,

    eventBusHeader: {
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
        padding: `5rem ${theme.spacing.lg}`,
        borderBottom: `1rem solid ${hexToRgba(cmd.sweep, 0.18)}`,
    } as React.CSSProperties,

    eventBusTitle: {
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "1.5rem",
        color: cmd.sweep,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    eventBusFeed: {
        display: "flex",
        flexDirection: "column" as const,
        flex: 1,
        minHeight: 0,
        overflowY: "auto" as const,
        overflowX: "hidden" as const,
        padding: `5rem ${theme.spacing.lg}`,
    } as React.CSSProperties,

    eventLine: {
        display: "flex",
        alignItems: "baseline",
        fontSize: "12rem",
        lineHeight: 1.55,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    eventTime: {
        color: cmd.compass,
        marginRight: "10rem",
        flexShrink: 0,
    } as React.CSSProperties,

    eventText: (color: string) => ({
        color,
    } as React.CSSProperties),

    eventEmpty: {
        fontSize: "11rem",
        fontStyle: "italic" as const,
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // ---- Arsenal procurement (FLEET STATUS) -------------------------------
    // Mockup arsenal: hairline divider above, transparent outline buttons.
    arsenalBlock: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        marginTop: theme.spacing.lg,
        paddingTop: theme.spacing.md,
        borderTop: `1rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    // Mockup a-title: tighter tracking than the section panel titles.
    arsenalTitle: {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "1.5rem",
        color: cmd.sweep,
        marginBottom: theme.spacing.sm,
        flexShrink: 0,
    } as React.CSSProperties,

    arsenalButton: (accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "9rem 0",
        marginTop: "8rem",
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

    // ---- Quantity stepper (−/value/+) -------------------------------------
    stepperRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginTop: "6rem",
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
        minWidth: "34rem",
        textAlign: "center" as const,
        fontSize: "14rem",
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textPrimary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // ---- Enemy suppression (FLEET STATUS) ---------------------------------
    // Per-axis row: label + SUPPRESSED badge; objective bar below the three axes.
    suppressionBlock: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        marginTop: theme.spacing.lg,
        paddingTop: theme.spacing.md,
        borderTop: `1rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    suppressionTitle: {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "1.5rem",
        color: cmd.sweep,
        marginBottom: theme.spacing.sm,
        flexShrink: 0,
    } as React.CSSProperties,

    axisRow: (accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "6rem 8rem",
        marginBottom: "4rem",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(cmd.ring, 0.4),
        border: `1rem solid ${hexToRgba(accentColor, 0.3)}`,
    } as React.CSSProperties),

    axisLabel: (accentColor: string) => ({
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: accentColor,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    suppressBadge: (suppressed: boolean) => ({
        padding: "2rem 7rem",
        borderRadius: theme.layout.borderRadius,
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        fontFamily: theme.typography.fontFamilyMono,
        ...(suppressed
            ? {
                  background: hexToRgba(cmd.sweep, 0.18),
                  border: `1rem solid ${hexToRgba(cmd.sweep, 0.55)}`,
                  boxShadow: `0 0 10rem ${hexToRgba(cmd.sweep, 0.25)}`,
                  color: cmd.sweep,
              }
            : {
                  background: "transparent",
                  border: `1rem solid ${hexToRgba(cmd.compass, 0.25)}`,
                  color: theme.colors.textMuted,
              }),
    } as React.CSSProperties),

    objectiveLabelRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginTop: "6rem",
        marginBottom: "4rem",
    } as React.CSSProperties,

    objectiveLabel: {
        fontSize: "10rem",
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    objectiveValue: {
        fontSize: "11rem",
        fontWeight: theme.typography.weightBold,
        color: cmd.sweep,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    objectiveTrack: {
        display: "flex",
        height: "8rem",
        borderRadius: theme.layout.borderRadius,
        background: hexToRgba(cmd.ring, 0.5),
        border: `1rem solid ${hexToRgba(cmd.sweep, 0.18)}`,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    objectiveFill: (fraction: number) => ({
        width: `${Math.round(Math.max(0, Math.min(1, fraction)) * 100)}%`,
        height: "100%",
        background: cmd.sweep,
        boxShadow: `0 0 10rem ${hexToRgba(cmd.sweep, 0.5)}`,
    } as React.CSSProperties),
});
