/**
 * Enriched endgame modal data.
 *
 * One computed layer for Defeat, Victory, and WarFatigue modals. The raw
 * scenario hook remains the binding layer; modal components consume this
 * enriched shape to avoid repeating defeat-cause and stat formatting logic.
 */

import { useMemo } from "react";
import { DefeatCauseType, type DefeatCauseValue } from "../bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { useDurationFormatter } from "../format";
import { useEndgameScenario } from "./useEndgameScenario";
import { assertNever } from "../../utils/exhaustive";

export type EndgameDefeatCause =
    | { kind: "none"; title: ""; description: ""; quote: "" }
    | { kind: "population"; title: string; description: string; quote: string }
    | { kind: "arrested"; title: string; description: string; quote: string }
    | { kind: "integrity"; title: string; description: string; quote: string };

const NO_DEFEAT_CAUSE: EndgameDefeatCause = {
    kind: "none",
    title: "",
    description: "",
    quote: "",
};

function isKnownDefeatCause(cause: number): cause is DefeatCauseValue {
    return cause === DefeatCauseType.None ||
        cause === DefeatCauseType.PopulationCollapse ||
        cause === DefeatCauseType.Arrested ||
        cause === DefeatCauseType.LostControl;
}

function createKnownDefeatCause(l: ReturnType<typeof useLocale>, cause: DefeatCauseValue): EndgameDefeatCause {
    switch (cause) {
        case DefeatCauseType.None:
            return NO_DEFEAT_CAUSE;
        case DefeatCauseType.PopulationCollapse:
            return {
                kind: "population",
                title: l.t("MODAL_DEFEAT_CAUSE_POPULATION"),
                description: l.t("MODAL_DEFEAT_DESC_POPULATION"),
                quote: l.t("MODAL_DEFEAT_QUOTE_POPULATION"),
            };
        case DefeatCauseType.Arrested:
            return {
                kind: "arrested",
                title: l.t("MODAL_DEFEAT_CAUSE_ARRESTED"),
                description: l.t("MODAL_DEFEAT_DESC_ARRESTED"),
                quote: l.t("MODAL_DEFEAT_QUOTE_ARRESTED"),
            };
        case DefeatCauseType.LostControl:
            return {
                kind: "integrity",
                title: l.t("MODAL_DEFEAT_CAUSE_INTEGRITY"),
                description: l.t("MODAL_DEFEAT_DESC_INTEGRITY"),
                quote: l.t("MODAL_DEFEAT_QUOTE_INTEGRITY"),
            };
        default:
            return assertNever(cause, "createDefeatCause.cause");
    }
}

function createDefeatCause(l: ReturnType<typeof useLocale>, cause: ReturnType<typeof useEndgameScenario>["defeatCause"]): EndgameDefeatCause {
    if (!isKnownDefeatCause(cause)) return NO_DEFEAT_CAUSE;
    return createKnownDefeatCause(l, cause);
}

export function useEndgameData() {
    const l = useLocale();
    const format = useDurationFormatter();
    const raw = useEndgameScenario();

    return useMemo(() => {
        const defeatCause = createDefeatCause(l, raw.defeatCause);

        return {
            raw,
            defeatCause,
            populationPercentRaw: raw.populationPercent,
            populationPercentDisplay: format.percent(raw.populationPercent, 0, "ratio"),
            refugeesDisplay: raw.totalRefugees.toLocaleString("en-US"),
            survivalSummary: format.days(raw.daysSurvived),
            interceptionRate: raw.wavesDefended > 0 ? raw.missilesIntercepted / raw.wavesDefended : 0,
            showBuildingsDamaged: raw.buildingsDamaged > 0,
        };
    }, [raw, l, format]);
}
