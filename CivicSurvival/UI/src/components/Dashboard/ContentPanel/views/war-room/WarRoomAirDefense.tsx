/**
 * WarRoomAirDefense — the left-column AIR DEFENSE block. Two ammo bars (Patriot missiles + AA gun
 * shells) with their per-kind RESUPPLY buttons, a compact status ledger (SIREN / AUTO-RESUPPLY /
 * POLICY / SPOTTER PENALTY), and a deep-link to the full DEFENSE tab for placement/policy work the
 * overlay cannot host.
 *
 * Reuses the existing defense data + resupply triggers verbatim: useDefenseData() builds the ammo
 * rows and the two resupply descriptors, and the RESUPPLY buttons fire the same
 * emergencyResupplyRockets / emergencyResupplyGuns wrappers the DEFENSE tab uses, correlated
 * against the shared EmergencyResupplyRequest. No new trigger. Pause-safe: the trigger goes
 * through the EmergencyResupply request lifecycle, whose consumer
 * (AirDefenseActionRequestSystem) is registered in ModificationEnd — a phase that ticks while
 * the game is paused.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { useDefenseData } from "@hooks/domain";
import { useDefenseActions, useRequestAction } from "@hooks/actions";
import { AMMO_KIND } from "../../../../../types/semantic";

// Mirrors ReasonIds.AaResupplyCooldown — when this is the lock reason the button shows the
// "next wave" wait instead of the cost (the one-resupply-per-wave gate, not an error).
const RESUPPLY_WAVE_COOLDOWN_REASON = "UI_AA_RESUPPLY_COOLDOWN";

interface WarRoomAirDefenseProps {
    /** Deep-link: close the overlay and land the Dashboard on the DEFENSE tab. */
    onManageAA: () => void;
}

export const WarRoomAirDefense: React.FC<WarRoomAirDefenseProps> = memo(({ onManageAA }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const { aa, ammoRows, patriotResupply, gunsResupply } = useDefenseData();
    const actions = useDefenseActions();

    // Two resupply actions, both correlated against the single EmergencyResupplyRequest key —
    // identical wiring to DefenseContent. Patriot restocks rockets, the gun button restocks shells.
    const patriotAction = useRequestAction(
        () => { actions.emergencyResupplyRockets(); return true; },
        aa.EmergencyResupplyRequest,
    );
    const gunsAction = useRequestAction(
        () => { actions.emergencyResupplyGuns(); return true; },
        aa.EmergencyResupplyRequest,
    );

    const s = useMemo(() => ({
        block: {
            display: "flex",
            flexDirection: "column" as const,
            minHeight: "40rem",
            flexShrink: 0,
            marginTop: theme.spacing.md,
            paddingTop: theme.spacing.sm,
            borderTop: `1rem solid ${theme.colors.border}`,
        } as React.CSSProperties,
        title: {
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            color: accents.schemes.accent,
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,
        ammoHead: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
        } as React.CSSProperties,
        ammoLabel: {
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textSecondary,
            fontFamily: mono,
        } as React.CSSProperties,
        ammoValue: (color: string): React.CSSProperties => ({
            fontSize: "13rem",
            fontWeight: theme.typography.weightBold,
            color,
            fontFamily: mono,
        }),
        track: {
            height: "6rem",
            marginTop: "5rem",
            borderRadius: "3rem",
            background: hexToRgba(theme.colors.textMuted, 0.18),
            overflow: "hidden" as const,
        } as React.CSSProperties,
        fill: (color: string, pct: number): React.CSSProperties => ({
            width: `${pct}%`,
            height: "100%",
            background: color,
            boxShadow: `0 0 6rem ${hexToRgba(color, 0.5)}`,
        }),
        resupply: (enabled: boolean, color: string): React.CSSProperties => ({
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginTop: "6rem",
            padding: "6rem 0",
            borderRadius: theme.layout.borderRadius,
            border: `1rem solid ${hexToRgba(color, enabled ? 0.7 : 0.25)}`,
            background: enabled ? hexToRgba(color, 0.1) : "transparent",
            cursor: enabled ? "pointer" : "not-allowed",
            opacity: enabled ? 1 : 0.6,
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: enabled ? color : theme.colors.textMuted,
            fontFamily: mono,
        }),
        row: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginTop: "5rem",
        } as React.CSSProperties,
        rowLabel: {
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
        } as React.CSSProperties,
        rowValue: (color: string): React.CSSProperties => ({
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            color,
            fontFamily: mono,
        }),
        manage: {
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginTop: "10rem",
            padding: "7rem 0",
            borderRadius: theme.layout.borderRadiusLg,
            border: `1rem solid ${hexToRgba(accents.schemes.accent, 0.5)}`,
            background: "transparent",
            cursor: "pointer",
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: accents.schemes.accent,
            fontFamily: mono,
        } as React.CSSProperties,
        ammoWrap: { marginBottom: "8rem" } as React.CSSProperties,
    }), [theme, accents, mono]);

    // Explicit per-kind wiring: an ammo kind this block does not know gets NO resupply button,
    // instead of silently inheriting the gun descriptor+action (a future third munition would
    // have shown the gun price and restocked the gun group on click).
    const resupplyFor = (kind: number) => {
        switch (kind) {
            case AMMO_KIND.Rockets: return { desc: patriotResupply, action: patriotAction };
            case AMMO_KIND.Shells: return { desc: gunsResupply, action: gunsAction };
            default: return null;
        }
    };

    const spotterPenalty = Math.round(aa.SpotterPenaltyPercent ?? 0);
    const policyText = aa.DefensePolicyName ? l.tDynamic(aa.DefensePolicyName) : "--";

    return (
        <div style={s.block}>
            <div style={s.title}>{l.t("UI_WARROOM_AIR_DEFENSE")}</div>

            {ammoRows.length === 0 ? (
                <span style={s.rowLabel}>{l.t("UI_WARROOM_AD_NO_AMMO")}</span>
            ) : (
                ammoRows.map((row) => {
                    const wired = resupplyFor(row.kind);
                    if (wired === null) {
                        return (
                            <div key={row.kind} style={s.ammoWrap}>
                                <div style={s.ammoHead}>
                                    <span style={s.ammoLabel}>{l.t(row.labelKey)}</span>
                                    <span style={s.ammoValue(row.color)}>{`${row.ammo} / ${row.maxAmmo}`}</span>
                                </div>
                                <div style={s.track}>
                                    <div style={s.fill(row.barColor, Math.max(0, Math.min(100, row.percent)))} />
                                </div>
                            </div>
                        );
                    }
                    const { desc, action } = wired;
                    // Disable while ANY resupply on the shared key is in flight (the one-batch
                    // gate), but say "PROCESSING" only on the button whose OWN request flies —
                    // the sibling shows its normal label so the player is not told the guns are
                    // restocking when they bought Patriot missiles.
                    const enabled = desc.canRun && !action.isPending;
                    const onCooldown = !desc.canRun && desc.reasonId === RESUPPLY_WAVE_COOLDOWN_REASON;
                    const buttonText = action.isOwnPending
                        ? l.t("UI_PROCESSING")
                        : onCooldown
                            ? l.t("UI_AA_RESUPPLY_NEXT_WAVE")
                            : !desc.canRun && desc.reasonId
                                ? l.tDynamic(desc.reasonId)
                                : l.t("UI_WARROOM_AD_RESUPPLY", `$${formatCostArg(desc.cost)}k`);
                    const handle = (): void => { if (enabled) action.execute(); };
                    return (
                        <div key={row.kind} style={s.ammoWrap}>
                            <div style={s.ammoHead}>
                                <span style={s.ammoLabel}>{l.t(row.labelKey)}</span>
                                <span style={s.ammoValue(row.color)}>{`${row.ammo} / ${row.maxAmmo}`}</span>
                            </div>
                            <div style={s.track}>
                                <div style={s.fill(row.barColor, Math.max(0, Math.min(100, row.percent)))} />
                            </div>
                            {desc.isNeeded && (
                                <div
                                    style={s.resupply(enabled, row.lowAmmo ? accents.crisis.accent : accents.schemes.accent)}
                                    role="button"
                                    tabIndex={0}
                                    onClick={handle}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter" || e.key === " ") {
                                            e.preventDefault();
                                            handle();
                                        }
                                    }}
                                >
                                    {buttonText}
                                </div>
                            )}
                        </div>
                    );
                })
            )}

            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_AD_SIREN")}</span>
                <span style={s.rowValue(aa.SirenActive ? accents.crisis.accent : theme.colors.textMuted)}>
                    {aa.SirenActive ? l.t("UI_WARROOM_AD_ON") : l.t("UI_WARROOM_AD_OFF")}
                </span>
            </div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_AD_AUTO")}</span>
                <span style={s.rowValue(aa.AutoResupplyEnabled ? accents.schemes.accent : theme.colors.textMuted)}>
                    {aa.AutoResupplyEnabled ? l.t("UI_WARROOM_AD_ON") : l.t("UI_WARROOM_AD_OFF")}
                </span>
            </div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_AD_POLICY")}</span>
                <span style={s.rowValue(theme.colors.textSecondary)}>{policyText}</span>
            </div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_AD_SPOTTER")}</span>
                <span style={s.rowValue(spotterPenalty > 0 ? accents.crisis.accent : theme.colors.textMuted)}>
                    {`-${spotterPenalty}%`}
                </span>
            </div>

            <div
                style={s.manage}
                role="button"
                tabIndex={0}
                onClick={onManageAA}
                onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onManageAA();
                    }
                }}
            >
                {l.t("UI_WARROOM_MANAGE_AA")}
            </div>
        </div>
    );
});

WarRoomAirDefense.displayName = "WarRoomAirDefense";
