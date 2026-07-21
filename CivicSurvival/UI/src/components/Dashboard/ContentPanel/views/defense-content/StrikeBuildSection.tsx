import React from "react";
import { Flex } from "@coherent";
import { useAccents, useTheme, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { scLog } from "../../../../../utils/logging";
import { useRequestAction } from "../../../../../hooks/actions";
import { bindingDataOrDefault, useGridWarfareDomain } from "@hooks/domain";
import { DEFAULT_GRID_WARFARE_DTO } from "../../../../../types/domainDtos";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "@hooks/bindingNames.generated";

/** Mirrors the AA tile geometry (AABuildSection): four slots a row, gap via Flex margins. */
const TILES_PER_ROW = 4;

/**
 * Strike-unit slugs. The drone launcher is the one placeable unit today; the ballistic
 * launcher is a declared slot whose hardware (the .cok) is still in production — it renders
 * dimmed and selectable-but-won't-place, so the card can say why (the AA locked-tile
 * convention). No icon jpg exists yet for either; the tiles carry inline silhouettes until
 * a thumbnail lands in Assets/Icons.
 */
type StrikeUnit = "drone" | "ballistic";

const DroneLauncherGlyph: React.FC<{ accent: string; body: string }> = ({ accent, body }) => (
    <svg viewBox="0 0 56 44" style={{ width: "56rem", height: "44rem" }} aria-hidden="true">
        <rect x="3" y="28" width="50" height="9" rx="2" fill={body} />
        <rect x="12" y="12" width="34" height="13" rx="2" transform="rotate(-9 29 18)" fill={accent} />
        <line x1="20" y1="14" x2="20" y2="22" stroke={body} strokeWidth="2" />
        <line x1="28" y1="13" x2="28" y2="21" stroke={body} strokeWidth="2" />
        <line x1="36" y1="12" x2="36" y2="20" stroke={body} strokeWidth="2" />
        <circle cx="11" cy="39" r="3.4" fill="none" stroke={body} strokeWidth="2" />
        <circle cx="27" cy="39" r="3.4" fill="none" stroke={body} strokeWidth="2" />
        <circle cx="45" cy="39" r="3.4" fill="none" stroke={body} strokeWidth="2" />
    </svg>
);

const BallisticLauncherGlyph: React.FC<{ body: string }> = ({ body }) => (
    <svg viewBox="0 0 56 44" style={{ width: "56rem", height: "44rem" }} aria-hidden="true">
        <rect x="3" y="29" width="50" height="8" rx="2" fill={body} />
        <rect x="18" y="3" width="11" height="26" rx="2" transform="rotate(-7 23 16)" fill={body} />
        <circle cx="11" cy="40" r="3.2" fill="none" stroke={body} strokeWidth="2" />
        <circle cx="28" cy="40" r="3.2" fill="none" stroke={body} strokeWidth="2" />
        <circle cx="45" cy="40" r="3.2" fill="none" stroke={body} strokeWidth="2" />
    </svg>
);

/**
 * STRIKE half of the DEFENSE tab's build section. Same take-in-hand convention as the AA
 * tiles: clicking the drone-launcher tile activates the vanilla placement tool (sync,
 * pause-safe — the panel does not cover the map, so the tool works right here). The C#
 * trigger is declared Supersede, so a re-click while a placement is pending cancels the
 * open request and re-arms the tool — no separate cancel wiring needed.
 */
export const StrikeBuildSection: React.FC = () => {
    const accents = useAccents();
    const theme = useTheme();
    const l = useLocale();
    const gw = bindingDataOrDefault(useGridWarfareDomain(), DEFAULT_GRID_WARFARE_DTO);

    const [selected, setSelected] = React.useState<StrikeUnit>("drone");

    const placementAction = useRequestAction(() => {
        scLog("[CivicSurvival] Place drone launcher (DEFENSE tab, STRIKE mode)");
        triggerCivic(B.PlaceDroneLauncher);
        return true;
    }, gw.DroneLauncherPlacementRequest);
    const placementPending = placementAction.isPending;

    const accent = accents.schemes.accent;

    const stat = (label: string, value: string, color?: string): React.ReactNode => (
        <div style={{
            display: "flex",
            justifyContent: "space-between",
            // flex-end, not baseline: Coherent rejects baseline (falls back to stretch)
            alignItems: "flex-end",
            padding: "4rem 0",
            borderBottom: `1rem solid ${theme.colors.borderLight}`,
            fontSize: "12rem",
        }}>
            <span style={{ color: theme.colors.textMuted }}>{label}</span>
            <span style={{ color: color ?? theme.colors.textPrimary, fontWeight: 600 }}>{value}</span>
        </div>
    );

    const tile = (
        unit: StrikeUnit,
        name: string,
        sub: string,
        placeable: boolean,
        glyph: React.ReactNode,
    ): React.ReactNode => {
        const active = selected === unit;
        // Shared by click and keyboard: Coherent elements have no HTMLElement.click(),
        // so onKeyDown must not synthesize a click.
        const activate = () => {
            setSelected(unit);
            if (!placeable) return;
            if (placementPending) {
                // Supersede on the C# side: the direct trigger cancels the open
                // placement and re-arms the tool (the AA switch-tile precedent).
                scLog("[CivicSurvival] Re-arm drone launcher placement");
                triggerCivic(B.PlaceDroneLauncher);
                return;
            }
            placementAction.execute();
        };
        return (
            <div key={unit} style={{ flexBasis: `${(100 / TILES_PER_ROW - 2).toFixed(3)}%` }}>
                <div
                    role="button"
                    tabIndex={0}
                    aria-pressed={active}
                    title={name}
                    onClick={activate}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            activate();
                        }
                    }}
                    style={{
                        backgroundColor: active ? theme.colors.paperHover : theme.colors.paper,
                        border: `1rem solid ${active ? accent : theme.colors.border}`,
                        borderRadius: "2rem",
                        padding: "6rem 4rem",
                        textAlign: "center" as const,
                        cursor: "pointer",
                        // Dimmed = "selectable but won't place" (awaiting hardware), matching
                        // the AA locked-tile convention: the click still selects, so the card
                        // below can explain WHY it will not place.
                        opacity: placeable ? 1 : 0.55,
                    }}
                >
                    <div style={{
                        width: "60rem",
                        height: "50rem",
                        margin: "0 auto",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                    }}>
                        {glyph}
                    </div>
                    <div style={{
                        fontSize: "11rem",
                        fontWeight: 600,
                        color: active ? accent : theme.colors.textPrimary,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap" as const,
                    }}>
                        {name}
                    </div>
                    <div style={{ fontSize: "10rem", color: theme.colors.textMuted }}>{sub}</div>
                </div>
            </div>
        );
    };

    const droneSelected = selected === "drone";

    return (
        <>
            <div style={{
                fontSize: "11rem",
                letterSpacing: "1rem",
                textTransform: "uppercase",
                color: accent,
                marginBottom: "6rem",
            }}>
                {l.t("STRIKE_BUILD_TITLE")}
            </div>

            <Flex wrap="wrap" gap="6rem">
                {tile(
                    "drone",
                    l.t("UI_STRIKE_DRONE_LAUNCHER"),
                    l.t("UI_STRIKE_LAUNCHES_DRONES"),
                    true,
                    <DroneLauncherGlyph accent={accent} body={theme.colors.textMuted} />,
                )}
                {tile(
                    "ballistic",
                    l.t("UI_STRIKE_BALLISTIC_LAUNCHER"),
                    l.t("UI_STRIKE_AWAITING_HARDWARE"),
                    false,
                    <BallisticLauncherGlyph body={theme.colors.textMuted} />,
                )}
            </Flex>

            {/* One detail card, mirroring the AA card's anatomy. */}
            <div style={{
                marginTop: "8rem",
                backgroundColor: theme.colors.paper,
                border: `1rem solid ${theme.colors.border}`,
                borderRadius: "2rem",
                padding: "8rem",
            }}>
                <div style={{ fontSize: "14rem", fontWeight: 700, color: theme.colors.textPrimary }}>
                    {droneSelected ? l.t("UI_STRIKE_DRONE_LAUNCHER") : l.t("UI_STRIKE_BALLISTIC_LAUNCHER")}
                </div>

                {droneSelected ? (
                    <>
                        {stat(l.t("UI_STRIKE_STAT_LAUNCHES"), l.t("UI_STRIKE_LAUNCHES_DRONES"))}
                        {stat(l.t("UI_DEFENSE_DEPLOYED"), String(gw.DroneLauncherCount))}
                        {/* Name the purse (the AA card precedent): this money leaves the shadow
                            wallet, and colour alone is not a label. */}
                        {stat(
                            l.t("AA_COST"),
                            l.t("UI_AA_COST_SHADOW", formatCostArg(gw.DroneLauncherCost)),
                            accent,
                        )}
                        <div style={{
                            marginTop: "8rem",
                            fontSize: "11rem",
                            color: theme.colors.textMuted,
                            lineHeight: 1.45,
                        }}>
                            {l.t("UI_STRIKE_LAUNCHER_NOTE")}
                        </div>
                        {placementAction.failureReasonId ? (
                            <div style={{
                                marginTop: "8rem",
                                padding: "7rem",
                                border: `1rem solid ${theme.colors.border}`,
                                borderRadius: "2rem",
                                color: theme.colors.textMuted,
                                fontSize: "11rem",
                                fontWeight: 600,
                                textTransform: "uppercase" as const,
                                textAlign: "center" as const,
                            }}>
                                {l.tDynamic(placementAction.failureReasonId)}
                            </div>
                        ) : null}
                    </>
                ) : (
                    <>
                        <div style={{
                            marginTop: "8rem",
                            fontSize: "11rem",
                            color: theme.colors.textMuted,
                            lineHeight: 1.45,
                        }}>
                            {l.t("UI_STRIKE_BALLISTIC_NOTE")}
                        </div>
                        {/* The refusal strip where the AA card carries its locked reason. */}
                        <div style={{
                            marginTop: "8rem",
                            padding: "7rem",
                            border: `1rem solid ${theme.colors.border}`,
                            borderRadius: "2rem",
                            color: theme.colors.textMuted,
                            fontSize: "11rem",
                            fontWeight: 600,
                            textTransform: "uppercase" as const,
                            textAlign: "center" as const,
                        }}>
                            {l.t("UI_STRIKE_AWAITING_HARDWARE")}
                        </div>
                    </>
                )}
            </div>
        </>
    );
};
