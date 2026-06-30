import React from "react";
import { useAccents, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { scLog } from "../../../../../utils/logging";
import { useRequestAction } from "../../../../../hooks/actions";
import type { RequestResult } from "../../../../../types/dtoSubTypes";
import { type useDefenseActions } from "@hooks/actions";
import type { BoforsValidator, GepardValidator, PatriotValidator } from "./types";
import { AABuildCard } from "./AABuildCard";
import { ICON_HOST } from "@shared/common/iconHost";

interface AABuildSectionProps {
    aa: {
        HeritageCredits?: number;
        DonorPatriotCredits?: number;
        HeritageCrew?: number;
        PatriotCrew?: number;
        BoforsCrew?: number;
        GepardCrew?: number;
        CanPlaceHeritageBofors?: boolean;
        HeritageBoforsLockedReasonId?: string;
        CanPlaceDonorPatriot?: boolean;
        DonorPatriotLockedReasonId?: string;
        PaidBoforsAffordableCount?: number;
        PaidGepardAffordableCount?: number;
        PaidPatriotAffordableCount?: number;
        AirDefensePlacementRequest?: RequestResult;
    };
    actions: ReturnType<typeof useDefenseActions>;
    bofors: BoforsValidator;
    gepard: GepardValidator;
    patriot: PatriotValidator;
    styles: {
        divider: React.CSSProperties;
        subsectionTitle: (color: string) => React.CSSProperties;
        buildingCard: React.CSSProperties;
        buildingThumbnail: React.CSSProperties;
        buildingInfo: React.CSSProperties;
        buildingName: React.CSSProperties;
        buildingStats: React.CSSProperties;
        buildingStat: React.CSSProperties;
        buildingStatValue: (color: string) => React.CSSProperties;
        buildingPlaceButton: (color: string) => React.CSSProperties;
    };
}

export const AABuildSection: React.FC<AABuildSectionProps> = ({
    aa,
    actions,
    bofors,
    gepard,
    patriot,
    styles: s
}) => {
    const accents = useAccents();
    const l = useLocale();
    const heritageCredits = aa.HeritageCredits ?? 0;
    const donorPatriotCredits = aa.DonorPatriotCredits ?? 0;
    const hasHeritage = heritageCredits > 0;
    const hasDonorPatriot = donorPatriotCredits > 0;
    const heritageDisabled = !aa.CanPlaceHeritageBofors;
    const donorDisabled = !aa.CanPlaceDonorPatriot;
    const heritageButtonText = heritageDisabled && aa.HeritageBoforsLockedReasonId
        ? l.tDynamic(aa.HeritageBoforsLockedReasonId)
        : l.t("AA_PLACE");
    const donorButtonText = donorDisabled && aa.DonorPatriotLockedReasonId
        ? l.tDynamic(aa.DonorPatriotLockedReasonId)
        : l.t("AA_PLACE");
    const paidButtonText = bofors.canPlace
        ? l.t("AA_PLACE")
        : bofors.reasonId
            ? l.tDynamic(bofors.reasonId)
            : l.t("UI_INSUFFICIENT_FUNDS");
    const paidGepardButtonText = gepard.canPlace
        ? l.t("AA_PLACE")
        : gepard.reasonId
            ? l.tDynamic(gepard.reasonId)
            : l.t("UI_INSUFFICIENT_FUNDS");
    const paidPatriotButtonText = patriot.canPlace
        ? l.t("AA_PLACE")
        : patriot.reasonId
            ? l.tDynamic(patriot.reasonId)
            : l.t("UI_INSUFFICIENT_FUNDS");
    const placementActionRef = React.useRef<() => boolean>(() => false);
    const placementAction = useRequestAction(() => placementActionRef.current(), aa.AirDefensePlacementRequest);
    const placementPending = placementAction.isPending || aa.AirDefensePlacementRequest?.Status === "pending";
    const runPlacementAction = React.useCallback((action: () => void) => {
        if (placementPending) return;
        placementActionRef.current = () => {
            action();
            return true;
        };
        placementAction.execute();
    }, [placementAction, placementPending]);

    const costLabel = l.t("AA_COST");
    const crewLabel = l.t("AA_CREW");
    const freeText = l.t("UI_FREE");
    const paidBoforsAffordable = aa.PaidBoforsAffordableCount ?? 0;
    const paidGepardAffordable = aa.PaidGepardAffordableCount ?? 0;
    const paidPatriotAffordable = aa.PaidPatriotAffordableCount ?? 0;

    return (
        <>
            <div style={{ ...s.divider, margin: "6rem 0" }} />
            <div style={{ ...s.subsectionTitle(accents.operations.accent), marginBottom: "6rem" }}>
                {l.t("AA_BUILD_TITLE")}
            </div>

            {hasHeritage && (
                <AABuildCard
                    title={l.t("UI_AA_HERITAGE_BOFORS")}
                    thumbnail={`${ICON_HOST}buildings/40mm.jpg`}
                    badgeText={l.t("UI_HERITAGE_REMAINING", heritageCredits)}
                    badgeColor={accents.schemes.accent}
                    costLabel={costLabel}
                    costText={freeText}
                    costColor={accents.schemes.accent}
                    crewLabel={crewLabel}
                    crew={aa.HeritageCrew || 4}
                    crewColor={accents.schemes.accent}
                    buttonText={heritageButtonText}
                    buttonColor={accents.schemes.accent}
                    disabled={heritageDisabled || placementPending}
                    onPlace={() => runPlacementAction(() => {
                        scLog("[CivicSurvival] Place AA: Heritage Bofors (free)");
                        actions.placeAABuilding({ prefab: "AA_40mm_Bofors", mode: "Heritage" });
                    })}
                    styles={s}
                />
            )}

            {hasDonorPatriot && (
                <AABuildCard
                    title={l.t("UI_AA_PATRIOT_SAM")}
                    thumbnail={`${ICON_HOST}buildings/Patriot.jpg`}
                    badgeText={l.t("UI_DONOR_CREDIT_REMAINING", donorPatriotCredits)}
                    badgeColor={accents.crisis.accent}
                    costLabel={costLabel}
                    costText={freeText}
                    costColor={accents.schemes.accent}
                    crewLabel={crewLabel}
                    crew={aa.PatriotCrew || 15}
                    crewColor={accents.crisis.accent}
                    buttonText={donorButtonText}
                    buttonColor={accents.crisis.accent}
                    disabled={donorDisabled || placementPending}
                    onPlace={() => runPlacementAction(() => {
                        scLog("[CivicSurvival] Place AA: Patriot SAM (donor credit)");
                        actions.placeAABuilding({ prefab: "MIM104_SAM", mode: "DonorCredit" });
                    })}
                    styles={s}
                />
            )}

            <AABuildCard
                title={l.t("UI_AA_BOFORS_L60")}
                thumbnail={`${ICON_HOST}buildings/40mm.jpg`}
                badgeText={l.t("UI_AA_AFFORDABLE", paidBoforsAffordable)}
                badgeColor={paidBoforsAffordable > 0 ? accents.operations.accent : accents.crisis.accent}
                costLabel={costLabel}
                costText={l.t("UI_COST_FORMAT", formatCostArg(bofors.cost))}
                costColor={bofors.canPlace ? accents.resilience.accent : accents.crisis.accent}
                crewLabel={crewLabel}
                crew={aa.BoforsCrew || 6}
                crewColor={accents.operations.accent}
                buttonText={paidButtonText}
                buttonColor={bofors.canPlace ? accents.operations.accent : accents.crisis.accent}
                disabled={!bofors.canPlace || placementPending}
                onPlace={() => runPlacementAction(() => {
                    scLog("[CivicSurvival] Place AA: 40mm Bofors (paid)");
                    actions.placeAABuilding({ prefab: "AA_40mm_Bofors", mode: "Paid" });
                })}
                styles={s}
            />

            <AABuildCard
                title={l.t("UI_AA_GEPARD")}
                thumbnail={`${ICON_HOST}buildings/Gepard.jpg`}
                badgeText={l.t("UI_AA_AFFORDABLE", paidGepardAffordable)}
                badgeColor={paidGepardAffordable > 0 ? accents.operations.accent : accents.crisis.accent}
                costLabel={costLabel}
                costText={l.t("UI_COST_FORMAT", formatCostArg(gepard.cost))}
                costColor={gepard.canPlace ? accents.resilience.accent : accents.crisis.accent}
                crewLabel={crewLabel}
                crew={aa.GepardCrew || 8}
                crewColor={accents.operations.accent}
                buttonText={paidGepardButtonText}
                buttonColor={gepard.canPlace ? accents.operations.accent : accents.crisis.accent}
                disabled={!gepard.canPlace || placementPending}
                onPlace={() => runPlacementAction(() => {
                    scLog("[CivicSurvival] Place AA: Gepard (paid)");
                    actions.placeAABuilding({ prefab: "Gepard", mode: "Paid" });
                })}
                styles={s}
            />

            <AABuildCard
                title={l.t("UI_AA_PATRIOT_SAM")}
                thumbnail={`${ICON_HOST}buildings/Patriot.jpg`}
                badgeText={l.t("UI_AA_AFFORDABLE", paidPatriotAffordable)}
                badgeColor={paidPatriotAffordable > 0 ? accents.operations.accent : accents.crisis.accent}
                costLabel={costLabel}
                costText={l.t("UI_COST_FORMAT", formatCostArg(patriot.cost))}
                costColor={patriot.canPlace ? accents.resilience.accent : accents.crisis.accent}
                crewLabel={crewLabel}
                crew={aa.PatriotCrew || 15}
                crewColor={accents.crisis.accent}
                buttonText={paidPatriotButtonText}
                buttonColor={accents.crisis.accent}
                disabled={!patriot.canPlace || placementPending}
                onPlace={() => runPlacementAction(() => {
                    scLog("[CivicSurvival] Place AA: Patriot SAM (paid)");
                    actions.placeAABuilding({ prefab: "MIM104_SAM", mode: "Paid" });
                })}
                styles={s}
            />
        </>
    );
};
