import React from "react";
import { Flex } from "@coherent";
import { useAccents, useTheme, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { scLog } from "../../../../../utils/logging";
import { useRequestAction } from "../../../../../hooks/actions";
import type { RequestResult } from "../../../../../types/dtoSubTypes";
import type { AaPlacementOptionEntry } from "../../../../../types/domainDtos.generated";
import { type useDefenseActions } from "@hooks/actions";
import { type AaTile } from "@hooks/domain/useDefenseData";
import { AA_PLACEMENT_MODE, placementModeName } from "../../../../../types/semantic";
import { buildingThumbnail, reportThumbnailRenderFailure } from "@shared/common/buildingThumbnails";

interface AABuildSectionProps {
    /** Every barrel the player can field, grouped by prefab. Built from the backend roster. */
    roster: AaTile[];
    placementRequest: RequestResult | undefined;
    actions: ReturnType<typeof useDefenseActions>;
}

/** How many tiles fit a row. The build column is ~400rem wide (820rem panel, two flex:1 columns),
 *  so four tiles land at ~94rem each — the tightest a short name still reads at. */
const TILES_PER_ROW = 4;

/** Rows before the tile strip starts scrolling instead of pushing the detail card off-panel. */
const MAX_TILE_ROWS = 3;

/**
 * Colour says where an option comes from, and only that. Green is the corruption accent
 * (ACCENT_PRESETS.schemes) and is reserved for shadow sourcing — a grant is gold, a donation is
 * the crisis red the donor conference already uses, a purchase is the operations blue.
 */
function sourceColor(mode: number, accents: ReturnType<typeof useAccents>): string {
    if (mode === AA_PLACEMENT_MODE.Heritage) return accents.vip.accent;
    if (mode === AA_PLACEMENT_MODE.DonorCredit) return accents.crisis.accent;
    if (mode === AA_PLACEMENT_MODE.BlackMarket) return accents.schemes.accent;
    return accents.operations.accent;
}

export const AABuildSection: React.FC<AABuildSectionProps> = ({ roster, placementRequest, actions }) => {
    const accents = useAccents();
    const theme = useTheme();
    const l = useLocale();

    const [selectedPrefab, setSelectedPrefab] = React.useState<string>("");
    const [selectedMode, setSelectedMode] = React.useState<number>(AA_PLACEMENT_MODE.Paid);

    const tile = roster.find((t) => t.prefab === selectedPrefab) ?? roster[0];
    const source: AaPlacementOptionEntry | undefined =
        tile?.sources.find((r) => r.Mode === selectedMode) ?? tile?.headline;

    const placementActionRef = React.useRef<() => boolean>(() => false);
    const placementAction = useRequestAction(() => placementActionRef.current(), placementRequest);
    const placementPending = placementAction.isPending;

    const place = React.useCallback((row: AaPlacementOptionEntry) => {
        if (placementPending) return;
        placementActionRef.current = () => {
            scLog(`[CivicSurvival] Place AA: ${row.Prefab} (${placementModeName(row.Mode)})`);
            actions.placeAABuilding({ prefab: row.Prefab, mode: placementModeName(row.Mode) });
            return true;
        };
        placementAction.execute();
    }, [actions, placementAction, placementPending]);

    if (!tile || !source) return null;

    const accent = sourceColor(source.Mode, accents);
    const isGrant = source.CreditsLeft >= 0;
    const isShadowFunded = source.Mode === AA_PLACEMENT_MODE.BlackMarket;

    // The source row a click on THIS tile would place with: the globally selected funding mode
    // when the barrel offers it, else the barrel's headline. Must match what the detail card
    // will show after the click — the player pays from the purse they see.
    const effectiveSource = (t: AaTile): AaPlacementOptionEntry =>
        t.sources.find((r) => r.Mode === selectedMode) ?? t.headline;

    const tileRows = Math.ceil(roster.length / TILES_PER_ROW);

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

    return (
        <>
            {/* The divider above the build block lives in AACommandColumn now — it sits
                between the ammo bars and the DEFENSE/STRIKE mode toggle. */}
            <div style={{
                fontSize: "11rem",
                letterSpacing: "1rem",
                textTransform: "uppercase",
                color: accents.operations.accent,
                marginBottom: "6rem",
            }}>
                {l.t("AA_BUILD_TITLE")}
            </div>

            {/* Coherent has no display:grid, and it silently ignores an inline CSS gap (probed in
                game 2026-07-17 — see UI_COHERENT_BEST_PRACTICES §4). civic/no-mixed-calc also bars
                a calc() mixing % with rem. Hence: Flex, which turns the gap prop into child
                margins, plus a plain % basis. */}
            <Flex
                wrap="wrap"
                gap="6rem"
                style={tileRows > MAX_TILE_ROWS ? { maxHeight: "220rem", overflowY: "auto" as const } : {}}
            >
                {roster.map((t) => {
                    const active = t.prefab === tile.prefab;
                    const row = effectiveSource(t);
                    // Vanilla take-in-hand convention, three cases:
                    //  - active tile while placing → put the tool away (cancel);
                    //  - other tile while placing → switch the tool. The trigger is sent
                    //    DIRECTLY, bypassing the request-action double-send guard on
                    //    purpose: the server handler is declared Supersede, so the new
                    //    request terminally cancels the open one — the exact flow the
                    //    guard exists to prevent for non-superseding keys;
                    //  - no placement active → select and take in hand. A barrel that
                    //    cannot place only selects; the card names the reason where the
                    //    PLACE button used to be.
                    // Shared by click and keyboard: Coherent elements have no
                    // HTMLElement.click(), so onKeyDown must not synthesize a click.
                    const activate = () => {
                        if (placementPending && active) {
                            actions.cancelAAPlacement();
                            return;
                        }
                        setSelectedPrefab(t.prefab);
                        if (row.Mode !== selectedMode) {
                            setSelectedMode(row.Mode);
                        }
                        if (!row.CanPlace) return;
                        if (placementPending) {
                            scLog(`[CivicSurvival] Switch AA placement: ${row.Prefab} (${placementModeName(row.Mode)})`);
                            actions.placeAABuilding({ prefab: row.Prefab, mode: placementModeName(row.Mode) });
                            return;
                        }
                        place(row);
                    };
                    // Basis is trimmed by ~2% so the per-row count survives the gap: Coherent renders
                    // the gap as child margins, so a naive 100/N% basis sums PAST 100% and the last
                    // tile wraps (a 4th "Hawk" tile fell to a phantom 2nd row and slid under the detail
                    // card). The trim keeps N tiles on one row; tileRows then matches the real height.
                    return (
                        <div key={t.prefab} style={{ flexBasis: `${(100 / TILES_PER_ROW - 2).toFixed(3)}%` }}>
                        <div
                            role="button"
                            tabIndex={0}
                            aria-pressed={active}
                            title={l.t(t.headline.NameKey as never)}
                            onClick={activate}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    activate();
                                }
                            }}
                            style={{
                                backgroundColor: active ? theme.colors.paperHover : theme.colors.paper,
                                border: `1rem solid ${active ? accents.operations.accent : theme.colors.border}`,
                                borderRadius: "2rem",
                                padding: "6rem 4rem",
                                textAlign: "center" as const,
                                cursor: "pointer",
                                // Dimmed = "selectable but won't place" (locked / no funds), matching
                                // vanilla's greyed toolbar assets. The click still selects, so the
                                // detail card can explain WHY it will not place.
                                opacity: row.CanPlace ? 1 : 0.55,
                            }}
                        >
                            {/* Box aspect matches the source art (~1453x1216 ≈ 1.2:1) so the art is
                                undistorted whether or not the engine honours object-fit — which is
                                NOT verified here, and inline CSS gap already proved that assuming
                                Coherent supports a property costs you. A 100%-wide, short box is
                                what squashed these into smears. */}
                            <img
                                src={buildingThumbnail(t.headline.Icon)}
                                alt={l.t(t.headline.NameKey as never)}
                                onError={() => reportThumbnailRenderFailure(t.headline.Icon)}
                                style={{ width: "60rem", height: "50rem", objectFit: "contain" as const }}
                            />
                            <div style={{
                                fontSize: "11rem",
                                fontWeight: 600,
                                color: active ? accents.operations.accent : theme.colors.textPrimary,
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                whiteSpace: "nowrap" as const,
                            }}>
                                {l.t(t.headline.NameKey as never)}
                            </div>
                            <div style={{ fontSize: "10rem", color: theme.colors.textMuted }}>
                                {/* Active tile shows the SELECTED source's range (e.g. the black-market
                                    +range bonus), matching the detail card below; inactive tiles show the
                                    base headline. Prevents the tile (700 m) disagreeing with the detail
                                    stat (840 m) once a buffed source is picked. */}
                                {`${Math.round(active && source ? source.Range : t.headline.Range)} m`}
                            </div>
                        </div>
                        </div>
                    );
                })}
            </Flex>

            {/* One detail card, however many barrels exist. */}
            <div style={{
                marginTop: "8rem",
                backgroundColor: theme.colors.paper,
                border: `1rem solid ${theme.colors.border}`,
                borderRadius: "2rem",
                padding: "8rem",
            }}>
                <div style={{ fontSize: "14rem", fontWeight: 700, color: theme.colors.textPrimary }}>
                    {l.t(tile.headline.NameKey as never)}
                </div>

                {/* Source segments: same barrel, different origin, different numbers. */}
                {tile.sources.length > 1 && (
                    <Flex gap="4rem" style={{ margin: "6rem 0" }}>
                        {tile.sources.map((row) => {
                            const on = row.Mode === source.Mode;
                            const color = sourceColor(row.Mode, accents);
                            return (
                                <div
                                    key={row.Mode}
                                    role="button"
                                    tabIndex={0}
                                    aria-pressed={on}
                                    onClick={() => setSelectedMode(row.Mode)}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter" || e.key === " ") {
                                            e.preventDefault();
                                            setSelectedMode(row.Mode);
                                        }
                                    }}
                                    style={{
                                        flex: "1 1 0",
                                        border: `1rem solid ${on ? color : theme.colors.border}`,
                                        borderRadius: "2rem",
                                        padding: "5rem 4rem",
                                        fontSize: "10rem",
                                        fontWeight: 700,
                                        textTransform: "uppercase" as const,
                                        textAlign: "center" as const,
                                        color: on ? color : theme.colors.textMuted,
                                        cursor: "pointer",
                                    }}
                                >
                                    {l.t(`UI_AA_SOURCE_${placementModeName(row.Mode).toUpperCase()}` as never)}
                                </div>
                            );
                        })}
                    </Flex>
                )}

                {stat(l.t("UI_AA_STAT_RANGE"), `${Math.round(source.Range)} m`)}
                {stat(l.t("UI_AA_STAT_VS_DRONES"), `${Math.round(source.InterceptShahed * 100)}%`)}
                {source.InterceptBallistic > 0
                    ? stat(l.t("UI_AA_STAT_VS_BALLISTIC"), `${Math.round(source.InterceptBallistic * 100)}%`)
                    : null}
                {stat(l.t("AA_CREW"), String(source.Crew))}
                {stat(l.t("UI_DEFENSE_DEPLOYED"), String(source.Deployed))}
                {/* Name the purse, don't just tint it. The same card shows a budget price and a
                    shadow price one click apart; with "$400k" on both, colour is the only thing
                    telling the player whose money leaves — and colour alone is not a label. */}
                {stat(
                    l.t("AA_COST"),
                    isGrant
                        ? `${l.t("UI_FREE")} · ${source.CreditsLeft}`
                        : isShadowFunded
                            ? l.t("UI_AA_COST_SHADOW", formatCostArg(source.Cost))
                            : l.t("UI_COST_FORMAT", formatCostArg(source.Cost)),
                    accent,
                )}
                {!isGrant && stat(l.t("UI_AA_AFFORDABLE", source.AffordableCount), "", accent)}

                {/* Placement is now on the tiles (vanilla take-in-hand convention) — this strip
                    only exists to carry the refusal reason the PLACE button used to show. Hidden
                    entirely while the selected source can place. */}
                {!source.CanPlace && (
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
                        {source.LockedReasonId
                            ? l.tDynamic(source.LockedReasonId)
                            : l.t("UI_INSUFFICIENT_FUNDS")}
                    </div>
                )}
            </div>
        </>
    );
};
