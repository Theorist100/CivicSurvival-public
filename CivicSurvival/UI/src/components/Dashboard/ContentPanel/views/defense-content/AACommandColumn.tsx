import React from "react";
import { Column } from "@coherent";
import { formatCostArg, useAccents, useTheme } from "@themes";
import type { AirDefenseDto } from "@hooks/domain";
import { useLocale } from "@locales";
import { HelpSection } from "@shared/common/HelpSection";
import { HoverTip } from "@shared/common/HoverTip";
import { ProgressBar, SectionHeader, StatRow } from "@shared/ui";
import { Spinner } from "@shared/common/Spinner";
import { AABuildSection } from "./AABuildSection";
import { AA_TYPE } from "../../../../../types/semantic";
import type { AaTypeRow, GunsResupply, PatriotResupply } from "@hooks/domain/useDefenseData";
import type { BoforsValidator, GepardValidator, PatriotValidator, WarViewStyles } from "./types";
import { type useDefenseActions, type useRequestAction } from "@hooks/actions";

type ResupplyAction = ReturnType<typeof useRequestAction>;

interface AACommandColumnProps {
    aa: AirDefenseDto;
    actions: ReturnType<typeof useDefenseActions>;
    ammoRows: AaTypeRow[];
    isAAActive: boolean;
    bofors: BoforsValidator;
    gepard: GepardValidator;
    patriot: PatriotValidator;
    patriotResupply: PatriotResupply;
    gunsResupply: GunsResupply;
    patriotResupplyAction: ResupplyAction;
    gunsResupplyAction: ResupplyAction;
    styles: WarViewStyles;
}

/** Amber warning triangle (SVG, never an emoji) — low-ammo flag inline on a type row. */
const LowAmmoMark: React.FC<{ color: string }> = ({ color }) => (
    <svg
        viewBox="0 0 24 24"
        style={{ width: "11rem", height: "11rem", marginLeft: "4rem", verticalAlign: "middle" }}
        aria-hidden="true"
    >
        <path d="M12 3 L22 20 L2 20 Z" fill={color} stroke="#1a1a1a" strokeWidth="1.5" strokeLinejoin="round" />
        <rect x="11" y="9" width="2" height="5" fill="#1a1a1a" />
        <rect x="11" y="16" width="2" height="2" fill="#1a1a1a" />
    </svg>
);

/** Circular reload arrow (SVG, never an emoji) — resupply button glyph. */
const ResupplyMark: React.FC<{ color: string }> = ({ color }) => (
    <svg
        viewBox="0 0 24 24"
        style={{ width: "11rem", height: "11rem", marginRight: "3rem", verticalAlign: "middle" }}
        aria-hidden="true"
    >
        <path
            d="M20 12 a8 8 0 1 1 -2.3 -5.6"
            fill="none"
            stroke={color}
            strokeWidth="2.2"
            strokeLinecap="round"
        />
        <path d="M20 4 L20 9 L15 9 Z" fill={color} />
    </svg>
);

/**
 * Reason id the backend emits when the Patriot resupply is blocked by its one-per-wave gate.
 * Mirrors ReasonIds.AaResupplyCooldown — when this is the lock reason the button shows the
 * "next wave" wait instead of the cost.
 */
const RESUPPLY_WAVE_COOLDOWN_REASON = "UI_AA_RESUPPLY_COOLDOWN";

/** Common shape of the two resupply buttons (guns group + Patriot). */
interface ResupplyDescriptor {
    canRun: boolean;
    reasonId: string;
    cost: number;
}

interface ResupplyButtonProps {
    desc: ResupplyDescriptor;
    action: ResupplyAction;
    accentColor: string;
    /** Optional leading label, e.g. the standalone "restock guns" button. */
    label?: string;
}

/** Resupply button: reload glyph + optional label + cost. Disabled when not eligible. */
const ResupplyButton: React.FC<ResupplyButtonProps> = ({ desc, action, accentColor, label }) => {
    const theme = useTheme();
    const l = useLocale();
    const disabled = !desc.canRun;
    // Patriot's one-resupply-per-wave gate surfaces as this lock reason — show the wait, not the cost.
    const onCooldown = disabled && desc.reasonId === RESUPPLY_WAVE_COOLDOWN_REASON;
    const tooltip = !desc.canRun && desc.reasonId
        ? l.tDynamic(desc.reasonId)
        : undefined;
    const handleClick = (): void => {
        if (!disabled && !action.isPending) action.execute();
    };

    const glyphColor = disabled ? theme.colors.textMuted : theme.colors.textPrimary;

    return (
        <button
            onClick={handleClick}
            title={tooltip}
            style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                padding: "3rem 8rem",
                marginLeft: "8rem",
                flexShrink: 0,
                minWidth: "62rem",
                backgroundColor: disabled || action.isPending ? theme.colors.paper : accentColor,
                color: glyphColor,
                border: "none",
                borderRadius: "4rem",
                cursor: disabled || action.isPending ? "not-allowed" : "pointer",
                fontWeight: 600,
                fontSize: "11rem",
                opacity: disabled ? 0.6 : 1,
            }}
        >
            {action.isPending ? (
                <Spinner size={11} thickness={2} color={theme.colors.textMuted} />
            ) : onCooldown ? (
                // Patriot gates to one resupply per wave: show the wait, not the cost.
                <span>{l.t("UI_AA_RESUPPLY_NEXT_WAVE")}</span>
            ) : (
                <>
                    <ResupplyMark color={glyphColor} />
                    {label ? <span style={{ marginRight: "4rem" }}>{label}</span> : null}
                    {`$${formatCostArg(desc.cost)}k`}
                </>
            )}
        </button>
    );
};

export const AACommandColumn: React.FC<AACommandColumnProps> = ({
    aa,
    actions,
    ammoRows,
    isAAActive,
    bofors,
    gepard,
    patriot,
    patriotResupply,
    gunsResupply,
    patriotResupplyAction,
    gunsResupplyAction,
    styles: s,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const patriotRows = ammoRows.filter((r) => r.type === AA_TYPE.PatriotSAM);
    const gunRows = ammoRows.filter((r) => r.type !== AA_TYPE.PatriotSAM);

    const renderAmmoBar = (row: AaTypeRow, inlineButton: React.ReactNode): React.ReactNode => {
        const unitLabel = row.unit === "missiles"
            ? l.t("UI_AA_UNIT_MISSILES")
            : l.t("UI_AA_UNIT_ROUNDS");
        // Hover the whole type row → "выпрыгивающий совет" balloon explaining the role:
        // Patriot is anti-ballistic (weak vs drones), guns are anti-drone. Role is derived
        // from row.type — no DTO field needed.
        const roleTip = row.type === AA_TYPE.PatriotSAM
            ? l.t("UI_AA_ROLE_TIP_PATRIOT")
            : l.t("UI_AA_ROLE_TIP_GUN");
        return (
            <HoverTip key={row.type} text={roleTip} style={{ display: "block", marginBottom: "6rem" }}>
                <div style={{ display: "flex", alignItems: "center" }}>
                    <StatRow
                        compact
                        label={l.t(row.labelKey)}
                        value={<span style={{ display: "inline-flex", alignItems: "center" }}>
                            {`${row.ammo}/${row.maxAmmo} ${unitLabel}`}
                            {row.lowAmmo && <LowAmmoMark color={accents.crisis.accent} />}
                        </span>}
                        color={row.color}
                        style={{ flex: 1, minWidth: 0 }}
                    />
                    {inlineButton}
                </div>
                <ProgressBar value={row.percent} color={row.barColor} height="6rem" style={{ marginTop: "2rem" }} />
            </HoverTip>
        );
    };

    return (
        <Column style={{
            flex: 1,
            padding: theme.spacing.md,
            overflowY: "auto",
            overflowX: "hidden",
            minWidth: 0,
        }}>
            <SectionHeader
                title={l.t("DEFENSE_PANEL_TITLE")}
                titleAs="span"
                titleStyle={s.panelHeader(accents.crisis.accent)}
                style={{ flexWrap: "wrap", marginBottom: "6rem" }}
                extra={<>
                    <span style={s.statusBadge(
                        isAAActive ? accents.schemes.accent : accents.crisis.accent,
                        isAAActive
                    )}>
                        {isAAActive ? l.t("STATUS_ACTIVE") : l.t("STATUS_INACTIVE")}
                    </span>
                    {aa.SirenActive && (
                        <span style={s.statusBadge(accents.crisis.accent, true)}>
                            {l.t("AA_SIREN_ACTIVE")}
                        </span>
                    )}
                    {isAAActive && (
                        <span style={{ fontSize: "11rem", color: theme.colors.textSecondary, marginLeft: "4rem" }}>
                            {l.t("UI_DEFENSE_DEPLOYED")} <span style={{ color: theme.colors.textPrimary, fontWeight: 700 }}>{aa.AaStations}</span>
                            <span style={{ margin: "0 4rem", color: theme.colors.border }}>·</span>
                            {l.t("AA_EFFICIENCY")} <span style={{ color: accents.schemes.accent, fontWeight: 700 }}>{Math.round(100 - (aa.SpotterPenaltyPercent ?? 0))}%</span>
                        </span>
                    )}
                </>}
                help={<HelpSection id="defense" title={l.t("DEFENSE_PANEL_TITLE")}>{l.t("HELP_DEFENSE")}</HelpSection>}
            />

            {isAAActive ? (
                <>
                    {/* Deployed + Efficiency now live inline in the panel header (extra slot). */}
                    {/* Patriot keeps its own inline button (dear, cooldown, no auto refill). */}
                    {patriotRows.map((row) => renderAmmoBar(
                        row,
                        patriotResupply.isNeeded ? (
                            <ResupplyButton
                                desc={patriotResupply}
                                action={patriotResupplyAction}
                                accentColor={accents.resilience.accent}
                            />
                        ) : null,
                    ))}

                    {/* Gun types (Heritage/Gepard/Bofors) share ONE resupply button, vertically
                        centered against the whole gun group and tied to it with a bracket — one
                        click refills every gun type at the summed cost. */}
                    {gunRows.length > 0 && (
                        gunsResupply.isNeeded ? (
                            <div style={{ display: "flex", alignItems: "stretch" }}>
                                <div style={{ flex: 1, minWidth: 0 }}>
                                    {gunRows.map((row) => renderAmmoBar(row, null))}
                                </div>
                                <div style={{
                                    width: "6rem",
                                    marginLeft: "8rem",
                                    borderTop: `2rem solid ${theme.colors.border}`,
                                    borderRight: `2rem solid ${theme.colors.border}`,
                                    borderBottom: `2rem solid ${theme.colors.border}`,
                                    borderTopRightRadius: "4rem",
                                    borderBottomRightRadius: "4rem",
                                }} />
                                <div style={{ display: "flex", alignItems: "center" }}>
                                    <ResupplyButton
                                        desc={gunsResupply}
                                        action={gunsResupplyAction}
                                        accentColor={accents.resilience.accent}
                                        label={l.t("UI_RESUPPLY")}
                                    />
                                </div>
                            </div>
                        ) : (
                            gunRows.map((row) => renderAmmoBar(row, null))
                        )
                    )}
                </>
            ) : (
                <div style={s.noData}>{l.t("AA_NO_COVERAGE")}</div>
            )}

            <AABuildSection
                aa={aa}
                actions={actions}
                bofors={bofors}
                gepard={gepard}
                patriot={patriot}
                styles={s}
            />
        </Column>
    );
};
