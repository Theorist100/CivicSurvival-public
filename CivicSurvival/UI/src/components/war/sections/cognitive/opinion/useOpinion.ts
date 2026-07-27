/**
 * useOpinion — live Opinion Board data.
 *
 * Reads the per-stratum opinion lanes (CognitiveStatsState read model) and the
 * live discrete PSYOPS contacts (PsyOpsAttack entities) from their JSON-array
 * bindings, then overlays them onto the static wealth scaffold. The wealth axis
 * is the only lens (battle axis); it is always live, never a template.
 * Display-only — pause-safe (reads bindings, no simulation dependency).
 */

import { useMemo } from "react";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import {
    cognitiveStrata$,
    cognitivePsyOps$,
    isCognitiveStratumDto,
    isCognitivePsyOpsDto,
} from "@hooks/bindings/coreBindings";
import {
    type CognitiveStratumEntry,
    type CognitivePsyOpsEntry,
} from "../../../../../types/domainDtos.generated";
import { buildLiveWealthAxis, buildLiveRaids, type Axis, type LiveRaid } from "./opinionData";

export interface OpinionData {
    /** Lens axes — wealth-only for now (the single live battle axis). */
    axes: readonly Axis[];
    /** The live wealth battle axis (always present; zero-state before classification). */
    battleAxis: Axis;
    /** Live PSYOPS contacts across all strata. */
    raids: readonly LiveRaid[];
    /** True once the backend has classified at least one stratum lane (Count > 0). */
    hasLiveStrata: boolean;
}

export function useOpinion(): OpinionData {
    const strataRaw = useSafeJsonArray(cognitiveStrata$, [], "cognitiveStrata");
    const psyOpsRaw = useSafeJsonArray(cognitivePsyOps$, [], "cognitivePsyOps");

    const strata = useMemo(
        () => strataRaw.filter(isCognitiveStratumDto) as CognitiveStratumEntry[],
        [strataRaw]
    );
    const psyOps = useMemo(
        () => psyOpsRaw.filter(isCognitivePsyOpsDto) as CognitivePsyOpsEntry[],
        [psyOpsRaw]
    );

    const raids = useMemo(() => buildLiveRaids(psyOps), [psyOps]);

    const battleAxis = useMemo(() => buildLiveWealthAxis(strata, raids), [strata, raids]);

    const axes = useMemo(() => [battleAxis], [battleAxis]);

    return { axes, battleAxis, raids, hasLiveStrata: strata.some((e) => e.Count > 0) };
}
