/**
 * ProcurementChoicePanel Styles
 * Side-by-side vendor comparison for maintenance contracts
 */

import type React from "react";
import { type Theme, type Accents } from "../../themes";
import { Z_INDEX } from "../../themes";

export const createStyles = (t: Theme, accents: Accents) => ({
    overlay: {
        position: "fixed" as const,
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: t.effects.glassBackground,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: Z_INDEX.modal,
    } as React.CSSProperties,

    panel: {
        width: "600rem",
        maxWidth: "900rem",
        backgroundColor: t.colors.surface,
        border: `2rem solid ${accents.schemes.accent}`,
        borderRadius: t.layout.borderRadiusLg,
        overflow: "hidden",
        boxShadow: t.effects.shadowLg,
    } as React.CSSProperties,

    header: {
        padding: t.spacing.md,
        backgroundColor: accents.schemes.accentDim,
        borderBottom: `1rem solid ${accents.schemes.accent}`,
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    headerChild: {
        marginRight: t.spacing.sm,
    } as React.CSSProperties,

    headerIcon: {
        fontSize: t.typography.sizeLG,
    } as React.CSSProperties,

    headerTitle: {
        flex: 1,
    } as React.CSSProperties,

    title: {
        color: accents.schemes.accentBright,
        fontSize: t.typography.sizeLG,
        fontWeight: t.typography.weightBold,
        margin: 0,
    } as React.CSSProperties,

    subtitle: {
        color: t.colors.textSecondary,
        fontSize: t.typography.sizeSM,
        margin: 0,
    } as React.CSSProperties,

    body: {
        padding: t.spacing.md,
    } as React.CSSProperties,

    description: {
        color: t.colors.textPrimary,
        fontSize: t.typography.sizeMD,
        marginBottom: t.spacing.md,
        lineHeight: 1.5,
        fontStyle: "italic",
    } as React.CSSProperties,

    vendorsContainer: {
        display: "flex",
        marginBottom: t.spacing.md,
    } as React.CSSProperties,

    vendorsContainerChild: {
        marginRight: t.spacing.md,
    } as React.CSSProperties,

    vendorCard: (isShady: boolean) => ({
        flex: 1,
        padding: t.spacing.md,
        backgroundColor: isShady ? accents.crisis.accentDim : t.colors.paper,
        border: `2rem solid ${isShady ? accents.crisis.accent : accents.operations.accent}`,
        borderRadius: t.layout.borderRadius,
        cursor: "pointer",
        transition: `background-color ${t.effects.transitionFast}, border-color ${t.effects.transitionFast}`,
    } as React.CSSProperties),

    vendorHeader: (isShady: boolean) => ({
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        marginBottom: t.spacing.sm,
        paddingBottom: t.spacing.xs,
        borderBottom: `1rem solid ${isShady ? accents.crisis.accent : accents.operations.accent}`,
    } as React.CSSProperties),

    vendorType: (isShady: boolean) => ({
        color: isShady ? accents.crisis.accentBright : accents.operations.accentBright,
        fontSize: t.typography.sizeSM,
        fontWeight: t.typography.weightBold,
        textTransform: "uppercase" as const,
    } as React.CSSProperties),

    vendorBadge: (isShady: boolean) => ({
        padding: `${t.spacing.xs} ${t.spacing.sm}`,
        backgroundColor: isShady ? accents.crisis.accent : accents.schemes.accent,
        borderRadius: t.layout.borderRadius,
        color: t.colors.surface,
        fontSize: t.typography.sizeXS,
        fontWeight: t.typography.weightBold,
    } as React.CSSProperties),

    vendorName: {
        color: t.colors.textPrimary,
        fontSize: t.typography.sizeMD,
        fontWeight: t.typography.weightMedium,
        marginBottom: t.spacing.sm,
    } as React.CSSProperties,

    kickbackRow: {
        marginTop: t.spacing.sm,
        padding: t.spacing.sm,
        backgroundColor: accents.vip.accentDim,
        borderRadius: t.layout.borderRadius,
        border: `1rem solid ${accents.vip.accent}`,
    } as React.CSSProperties,

    kickbackLabel: {
        color: accents.vip.accentBright,
        fontSize: t.typography.sizeSM,
        fontWeight: t.typography.weightBold,
    } as React.CSSProperties,

    kickbackValue: {
        color: accents.vip.accent,
        fontSize: t.typography.sizeMD,
        fontWeight: t.typography.weightBold,
        fontFamily: t.typography.fontFamilyMono,
    } as React.CSSProperties,

    actions: {
        display: "flex",
        justifyContent: "flex-end",
        paddingTop: t.spacing.md,
        borderTop: `1rem solid ${t.colors.border}`,
    } as React.CSSProperties,

    actionsChild: {
        marginRight: t.spacing.sm,
    } as React.CSSProperties,

    button: (variant: "official" | "shady" | "decline") => {
        const colors = {
            official: { bg: accents.operations.accentDim, border: accents.operations.accent, text: accents.operations.accentBright },
            shady: { bg: accents.crisis.accentDim, border: accents.crisis.accent, text: accents.crisis.accentBright },
            decline: { bg: t.colors.paper, border: t.colors.border, text: t.colors.textSecondary },
        };
        const c = colors[variant];
        return {
            padding: `${t.spacing.sm} ${t.spacing.lg}`,
            backgroundColor: c.bg,
            border: `2rem solid ${c.border}`,
            borderRadius: t.layout.borderRadius,
            color: c.text,
            fontSize: t.typography.sizeMD,
            fontWeight: t.typography.weightBold,
            cursor: "pointer",
            transition: `background-color ${t.effects.transitionFast}, color ${t.effects.transitionFast}`,
        } as React.CSSProperties;
    },

});
